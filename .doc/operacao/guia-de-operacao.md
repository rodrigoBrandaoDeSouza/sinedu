# Guia de operação do Sinedu

Para quem instala, atualiza e mantém a plataforma. A visão técnica está em [arquitetura/visao-tecnica.md](../arquitetura/visao-tecnica.md); as decisões, em [decisoes-mvp.md](../decisoes-mvp.md).

## 1. Ambiente local

Pré-requisitos: Docker, .NET SDK 10, Node 22.

```bash
docker compose -f infra/docker-compose.yml up -d                              # postgres, pgbouncer, mailpit, seaweedfs, vault (dev)
dotnet run --project apps/api/src/Sinedu.Platform.Host -- migrate seed-dev    # banco da plataforma + contas e escolas fictícias
dotnet run --project apps/api/src/Sinedu.Platform.Host                        # :5100  conta. e painel.
dotnet run --project apps/api/src/Sinedu.Tenant.Host                          # :5200  {escola}.
npm install
npm run dev:conta & npm run dev:web & npm run dev:painel                      # :5175 :5173 :5174
```

Contas de desenvolvimento: [README](../../README.md) e [acessos-de-teste.pdf](../acessos-de-teste.pdf). Os e-mails chegam no Mailpit (http://localhost:8025).

Amostra anonimizada (nunca dados reais): `dotnet run --project apps/api/src/Sinedu.Tenant.Host -- import-sample escola-teste <caminho>/.doc/amostras/alunos-2026-anonimizado.xlsx`.

## 2. Testes

| Comando | O que roda |
|---------|-----------|
| `dotnet test apps/api/Sinedu.slnx` | Unitários, arquitetura e integração (Testcontainers sobe um PostgreSQL real; precisa do Docker). Provisiona 3 escolas e testa matriz de permissões, isolamento e vazamento. |
| `npm run lint && npm run typecheck && npm test` | Fronts (ESLint, TypeScript estrito, Vitest). |
| `npm run e2e` | Playwright contra o ambiente local **inteiro rodando** (seção 1), com a amostra importada na `escola-demo`. |

Os testes `ProtectedDataLeakTests` e os de negação em `PermissionMatrixTests`, `ProtectedRecordsTests`, `OccurrenceTests`, `CommunicationTests` e `AnalyticsTests` **bloqueiam a release** (spec §10.3).

## 3. Variáveis de configuração

Em produção tudo vem de variáveis de ambiente (modelo: [`infra/prod/env.example`](../../infra/prod/env.example)). Nenhum segredo vai para o Git.

| Variável (formato .NET) | Host | Para quê |
|---|---|---|
| `Sinedu__BaseDomain`, `Sinedu__AccountUrl`, `Sinedu__PanelUrl`, `Sinedu__TenantUrlTemplate` | ambos | Endereços públicos (`https://{slug}.dominio`). |
| `Sinedu__SecureCookies=true` | ambos | Cookies `Secure`/`__Host-` e exigência de HTTPS no OIDC. Ligado, os certificados OIDC passam a ser obrigatórios. |
| `Sinedu__TrustedProxyNetwork` | ambos | Rede do Caddy (CIDR). Só dela os cabeçalhos `X-Forwarded-*` são aceitos. |
| `ConnectionStrings__Platform` | plataforma | Banco `sinedu_platform` (dono). |
| `TenantDatabases__AdminConnectionString`, `__Host`, `__Port` | plataforma | Criação de bancos de escola e DDL (provisionamento e migrations). |
| `ConnectionStrings__PlatformRegistry` | escola | Papel `sinedu_registry_reader`: só `SELECT` em `tenants.v_registry`. |
| `Vault__Address`, `Vault__Token` | ambos | Um token por host, com as políticas de `infra/vault/policies` (a da plataforma **não** decifra). |
| `Storage__ServiceUrl`, `__AccessKey`, `__SecretKey` | ambos | SeaweedFS (S3). Um bucket por escola. |
| `Smtp__Host`, `__Port`, `__UseTls`, `__Username`, `__Password`, `__FromAddress` | ambos | SMTP **padrão** da plataforma. Cada escola pode ter o próprio SMTP, configurado no painel (Escolas → E-mail), guardado no Vault; teste em Sistema → Teste de e-mail. |
| `Oidc__TenantClientSecret`, `Oidc__PanelClientSecret` | ambos | Clientes confidenciais da escola e do painel. |
| `Oidc__SigningCertificatePath`, `Oidc__EncryptionCertificatePath`, `Oidc__CertificatePassword` | plataforma | Certificados PFX persistentes do login único. |
| `Notifications__RetryWorker` | escola | `false` desliga a fila de reenvio de e-mails (padrão: ligada). |
| `RateLimiting__PermitPerMinute` | ambos | Requisições por IP por minuto (padrão 300). |

## 4. Implantação (homologação/produção na VPS)

Arquivos em [`infra/prod/`](../../infra/prod): `docker-compose.prod.yml`, `Caddyfile` (certificado wildcard por desafio DNS), `Dockerfile.caddy` (Caddy + os três fronts), `apps/api/Dockerfile` (APIs), `init-roles.sql`, `backup.sh`, `restore-escola.sh`.

1. **DNS:** registros `A` para `conta.`, `painel.` e `*.` apontando para a VPS. Token de API do provedor de DNS para o desafio do wildcard.
2. **Imagens** (na raiz do repositório):
   ```bash
   docker build -f apps/api/Dockerfile --build-arg HOST=Sinedu.Platform.Host -t sinedu/platform:$VERSAO .
   docker build -f apps/api/Dockerfile --build-arg HOST=Sinedu.Tenant.Host   -t sinedu/tenant:$VERSAO .
   docker build -f infra/prod/Dockerfile.caddy --build-arg DNS_PROVIDER=cloudflare -t sinedu/caddy:$VERSAO .
   ```
3. **Segredos:** `cp infra/prod/env.example infra/prod/.env` e preencha (senhas com `openssl rand -base64 32`). Gere os certificados OIDC em `infra/prod/secrets/` (ex.: `openssl req -x509 -newkey rsa:3072 -days 730 ...` e `openssl pkcs12 -export`) e o `infra/prod/s3.json` com a credencial do S3. Esses três ficam fora do Git.
4. **Vault:** suba só o `vault`, rode `vault operator init` (guarde as chaves de unseal **fora da VPS**, com duas pessoas diferentes), `unseal`, habilite `transit` e `kv-v2`, carregue as políticas e emita os tokens/AppRoles da plataforma e da escola. O `infra/vault/bootstrap.sh` mostra os comandos usados em dev.
5. **Banco:** suba o `postgres`, rode `init-roles.sql` e depois `docker compose run --rm platform migrate` (sem `seed-dev`).
6. **Subir tudo:** `docker compose -f infra/prod/docker-compose.prod.yml --env-file infra/prod/.env up -d`.
7. **Primeiro administrador:** `docker compose run --rm platform create-super-admin admin@dominio "Nome Completo"`. A pessoa recebe o convite por e-mail, cria a senha e cadastra o MFA. O comando só funciona enquanto não existe nenhum SUPER_ADMIN.
8. **Primeira escola:** pelo painel. O provisionamento cria banco, papéis, chave no Vault e bucket, e convida o primeiro Diretor.

### 4.1 Homologação atual (`sinedu.bsuite.tech`)

A homologação roda numa VPS que já tem outros sites, atrás do **Nginx do servidor** (não do Caddy). Os arquivos estão em [`infra/hml/`](../../infra/hml) e ficam em `/opt/sinedu/hml` na VPS. Os dados são sempre fictícios ou anonimizados.

| Script | Onde roda | O que faz |
|---|---|---|
| `deploy.sh [--sem-api]` | este computador (Git Bash) | Gera as imagens e os fronts, envia por SSH (`bem-vps`), aplica as migrations e atualiza as escolas |
| `bootstrap.sh <email> "<Nome>"` | VPS, uma vez | Gera os segredos **na VPS**, inicializa o Vault, migra o banco e cria o primeiro super admin |
| `add-host-cert.sh <slug...>` | VPS | Inclui o subdomínio de cada escola nova no certificado Let's Encrypt (o DNS da Hostinger não emite certificado curinga) |
| `backup.sh` / `restore-escola.sh` | VPS | Backup cifrado e restauração por escola (seção 6) |
| `pull-backups.sh` | este computador | Traz para cá os backups ainda não copiados e confere os hashes |

Endereços: `conta.`, `painel.`, `mail.` (Mailpit, com senha em `secrets/mailpit-password.txt`) e `{escola}.sinedu.bsuite.tech`. Todos os contêineres escutam só em `127.0.0.1`. Antes de recarregar o Nginx, rode sempre `nginx -t`, porque os outros sites dependem dele.

## 5. Atualização de versão

1. Backup (seção 6).
2. Novas imagens e `docker compose up -d`.
3. `docker compose run --rm platform migrate` (banco da plataforma) e `docker compose run --rm platform migrate-tenants` (bancos das escolas). Escola com schema diferente de `TenantSchema.Version` fica **em manutenção** (503) até ser atualizada. O painel mostra a versão de cada escola.
4. Migrations nunca são editadas depois de aplicadas; uma escola nova recebe todas desde a primeira.

## 6. Backup e restauração por escola

- `infra/prod/backup.sh` (cron diário): um arquivo por escola (`{slug}.dump.age` e `{slug}.bucket.tar.age`) e um da plataforma, cifrados com a **chave pública** age. A chave privada não fica na VPS. Copie a pasta para fora do servidor.
- `infra/prod/restore-escola.sh <pasta> <slug> <chave-privada>`: confere o `SHA256SUMS`, restaura só aquela escola (banco e anexos) e não toca nas outras.
- Saúde e Proteção continuam cifrados dentro do backup (envelope com chave no Vault). Sem a chave Transit da escola, não há como lê-los. É isso que torna o encerramento com crypto-shredding definitivo (P11).
- **Teste de restauração:** uma vez por trimestre, restaure uma escola de homologação e abra uma ficha de Saúde e um anexo.

**Na homologação** ([`infra/hml/backup.sh`](../../infra/hml/backup.sh)):
- Roda pelo cron todo dia às 03:30 de Brasília (`/etc/cron.d/sinedu-hml-backup`). Grava em `/var/backups/sinedu-hml/<data>/` e guarda 14 dias. O log fica em `/var/log/sinedu-hml-backup.log`.
- Além dos arquivos por escola, gera o `infra.tar.age`, com os dados do Vault (chaves Transit das escolas), o `.env` e a pasta `secrets/`. Sem ele, um servidor perdido não volta: os bancos restaurados ficariam com Saúde e Proteção ilegíveis.
- A chave pública fica em `secrets/backup-recipient.txt`. A **chave privada não fica na VPS**: está em `~/.sinedu/hml-backup.agekey` no computador do responsável. Guarde uma cópia num cofre de senhas, porque sem ela nenhum backup pode ser aberto.
- **Cópia para fora da VPS:** rode `infra/hml/pull-backups.sh` regularmente. Ele baixa para `~/.sinedu/backups-hml` só o que é novo.
- **Restaurar uma escola:** copie a chave privada para a VPS só durante a operação e rode `./restore-escola.sh /var/backups/sinedu-hml/<data> <slug> <chave>`. Depois apague a chave da VPS.
- **Servidor perdido:**
  1. Decifre o `infra.tar.age` e recoloque o `.env` e a pasta `secrets/` em `/opt/sinedu/hml`.
  2. Recoloque o `vault-data.tar` no volume `sinedu-hml_vault-data`.
  3. Rode `deploy.sh`.
  4. Restaure o `platform.dump.age` com `pg_restore` e depois cada escola.

## 7. Operações de escola

| Situação | Como |
|---|---|
| Nova escola | Painel → Escolas → Nova. Acompanhe os passos do provisionamento. |
| Suspender / reativar | Painel. Suspensa: ninguém entra, dados preservados, fila pausada (P12). |
| Rotação de chave | [Runbook](../arquitetura/runbook-rotacao-de-chave.md): `vault write -f transit/keys/tenant-<id>/rotate` e depois `rewrap-protected <slug>`. |
| E-mail que falhou | Escola → Comunicações → Reenviar. A fila tenta sozinha 3 vezes (1 e 5 minutos). |
| Encerrar escola | Painel → Escolas → **Encerrar** (motivo obrigatório). A escola fecha para todos. A Direção entra e baixa a **exportação de encerramento** (ZIP com CSVs de turmas, alunos, matrículas, responsáveis, endereços, equipe e ocorrências; **sem** Saúde e Proteção). **Desfazer** volta a escola para suspensa. Depois de 90 dias (`Tenants:ClosingRetentionDays`), **Destruir dados**, digitando o endereço da escola, apaga banco, papéis, bucket e segredos e destrói a chave Transit. Os backups antigos continuam com Saúde e Proteção cifrados por uma chave que não existe mais. Irreversível. |
