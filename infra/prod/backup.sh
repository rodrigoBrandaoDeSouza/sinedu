#!/usr/bin/env sh
# Backup cifrado POR ESCOLA (F6-08): banco sinedu_t_{slug} + bucket de anexos, cada um num arquivo .age próprio,
# além do banco da plataforma. Cifrado com a chave PÚBLICA age; a privada não fica na VPS.
#
#   infra/prod/backup.sh            # todas as escolas ativas
#   infra/prod/backup.sh escola-x   # só uma escola
#
# Requer: docker compose (serviços do docker-compose.prod.yml), age. Rodar via cron diário, fora do horário escolar.
# Dados de Saúde/Proteção já estão cifrados pela aplicação (envelope com chave no Vault): o backup não os expõe
# mesmo antes da cifragem age — e restaurar sem a chave do Vault não os torna legíveis.
set -eu
cd "$(dirname "$0")"
. ./.env

: "${BACKUP_AGE_RECIPIENT:?defina BACKUP_AGE_RECIPIENT no .env}"
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
OUT="${BACKUP_DIR:-/var/backups/sinedu}/$STAMP"
mkdir -p "$OUT"
umask 077

compose() { docker compose -f docker-compose.prod.yml --env-file .env "$@"; }
psql_admin() { compose exec -T postgres psql -U "$POSTGRES_ADMIN_USER" -At "$@"; }

if [ $# -gt 0 ]; then
  SLUGS="$*"
else
  SLUGS=$(psql_admin -d sinedu_platform -c "SELECT slug FROM tenants.v_registry WHERE status = 'Active' ORDER BY slug")
fi

echo "Plataforma → $OUT/platform.dump.age"
compose exec -T postgres pg_dump -U "$POSTGRES_ADMIN_USER" -Fc sinedu_platform | age -r "$BACKUP_AGE_RECIPIENT" > "$OUT/platform.dump.age"

for slug in $SLUGS; do
  db="sinedu_t_$(echo "$slug" | tr '-' '_')"
  bucket=$(psql_admin -d sinedu_platform -c "SELECT bucket_name FROM tenants.v_registry WHERE slug = '$slug'")
  echo "Escola $slug → banco $db, bucket $bucket"
  compose exec -T postgres pg_dump -U "$POSTGRES_ADMIN_USER" -Fc "$db" | age -r "$BACKUP_AGE_RECIPIENT" > "$OUT/$slug.dump.age"
  docker run --rm --network sinedu_default -e AWS_ACCESS_KEY_ID="$S3_ACCESS_KEY" -e AWS_SECRET_ACCESS_KEY="$S3_SECRET_KEY" \
    -v "$OUT:/out" amazon/aws-cli:2.27.0 --endpoint-url http://storage:8333 s3 cp "s3://$bucket" "/out/$slug-bucket" --recursive --quiet
  tar -C "$OUT" -cf - "$slug-bucket" | age -r "$BACKUP_AGE_RECIPIENT" > "$OUT/$slug.bucket.tar.age"
  rm -rf "${OUT:?}/$slug-bucket"
done

# Integridade: hash de cada arquivo cifrado, para conferir antes de restaurar.
(cd "$OUT" && sha256sum ./*.age > SHA256SUMS)
echo "Backup concluído em $OUT. Copie para fora da VPS (rclone/rsync) e confira o SHA256SUMS."
