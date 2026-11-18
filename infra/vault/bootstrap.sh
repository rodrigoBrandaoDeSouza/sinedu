#!/bin/sh
# Configura o Vault de DESENVOLVIMENTO. Idempotente.
# Em produção, as mesmas políticas são aplicadas por um operador, nunca com token root.
set -eu

echo "[vault-init] habilitando engine transit"
vault secrets list -format=json | grep -q '"transit/"' || vault secrets enable transit

# Chave da plataforma (ex.: cifrar segredos TOTP). Chaves de escola (tenant-*) são
# criadas pelo provisionamento, nunca aqui.
vault read transit/keys/platform-secrets >/dev/null 2>&1 \
  || vault write -f transit/keys/platform-secrets type=aes256-gcm96

echo "[vault-init] aplicando políticas"
vault policy write sinedu-platform /vault-config/policies/platform.hcl
vault policy write sinedu-tenant-app /vault-config/policies/tenant-app.hcl

echo "[vault-init] concluído"
