# Política do Sinedu.Tenant.Host
# Usa as chaves das escolas para envelope encryption; não cria nem destrói chaves.

path "transit/datakey/plaintext/tenant-*" {
  capabilities = ["update"]
}

path "transit/decrypt/tenant-*" {
  capabilities = ["update"]
}

path "transit/rewrap/tenant-*" {
  capabilities = ["update"]
}

path "secret/data/tenants/*" {
  capabilities = ["read"]
}

path "transit/keys/*" {
  capabilities = ["deny"]
}
