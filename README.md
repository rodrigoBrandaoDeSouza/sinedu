# Sinedu

Plataforma multi-tenant para escolas das redes estadual e municipal. Cada escola funciona no próprio subdomínio, com banco de dados próprio. O dono da plataforma controla escolas, contratos e faturas num painel próprio.

Documentação: [.doc/](.doc/README.md) · Guia para o Claude Code: [CLAUDE.md](CLAUDE.md)

## Pré-requisitos

- .NET SDK 10
- Node.js 24
- Docker Desktop

## Rodando localmente

```bash
# 1. Infraestrutura (PostgreSQL, PgBouncer, Mailpit, SeaweedFS S3, Vault)
docker compose -f infra/docker-compose.yml up -d

# 2. Dependências do front
npm install

# 3. APIs (um terminal para cada)
dotnet run --project apps/api/src/Sinedu.Tenant.Host     # escolas  → :5200
dotnet run --project apps/api/src/Sinedu.Platform.Host   # conta/painel → :5100

# 4. Fronts (um terminal para cada)
npm run dev:web      # http://escola-demo.localhost:5173
npm run dev:painel   # http://painel.localhost:5174
```

Subdomínios `*.localhost` resolvem para `127.0.0.1` nos navegadores modernos, sem configurar DNS. Qualquer subdomínio válido funciona como escola (ex.: `http://outra-escola.localhost:5173`); o registro real de escolas entra no item F1-03.

| Serviço | Endereço local |
|---------|----------------|
| App da escola | http://escola-demo.localhost:5173 |
| Painel da plataforma | http://painel.localhost:5174 |
| Mailpit (e-mails capturados) | http://localhost:8025 |
| Vault (dev) | http://localhost:8200 (token `dev-root-token`) |
| S3 (SeaweedFS) | http://localhost:8333 |

> As credenciais em `infra/` são **somente de desenvolvimento**.

## Testes e qualidade

```bash
dotnet test apps/api/Sinedu.slnx   # unitários, arquitetura e integração
npm run lint                       # ESLint nos fronts
npm run typecheck                  # TypeScript estrito
npm test                           # Vitest nos fronts
npm run build                      # build de produção dos fronts
```

## Estrutura

```
apps/api/        Solução .NET: Platform.Host, Tenant.Host, SharedKernel e módulos
apps/web/        SPA da escola (React + Vite)
apps/painel/     SPA do painel da plataforma
packages/ui/     Design system compartilhado (tokens, componentes, gráficos)
infra/           Docker Compose de desenvolvimento, políticas do Vault
.doc/            Especificação, decisões, ADRs, arquitetura e backlog
```

Os dashboards ainda usam **dados fictícios** (marcados na interface). As APIs reais entram conforme o [backlog](.doc/arquitetura/backlog.md).
