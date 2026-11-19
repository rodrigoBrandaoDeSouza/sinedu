#!/usr/bin/env bash
# Primeira instalação da HOMOLOGAÇÃO na VPS (idempotente: pode rodar de novo).
# Gera segredos, sobe a infraestrutura, inicializa o Vault, migra o banco e cria o primeiro super admin.
#   cd /opt/sinedu/hml && ./bootstrap.sh <email-do-super-admin> "<Nome completo>"
set -euo pipefail
cd "$(dirname "$0")"
umask 077
EMAIL=${1:-}
NAME=${2:-Administração}
DOMAIN=sinedu.bsuite.tech
compose() { docker compose -f docker-compose.hml.yml --env-file .env "$@"; }
rand() { openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c "${1:-32}"; }
mkdir -p secrets

# 1) Segredos (gerados aqui, nunca no Git)
if [ ! -f .env ]; then
  cat > .env <<EOF
SINEDU_DOMAIN=$DOMAIN
POSTGRES_ADMIN_PASSWORD=$(rand)
REGISTRY_READER_PASSWORD=$(rand)
S3_ACCESS_KEY=sinedu$(rand 12)
S3_SECRET_KEY=$(rand 40)
OIDC_TENANT_CLIENT_SECRET=$(rand 40)
OIDC_PANEL_CLIENT_SECRET=$(rand 40)
OIDC_CERTIFICATE_PASSWORD=$(rand)
VAULT_PLATFORM_TOKEN=
VAULT_TENANT_TOKEN=
EOF
  echo "[bootstrap] .env gerado"
fi
set -a; . ./.env; set +a

if [ ! -f secrets/s3.json ]; then
  cat > secrets/s3.json <<EOF
{"identities":[{"name":"sinedu","credentials":[{"accessKey":"$S3_ACCESS_KEY","secretKey":"$S3_SECRET_KEY"}],"actions":["Admin","Read","List","Tagging","Write"]}]}
EOF
fi

for kind in signing encryption; do
  if [ ! -f "secrets/oidc-$kind.pfx" ]; then
    usage=$([ "$kind" = signing ] && echo digitalSignature || echo keyEncipherment)
    openssl req -x509 -newkey rsa:3072 -sha256 -days 730 -nodes -subj "/CN=Sinedu OIDC $kind (hml)" \
      -addext "keyUsage=critical,$usage" -keyout "secrets/oidc-$kind.key" -out "secrets/oidc-$kind.crt" 2>/dev/null
    openssl pkcs12 -export -inkey "secrets/oidc-$kind.key" -in "secrets/oidc-$kind.crt" -out "secrets/oidc-$kind.pfx" -passout "pass:$OIDC_CERTIFICATE_PASSWORD"
    rm -f "secrets/oidc-$kind.key"
    echo "[bootstrap] certificado OIDC ($kind) gerado"
  fi
done
# Os contêineres .NET rodam como usuário 'app' (uid 1654): precisam ler os PFX.
chown 1654:1654 secrets/oidc-*.pfx && chmod 400 secrets/oidc-*.pfx

if [ ! -f /etc/nginx/sinedu-mailpit.htpasswd ]; then
  MAIL_PASSWORD=$(rand 20)
  # Fora de secrets/ (700): o Nginx (grupo www-data) precisa ler.
  printf 'sinedu:%s\n' "$(openssl passwd -apr1 "$MAIL_PASSWORD")" > /etc/nginx/sinedu-mailpit.htpasswd
  chown root:www-data /etc/nginx/sinedu-mailpit.htpasswd && chmod 640 /etc/nginx/sinedu-mailpit.htpasswd
  echo "$MAIL_PASSWORD" > secrets/mailpit-password.txt
  echo "[bootstrap] senha do Mailpit em secrets/mailpit-password.txt (usuário: sinedu)"
fi
touch secrets/vault-unseal-key

