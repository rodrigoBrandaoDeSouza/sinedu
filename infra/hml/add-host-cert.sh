#!/usr/bin/env bash
# Instala o site do Sinedu no Nginx e emite/expande o certificado Let's Encrypt (webroot) com nomes explícitos.
# O DNS da Hostinger não tem integração para certificado curinga, então cada escola nova entra aqui:
#   ./add-host-cert.sh                      # conta, painel e mail
#   ./add-host-cert.sh escola-demo outra    # + subdomínios de escolas
# Duas etapas: com o certificado ainda inexistente, instala só o bloco HTTP (desafio), emite, e então o site completo.
set -euo pipefail
cd "$(dirname "$0")"
DOMAIN=sinedu.bsuite.tech
CERT=$DOMAIN
SITE=/etc/nginx/sites-available/sinedu
mkdir -p /var/www/certbot

names=(conta painel mail "$@")
if [ -f "/etc/letsencrypt/live/$CERT/cert.pem" ]; then
  while read -r n; do names+=("${n%%.$DOMAIN}"); done < <(openssl x509 -in "/etc/letsencrypt/live/$CERT/cert.pem" -noout -ext subjectAltName | grep -oE "[a-z0-9-]+\.$DOMAIN")
else
  # Etapa 1: só HTTP, para o Let's Encrypt validar.
  cat > "$SITE" <<EOF
server {
    listen 80;
    listen [::]:80;
    server_name $DOMAIN *.$DOMAIN;
    location /.well-known/acme-challenge/ { root /var/www/certbot; }
    location / { return 503; }
}
EOF
  ln -sf "$SITE" /etc/nginx/sites-enabled/sinedu
  nginx -t && systemctl reload nginx
fi

args=()
for n in $(printf '%s\n' "${names[@]}" | sort -u); do args+=(-d "$n.$DOMAIN"); done
certbot certonly --webroot -w /var/www/certbot --cert-name "$CERT" --expand --non-interactive --agree-tos --keep-until-expiring "${args[@]}"

# Etapa 2: site completo (HTTPS). Renovação automática do Certbot recarrega o Nginx.
cp nginx-sinedu.conf "$SITE"
ln -sf "$SITE" /etc/nginx/sites-enabled/sinedu
mkdir -p /etc/letsencrypt/renewal-hooks/deploy
printf '#!/bin/sh\nsystemctl reload nginx\n' > /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
chmod +x /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
nginx -t && systemctl reload nginx
echo "Certificado $CERT com $(( ${#args[@]} / 2 )) nome(s):$(printf ' %s' "${args[@]}" | sed 's/ -d//g')"
