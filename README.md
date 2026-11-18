# Sinedu

Plataforma multi-tenant para escolas das redes estadual e municipal. Cada escola funciona no próprio subdomínio, com banco de dados próprio. O dono da plataforma controla escolas, contratos e faturas num painel próprio, e todos entram por um login único com verificação em duas etapas.

Documentação: [.doc/](.doc/README.md) · Guia para o Claude Code: [CLAUDE.md](CLAUDE.md)

## Pré-requisitos

- .NET SDK 10
- Node.js 24
- Docker Desktop

## Rodando localmente

```bash
# 1. Infraestrutura (PostgreSQL, PgBouncer, Mailpit, SeaweedFS S3, Vault)
docker compose -f infra/docker-compose.yml up -d

# 2. Banco da plataforma + dados de desenvolvimento (super admin e 2 escolas fictícias)
dotnet run --project apps/api/src/Sinedu.Platform.Host -- migrate seed-dev

# 3. APIs (um terminal para cada)
dotnet run --project apps/api/src/Sinedu.Platform.Host   # conta + painel → :5100
dotnet run --project apps/api/src/Sinedu.Tenant.Host     # escolas → :5200

# 4. Fronts (um terminal para cada)
npm install
npm run dev:conta    # http://conta.localhost:5175  (login único)
npm run dev:web      # http://escola-demo.localhost:5173
npm run dev:painel   # http://painel.localhost:5174
```

Subdomínios `*.localhost` resolvem para `127.0.0.1` nos navegadores modernos, sem configurar DNS.

### Contas de desenvolvimento

Criadas pelo `seed-dev`. **Somente para uso local.**

| Conta | Onde entra |
|-------|-----------|
| `admin@sinedu.local` | Painel — http://painel.localhost:5174 |
| `diretora@escola-demo.local` | http://escola-demo.localhost:5173 |
| `diretor@escola-modelo.local` | http://escola-modelo.localhost:5173 |

- Senha de todas: `Sinedu-Dev-2026!`
- Verificação em duas etapas: cadastre o segredo `KRSXG5CTMVRXEZLUKNSWG4TFOQ2DAMZR` em qualquer app autenticador. O `seed-dev` também imprime o código do momento.
- Convites e redefinições de senha chegam no Mailpit: http://localhost:8025

| Serviço | Endereço local |
|---------|----------------|
| Mailpit (e-mails capturados) | http://localhost:8025 |
| Vault (dev) | http://localhost:8200 (token `dev-root-token`) |
| S3 (SeaweedFS) | http://localhost:8333 |

> As credenciais em `infra/` e em `appsettings.Development.json` são **somente de desenvolvimento**.

## Testes e qualidade

```bash
dotnet test apps/api/Sinedu.slnx   # unitários, arquitetura e integração (Testcontainers: precisa do Docker)
npm run lint                       # ESLint nos fronts
npm run typecheck                  # TypeScript estrito
npm test                           # Vitest nos fronts
npm run build                      # build de produção dos fronts
```

Os testes de integração sobem um PostgreSQL descartável, provisionam duas escolas de verdade e verificam isolamento, matriz de permissões, step-up e regras de conta.

## Estrutura

```
apps/api/        Solução .NET: Platform.Host, Tenant.Host, SharedKernel, módulos e Tenant.Migrations
apps/conta/      SPA do login único (entrar, MFA, convites, senha)
apps/web/        SPA da escola
apps/painel/     SPA do painel da plataforma
packages/ui/     Design system compartilhado (tokens, componentes, gráficos)
infra/           Docker Compose de desenvolvimento, políticas do Vault
.doc/            Especificação, decisões, ADRs, arquitetura e backlog
```

Os dashboards ainda usam **dados fictícios**, marcados na interface. As APIs reais entram conforme o [backlog](.doc/arquitetura/backlog.md).
