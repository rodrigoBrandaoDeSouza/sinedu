#!/usr/bin/env bash
# Publica a HOMOLOGAÇÃO na VPS a partir deste computador (Git Bash):
#   infra/hml/deploy.sh            # imagens + fronts + configuração, depois sobe e atualiza escolas
#   infra/hml/deploy.sh --sem-api  # só fronts e configuração
# Requer: Docker local (linux/amd64), Node, acesso SSH "bem-vps".
set -euo pipefail
cd "$(dirname "$0")/../.."
VPS=${VPS:-bem-vps}
REMOTE=/opt/sinedu

if [ "${1:-}" != "--sem-api" ]; then
  echo "[deploy] imagens das APIs"
  docker build -q -f apps/api/Dockerfile --build-arg HOST=Sinedu.Platform.Host -t sinedu/platform:hml . >/dev/null
  docker build -q -f apps/api/Dockerfile --build-arg HOST=Sinedu.Tenant.Host -t sinedu/tenant:hml . >/dev/null
  echo "[deploy] enviando imagens (comprimidas)"
  docker save sinedu/platform:hml sinedu/tenant:hml | gzip -1 | ssh "$VPS" 'gunzip | docker load' | tail -2
fi

echo "[deploy] fronts"
npm run build >/dev/null
ssh "$VPS" "mkdir -p $REMOTE/www $REMOTE/hml/policies"
for app in conta painel web; do
  tar -C "apps/$app/dist" -czf - . | ssh "$VPS" "rm -rf $REMOTE/www/$app.new && mkdir -p $REMOTE/www/$app.new && tar -xzf - -C $REMOTE/www/$app.new && rm -rf $REMOTE/www/$app && mv $REMOTE/www/$app.new $REMOTE/www/$app"
done

echo "[deploy] configuração"
tar -czf - -C infra/hml docker-compose.hml.yml bootstrap.sh nginx-sinedu.conf nginx-sinedu-common.conf nginx-sinedu-proxy.conf nginx-sinedu-tls.conf add-host-cert.sh backup.sh restore-escola.sh \
  | ssh "$VPS" "tar -xzf - -C $REMOTE/hml && chmod +x $REMOTE/hml/*.sh"
tar -czf - -C infra/vault/policies platform.hcl tenant-app.hcl | ssh "$VPS" "tar -xzf - -C $REMOTE/hml/policies"

if ssh "$VPS" "test -f $REMOTE/hml/.env"; then
  echo "[deploy] subindo e atualizando o schema das escolas"
  ssh "$VPS" "cd $REMOTE/hml && docker compose -f docker-compose.hml.yml --env-file .env run --rm platform migrate \
    && docker compose -f docker-compose.hml.yml --env-file .env up -d \
    && docker compose -f docker-compose.hml.yml --env-file .env run --rm platform migrate-tenants | tail -1 \
    && docker image prune -f >/dev/null"
else
  echo "[deploy] primeira vez: rode na VPS  cd $REMOTE/hml && ./bootstrap.sh <email> \"<Nome>\""
fi
echo "[deploy] ok"
