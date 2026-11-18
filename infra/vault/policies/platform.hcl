# Política do Sinedu.Platform.Host
# Cria, rotaciona e destrói chaves das escolas, mas NUNCA decifra dados de escola.

path "transit/keys/tenant-*" {
  capabilities = ["create", "read", "update", "delete"]
}

path "transit/keys/tenant-*/rotate" {
  capabilities = ["update"]
}

path "transit/keys/tenant-*/config" {
  capabilities = ["update"]
}

# Segredos da própria plataforma (ex.: TOTP).
path "transit/encrypt/platform-secrets" {
  capabilities = ["update"]
}

path "transit/decrypt/platform-secrets" {
  capabilities = ["update"]
}

# Credenciais de banco das escolas (escrita no provisionamento).
path "secret/data/tenants/*" {
  capabilities = ["create", "read", "update", "delete"]
}

path "secret/metadata/tenants/*" {
  capabilities = ["read", "delete"]
}

# Segredos da própria plataforma (ex.: senha do papel de métricas).
path "secret/data/platform/*" {
  capabilities = ["create", "read", "update"]
}

# Explícito: a plataforma não decifra nem gera chaves de dados de escola.
path "transit/decrypt/tenant-*" {
  capabilities = ["deny"]
}

path "transit/datakey/*" {
  capabilities = ["deny"]
}
