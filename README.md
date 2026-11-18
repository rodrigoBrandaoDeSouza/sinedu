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

# 2. Banco da plataforma + dados de desenvolvimento (super admin e 3 escolas fictícias)
dotnet run --project apps/api/src/Sinedu.Platform.Host -- migrate seed-dev

# 2b. Carrega a amostra anonimizada (1.031 alunos, 34 turmas) na escola de teste
dotnet run --project apps/api/src/Sinedu.Tenant.Host -- import-sample escola-teste "$PWD/.doc/amostras/alunos-2026-anonimizado.xlsx"

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
| `suporte@sinedu.local` | Painel — **sem MFA**: o 1º login abre o cadastro do autenticador com QR code (o `seed-dev` zera o MFA dela a cada execução) |
| `diretora@escola-demo.local` | http://escola-demo.localhost:5173 |
| `diretor@escola-modelo.local` | http://escola-modelo.localhost:5173 |
| `diretora@escola-teste.local` | http://escola-teste.localhost:5173 — com a amostra anonimizada |

- Senha de todas: `Sinedu-Dev-2026!`
- Verificação em duas etapas: cadastre o segredo `KRSXG5CTMVRXEZLUKNSWG4TFOQ2DAMZR` em qualquer app autenticador. O `seed-dev` também imprime o código do momento.
- Convites e redefinições de senha chegam no Mailpit: http://localhost:8025

| Serviço | Endereço local |
|---------|----------------|
| Mailpit (e-mails capturados) | http://localhost:8025 |
| Vault (dev) | http://localhost:8200 (token `dev-root-token`) |
| S3 (SeaweedFS) | http://localhost:8333 |

> As credenciais em `infra/` e em `appsettings.Development.json` são **somente de desenvolvimento**.

## Operação e segurança

- [Guia de operação](.doc/operacao/guia-de-operacao.md): variáveis, implantação na VPS, atualização, backup e restauração por escola.
- [Revisão de segurança do MVP](.doc/relatorios/revisao-seguranca-mvp.md): regras bloqueantes e os testes que as garantem.

## Testes e qualidade

```bash
dotnet test apps/api/Sinedu.slnx   # unitários, arquitetura e integração (Testcontainers: precisa do Docker)
npm run lint                       # ESLint nos fronts
npm run typecheck                  # TypeScript estrito
npm test                           # Vitest nos fronts
npm run build                      # build de produção dos fronts
npm run e2e                        # Playwright contra o ambiente local rodando
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

Turmas, alunos e painel do aluno usam dados reais do banco da escola. Os dashboards ainda usam **dados fictícios**, marcados na interface. As APIs reais entram conforme o [backlog](.doc/arquitetura/backlog.md).
