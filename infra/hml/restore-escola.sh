#!/usr/bin/env bash
# Restaura UMA escola da homologação a partir de um backup (banco + anexos), sem tocar nas outras.
#   ./restore-escola.sh /var/backups/sinedu-hml/20261119T033000Z escola-x /caminho/da/chave-privada.age
# A chave privada vem de fora da VPS (copie só para a restauração e apague depois).
# Saúde/Proteção voltam legíveis porque a chave Transit da escola continua no Vault. Se o Vault também se perdeu,
# restaure antes o infra.tar.age (ver guia de operação, seção 6).
set -euo pipefail
cd "$(dirname "$0")"

DIR=${1:?pasta do backup}
SLUG=${2:?slug da escola}
KEY=${3:?arquivo da chave privada age}
[[ "$SLUG" =~ ^[a-z0-9-]+$ ]] || { echo "slug inválido"; exit 1; }
set -a; . ./.env; set +a
compose() { docker compose -f docker-compose.hml.yml --env-file .env "$@"; }

(cd "$DIR" && sha256sum -c SHA256SUMS --ignore-missing --quiet)

db="sinedu_t_${SLUG//-/_}"
bucket=$(compose exec -T postgres psql -U sinedu_admin -At -d sinedu_platform -c "SELECT bucket_name FROM tenants.v_registry WHERE slug = '$SLUG'")
[ -n "$bucket" ] || { echo "Escola $SLUG não existe no registro."; exit 1; }

echo "Suspenda a escola $SLUG no painel antes de continuar. Enter para seguir, Ctrl+C para cancelar."
read -r _

age -d -i "$KEY" "$DIR/$SLUG.dump.age" \
  | compose exec -T postgres pg_restore -U sinedu_admin -d "$db" --clean --if-exists --no-owner --role="t_${SLUG//-/_}_owner"

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
age -d -i "$KEY" "$DIR/$SLUG.bucket.tar.age" | tar -C "$TMP" -xf -
docker run --rm --network sinedu-hml_default -e AWS_ACCESS_KEY_ID="$S3_ACCESS_KEY" -e AWS_SECRET_ACCESS_KEY="$S3_SECRET_KEY" \
  -v "$TMP:/in" amazon/aws-cli:2.27.0 --endpoint-url http://storage:8333 s3 cp "/in/$SLUG-bucket" "s3://$bucket" --recursive --quiet

echo "Escola $SLUG restaurada. Se o backup for de schema anterior, rode a atualização pelo painel; depois reative a escola."
