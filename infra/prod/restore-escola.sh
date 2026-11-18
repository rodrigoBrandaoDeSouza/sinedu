#!/usr/bin/env sh
# Restaura UMA escola a partir de um backup cifrado (banco + bucket), sem tocar nas outras.
#
#   infra/prod/restore-escola.sh /caminho/do/backup/20261118T030000Z escola-x /caminho/da/chave-privada.age
#
# Passos: confere o SHA256SUMS, decifra com a chave privada age, restaura o banco sinedu_t_{slug} (--clean) e
# recoloca os anexos no bucket. A chave da escola no Vault NÃO está no backup: os dados de Saúde/Proteção só voltam
# a ser legíveis se a chave Transit da escola ainda existir (encerramento com crypto-shredding é irreversível — P11).
set -eu
cd "$(dirname "$0")"
. ./.env

DIR=${1:?pasta do backup}
SLUG=${2:?slug da escola}
KEY=${3:?arquivo da chave privada age}
compose() { docker compose -f docker-compose.prod.yml --env-file .env "$@"; }

(cd "$DIR" && sha256sum -c SHA256SUMS --ignore-missing)

db="sinedu_t_$(echo "$SLUG" | tr '-' '_')"
bucket=$(compose exec -T postgres psql -U "$POSTGRES_ADMIN_USER" -At -d sinedu_platform -c "SELECT bucket_name FROM tenants.v_registry WHERE slug = '$SLUG'")
[ -n "$bucket" ] || { echo "Escola $SLUG não existe no registro."; exit 1; }

echo "Coloque a escola em manutenção no painel antes de continuar. Enter para seguir, Ctrl+C para cancelar."
read -r _

age -d -i "$KEY" "$DIR/$SLUG.dump.age" | compose exec -T postgres pg_restore -U "$POSTGRES_ADMIN_USER" -d "$db" --clean --if-exists --no-owner --role="t_$(echo "$SLUG" | tr '-' '_')_owner"

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
age -d -i "$KEY" "$DIR/$SLUG.bucket.tar.age" | tar -C "$TMP" -xf -
docker run --rm --network sinedu_default -e AWS_ACCESS_KEY_ID="$S3_ACCESS_KEY" -e AWS_SECRET_ACCESS_KEY="$S3_SECRET_KEY" \
  -v "$TMP:/in" amazon/aws-cli:2.27.0 --endpoint-url http://storage:8333 s3 cp "/in/$SLUG-bucket" "s3://$bucket" --recursive --quiet

echo "Escola $SLUG restaurada. Rode 'migrate-tenants' pelo painel se o backup for de versão de schema anterior e reative a escola."