# 2) Infraestrutura
compose up -d postgres vault storage mailpit
until compose exec -T postgres pg_isready -U sinedu_admin >/dev/null 2>&1; do sleep 2; done
# vault status: 0 = destravado, 2 = travado (ambos significam que respondeu); 1 = ainda subindo.
until rc=0; compose exec -T -e VAULT_ADDR=http://127.0.0.1:8200 vault vault status >/dev/null 2>&1 || rc=$?; [ "$rc" -ne 1 ]; do sleep 2; done

# 3) Vault: init (1 chave, homologação), unseal, engines, políticas e tokens dos hosts
export VAULT_ADDR=http://127.0.0.1:8200
vexec() { compose exec -T -e VAULT_ADDR=http://127.0.0.1:8200 -e VAULT_TOKEN="${ROOT_TOKEN:-}" vault vault "$@"; }
VSTATUS=$(vexec status -format=json || true)
if ! echo "$VSTATUS" | python3 -c 'import json,sys; sys.exit(0 if json.load(sys.stdin)["initialized"] else 1)'; then
  vexec operator init -key-shares=1 -key-threshold=1 -format=json > secrets/vault-init.json
  python3 -c 'import json; print(json.load(open("secrets/vault-init.json"))["unseal_keys_b64"][0])' > secrets/vault-unseal-key
  echo "[bootstrap] Vault inicializado (chaves em secrets/vault-init.json — guarde uma cópia fora da VPS)"
fi
ROOT_TOKEN=$(python3 -c 'import json; print(json.load(open("secrets/vault-init.json"))["root_token"])')
vexec operator unseal "$(cat secrets/vault-unseal-key)" >/dev/null || true
vexec secrets list -format=json | grep -q '"transit/"' || vexec secrets enable transit
vexec secrets list -format=json | grep -q '"secret/"' || vexec secrets enable -path=secret kv-v2
vexec read transit/keys/platform-secrets >/dev/null 2>&1 || vexec write -f transit/keys/platform-secrets type=aes256-gcm96
compose cp policies/platform.hcl vault:/tmp/platform.hcl
compose cp policies/tenant-app.hcl vault:/tmp/tenant-app.hcl
vexec policy write sinedu-platform /tmp/platform.hcl
vexec policy write sinedu-tenant-app /tmp/tenant-app.hcl
if [ -z "${VAULT_PLATFORM_TOKEN}" ]; then
  P=$(vexec token create -orphan -policy=sinedu-platform -ttl=87600h -display-name=sinedu-platform -field=token)
  T=$(vexec token create -orphan -policy=sinedu-tenant-app -ttl=87600h -display-name=sinedu-tenant -field=token)
  sed -i "s|^VAULT_PLATFORM_TOKEN=.*|VAULT_PLATFORM_TOKEN=$P|; s|^VAULT_TENANT_TOKEN=.*|VAULT_TENANT_TOKEN=$T|" .env
  set -a; . ./.env; set +a
  echo "[bootstrap] tokens do Vault emitidos (políticas da plataforma e da escola)"
fi
compose up -d vault-unsealer

# 4) Banco da plataforma, papel de leitura do registro e aplicação
compose run --rm platform migrate
compose exec -T postgres psql -U sinedu_admin -d sinedu_platform -v ON_ERROR_STOP=1 <<SQL
DO \$\$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sinedu_registry_reader') THEN
    CREATE ROLE sinedu_registry_reader LOGIN PASSWORD '$REGISTRY_READER_PASSWORD';
  END IF;
END \$\$;
GRANT CONNECT ON DATABASE sinedu_platform TO sinedu_registry_reader;
GRANT USAGE ON SCHEMA tenants TO sinedu_registry_reader;
GRANT SELECT ON tenants.v_registry TO sinedu_registry_reader;
SQL
compose up -d platform tenant

# 5) Primeiro super admin (só se ainda não houver)
if [ -n "$EMAIL" ]; then
  compose run --rm platform create-super-admin "$EMAIL" "$NAME" || echo "[bootstrap] super admin já existe — nada a fazer"
else
  echo "[bootstrap] sem e-mail: crie o super admin depois com  docker compose -f docker-compose.hml.yml --env-file .env run --rm platform create-super-admin <email> \"<Nome>\""
fi
echo "[bootstrap] concluído. Convite no Mailpit: https://mail.$DOMAIN"
