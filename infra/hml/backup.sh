#!/usr/bin/env bash
# Backup cifrado da HOMOLOGAÇÃO (F6-08), um arquivo por escola, como em infra/prod/backup.sh:
#   platform.dump.age            banco da plataforma
#   {slug}.dump.age              banco da escola
#   {slug}.bucket.tar.age        anexos da escola
#   infra.tar.age                Vault (chaves Transit das escolas), .env e secrets/ — sem isso, Saúde/Proteção
#                                restaurados ficam ilegíveis (envelope com chave no Vault)
# Tudo cifrado com a chave PÚBLICA age (secrets/backup-recipient.txt). A chave privada NÃO fica na VPS.
#
#   ./backup.sh             # todas as escolas ativas (é o que o cron roda)
#   ./backup.sh escola-x    # só uma escola
# Retenção: apaga backups com mais de BACKUP_KEEP_DAYS (padrão 14) dias.
set -euo pipefail
cd "$(dirname "$0")"
umask 077

RECIPIENT=$(cat secrets/backup-recipient.txt)
ROOT=${BACKUP_DIR:-/var/backups/sinedu-hml}
OUT="$ROOT/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$OUT"
set -a; . ./.env; set +a

compose() { docker compose -f docker-compose.hml.yml --env-file .env "$@"; }
psql_admin() { compose exec -T postgres psql -U sinedu_admin -At "$@"; }
seal() { age -r "$RECIPIENT" > "$1"; }

if [ $# -gt 0 ]; then
  SLUGS="$*"
else
  SLUGS=$(psql_admin -d sinedu_platform -c "SELECT slug FROM tenants.v_registry WHERE status = 'Active' ORDER BY slug")
fi

echo "[backup] plataforma"
compose exec -T postgres pg_dump -U sinedu_admin -Fc sinedu_platform | seal "$OUT/platform.dump.age"

for slug in $SLUGS; do
  [[ "$slug" =~ ^[a-z0-9-]+$ ]] || { echo "slug inválido: $slug"; exit 1; }
  db="sinedu_t_${slug//-/_}"
  bucket=$(psql_admin -d sinedu_platform -c "SELECT bucket_name FROM tenants.v_registry WHERE slug = '$slug'")
  echo "[backup] escola $slug"
  compose exec -T postgres pg_dump -U sinedu_admin -Fc "$db" | seal "$OUT/$slug.dump.age"
  docker run --rm --network sinedu-hml_default -e AWS_ACCESS_KEY_ID="$S3_ACCESS_KEY" -e AWS_SECRET_ACCESS_KEY="$S3_SECRET_KEY" \
    -v "$OUT:/out" amazon/aws-cli:2.27.0 --endpoint-url http://storage:8333 s3 cp "s3://$bucket" "/out/$slug-bucket" --recursive --quiet
  mkdir -p "$OUT/$slug-bucket"
  tar -C "$OUT" -cf - "$slug-bucket" | seal "$OUT/$slug.bucket.tar.age"
  rm -rf "${OUT:?}/$slug-bucket"
done

echo "[backup] Vault e segredos"
docker run --rm -v sinedu-hml_vault-data:/vault:ro alpine:3.22 tar -C /vault -cf - . > "$OUT/vault-data.tar"
tar -cf - .env secrets -C "$OUT" vault-data.tar | seal "$OUT/infra.tar.age"
rm -f "$OUT/vault-data.tar"

(cd "$OUT" && sha256sum ./*.age > SHA256SUMS)
find "$ROOT" -mindepth 1 -maxdepth 1 -type d -mtime +"${BACKUP_KEEP_DAYS:-14}" -exec rm -rf {} +
echo "[backup] concluído: $OUT ($(du -sh "$OUT" | cut -f1))"
