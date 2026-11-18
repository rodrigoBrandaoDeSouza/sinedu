# Runbook — rotação da chave de uma escola

Cada escola tem uma chave no Vault Transit (`tenant-{id}`). Ela não cifra os dados direto: cifra as **DEKs** (uma por versão de registro, anexo e exportação). Rotacionar a chave e fazer o `rewrap` troca só as DEKs embrulhadas. O conteúdo e os anexos não são decifrados nem regravados.

## Quando rodar

- Rotina: uma vez por ano, por escola.
- Imediatamente: suspeita de vazamento de token do Vault, saída de pessoa com acesso administrativo ao Vault, ou orientação do encarregado de dados.

## Passos

1. **Descubra o nome da chave.** No banco da plataforma, `tenants.tenants.transit_key_name`, ou na tela da escola no painel.
2. **Rotacione no Vault.** É uma operação da plataforma, com o token/AppRole de `sinedu-platform`:
   ```bash
   vault write -f transit/keys/tenant-<id>/rotate
   vault read -field=latest_version transit/keys/tenant-<id>
   ```
   As DEKs antigas continuam abrindo, porque o Vault mantém as versões anteriores da chave. Nada para de funcionar.
3. **Recifre as DEKs.** É uma operação da escola, com a política `sinedu-tenant-app`, que só pode `rewrap`:
   ```bash
   dotnet run --project apps/api/src/Sinedu.Tenant.Host -- rewrap-protected <slug-da-escola>
   ```
   O comando informa quantas versões, anexos e exportações foram recifrados e grava `protected_keys_rewrapped` na auditoria da escola.
4. **Confira.** Todas as DEKs devem estar na versão nova:
   ```sql
   SELECT substring(wrapped_key from 1 for 9) AS versao, count(*) FROM protected.record_versions GROUP BY 1;
   ```
   Depois abra uma ficha de Saúde e baixe um anexo pela aplicação.
5. **Opcional (só depois do passo 4):** aposente as versões antigas com `min_decryption_version` no Vault. Depois disso, qualquer DEK que tenha escapado do rewrap deixa de abrir. Por isso o passo 4 vem antes.

## O que não fazer

- Não apague a chave (`DestroyKey`). Destruir a chave é o **encerramento** da escola (crypto-shredding, P11) e torna os dados irrecuperáveis.
- Não rode o `rewrap` com o token da plataforma. O Platform.Host não tem e não deve ter permissão de decifrar.

## Verificado em

- Testes: `ProtectedRecordsTests.Rotacao_de_chave_recifra_as_deks_sem_perder_o_conteudo`.
- Ambiente local (Vault real), em 18/11/2026: a `escola-demo` foi da v1 para a v2, 7 versões, 6 anexos e 1 exportação foram recifrados, e a leitura continuou funcionando.
