# CLAUDE.md

Guia para o Claude Code trabalhar neste repositório. Leia antes de qualquer tarefa.

## O que é o Sinedu

Plataforma **multi-tenant** para escolas das redes **estadual e municipal**. Cada escola funciona no próprio subdomínio (`{escola}.sinedu.com.br`), com banco de dados próprio. A aplicação da escola consolida cadastro de alunos, turmas e equipes (importação CSV/XLSX), registra ocorrências pedagógicas, comunica responsáveis por e-mail e gera indicadores bimestrais. O dono da plataforma controla escolas, contratos e faturas num painel próprio.

| Endereço | O que é |
|----------|---------|
| `conta.sinedu.com.br` | Login único (OIDC) + MFA |
| `painel.sinedu.com.br` | Painel gerencial da plataforma |
| `{escola}.sinedu.com.br` | Aplicação da escola |

**Documentos obrigatórios antes de implementar:**
1. [.doc/MVP_Sistema_Integrado_Escolar_Claude.md](.doc/MVP_Sistema_Integrado_Escolar_Claude.md) — especificação funcional (fonte da verdade).
2. [.doc/decisoes-mvp.md](.doc/decisoes-mvp.md) — decisões e propostas sobre pontos em aberto.
3. [.doc/arquitetura/](.doc/arquitetura/) — visão técnica, modelo de dados, matriz de permissões, backlog.

Não alterar regras funcionais ou de sigilo sem aprovação explícita. Em ambiguidade: privacidade, menor privilégio e histórico confiável.

## ⛔ Regras que bloqueiam release

**Sigilo.** Dados de **Saúde** e **Proteção/Sigilo** são criptografados e acessíveis **somente por DIRETOR ou PEDAGOGO da escola do aluno**. `PROFESSOR`, `CONSULTA`, `ADMIN_TECNICO`, `SUPORTE_PLATAFORMA` e as contas da plataforma (`SUPER_ADMIN`, `SUPORTE`) **nunca** os obtêm: nem por API, exportação, log, busca, contagem, métrica, anexo, cache ou metadado.

**Isolamento entre escolas.** Cada requisição da escola resolve o tenant pelo `Host` e usa **somente** a conexão, a chave do Vault e o bucket daquela escola. Uma sessão de uma escola nunca vale em outra.

**Painel.** O `Platform.Host` não lê dados de alunos. Só acessa bancos de escola para DDL (migrations) e para a view `metrics.v_counts`. Não tem permissão de `decrypt` no Vault.

Na prática:
- Autorização no back-end em **toda** requisição: papel na escola + vínculo (aluno/turma). `deny by default`. Esconder botão na UI não é controle.
- Recurso fora do escopo → **404** (não revela existência).
- DTOs comuns de aluno **nunca** contêm propriedades sensíveis (nem `null`). Módulos protegidos têm DTOs, rotas e políticas próprias.
- Nunca logar, auditar, enviar a telemetria ou incluir em e-mail/ata/indicador conteúdo sensível.
- Todo endpoint novo precisa de **teste de negação** (cada papel, outra escola, sessão de outra escola) na `PermissionMatrixTests`.

## Escopo do MVP

- Web responsiva + painel da plataforma. **Android fica para depois** (a API já nasce com OIDC + PKCE para recebê-lo).
- Fora do escopo: notas/frequência, portal de pais, WhatsApp ativo, integração automática com SERE, OCR, IA, rankings, gateway de pagamento, visão de rede.

## Stack

| Camada | Tecnologia |
|--------|-----------|
| Front-end | React + TypeScript + Vite (dois apps: escola e painel), React Router, TanStack Query |
| Back-end | ASP.NET Core (.NET 10 LTS), monólito modular em dois hosts, EF Core + Npgsql |
| Identidade | OpenIddict (OIDC) em `conta.`, Argon2id, TOTP |
| Banco | PostgreSQL (`sinedu_platform` + um `sinedu_t_{slug}` por escola) via PgBouncer |
| Chaves e segredos | HashiCorp Vault (Transit, uma chave por escola) |
| Anexos | SeaweedFS (S3, um bucket por escola) |
| E-mail | SMTP genérico (Mailpit em dev) |
| Proxy | Caddy com certificado wildcard |
| Testes | xUnit + Testcontainers, Vitest + Testing Library, Playwright |

Decisões: [ADR 0001](.doc/adr/0001-stack-mvp.md) (stack) e [ADR 0002](.doc/adr/0002-multi-tenancy-subdominio.md) (multi-tenancy).

## Estrutura do repositório

```
sinedu/
├── apps/
│   ├── api/
│   │   ├── src/
│   │   │   ├── Sinedu.SharedKernel/         # tenancy, autorização, auditoria, criptografia, jobs, storage
│   │   │   ├── Sinedu.Platform.Host/        # conta. + painel. (API)
│   │   │   ├── Sinedu.Platform.<Modulo>/    # Identity, Tenants, Billing, Metrics, Support
│   │   │   ├── Sinedu.Tenant.Host/          # {escola}. (API)
│   │   │   ├── Sinedu.Tenant.<Modulo>/      # Access, School, Students, ProtectedRecords,
│   │   │   │                                # Occurrences, Notifications, Imports, Analytics
│   │   │   └── Sinedu.Tenant.Migrations/    # aplica o schema da escola + GRANTs (usado pela plataforma)
│   │   └── tests/
│   │       ├── Sinedu.UnitTests/
│   │       ├── Sinedu.ArchitectureTests/
│   │       └── Sinedu.IntegrationTests/     # Testcontainers: 2 escolas reais, matriz, isolamento
│   ├── conta/                # SPA do login único
│   ├── web/                  # SPA da escola
│   ├── painel/               # SPA do painel
│   └── android/              # (pós-MVP)
├── packages/
│   └── ui/                   # design system compartilhado: tokens, componentes, gráficos
├── infra/                    # docker-compose, políticas e bootstrap do Vault
├── .doc/
└── .claude/
```

Módulos não acessam tabelas de outro módulo; usam contratos públicos. **Nenhum módulo depende de `ProtectedRecords`.** Módulos `Platform.*` não referenciam módulos `Tenant.*` (exceto contratos de migrations e métricas).

## Comandos

```bash
# Infra local (postgres, pgbouncer, mailpit, seaweedfs s3, vault dev)
docker compose -f infra/docker-compose.yml up -d

# API (warnings são erros de build)
dotnet build apps/api/Sinedu.slnx
dotnet test apps/api/Sinedu.slnx
dotnet run --project apps/api/src/Sinedu.Platform.Host -- migrate seed-dev   # banco + contas e escolas fictícias
dotnet run --project apps/api/src/Sinedu.Platform.Host -- migrate-tenants    # atualiza o schema das escolas desatualizadas
dotnet run --project apps/api/src/Sinedu.Platform.Host -- create-super-admin <e-mail> "<Nome>"   # instalação nova (uso único)
dotnet run --project apps/api/src/Sinedu.Tenant.Host -- import-sample escola-teste <caminho absoluto do .xlsx em .doc/amostras> [aba]   # usa o mesmo pipeline da importação
dotnet run --project apps/api/src/Sinedu.Tenant.Host -- rewrap-protected escola-teste   # após rotacionar a chave da escola no Vault (runbook em .doc/arquitetura)
dotnet run --project apps/api/src/Sinedu.Platform.Host   # :5100 → conta.localhost / painel.localhost
dotnet run --project apps/api/src/Sinedu.Tenant.Host     # :5200 → {escola}.localhost

# Migration nova (sempre com --context e --output-dir Migrations)
dotnet ef migrations add <Nome> --project apps/api/src/Sinedu.Tenant.<Modulo> \
  --startup-project apps/api/src/Sinedu.Tenant.Host --context <Modulo>DbContext --output-dir Migrations

# Fronts (npm workspaces, rodar na raiz)
npm install
npm run dev:conta      # http://conta.localhost:5175
npm run dev:web        # http://escola-demo.localhost:5173
npm run dev:painel     # http://painel.localhost:5174
npm run lint && npm run typecheck && npm test && npm run build
npm run e2e           # Playwright contra o ambiente local inteiro rodando (pasta e2e/)
```

Em desenvolvimento, `*.localhost` resolve para 127.0.0.1 sem configurar DNS. O Vite repassa `/api`, `/auth` e os callbacks do OIDC mantendo o `Host`, e a API identifica a escola pelo subdomínio. Contas de dev e o segredo TOTP estão no [README](README.md).

## Regras de implementação (backend)

- **Novo schema/tabela de escola:** migration no módulo, registrar o `DbContext` em `TenantSchemaMigrator` (migrate + GRANTs) e **incrementar `TenantSchema.Version`**. Escola com versão diferente fica em manutenção até o painel rodar a atualização.
- **DbContext de escola** sempre via `AddTenantDbContext<T>()`: a conexão vem da escola resolvida pelo host. Nunca montar connection string de escola à mão.
- **Endpoint novo da escola:** política explícita (`Policies.*`) e linha(s) em `PermissionMatrixTests` para todos os papéis + anônimo. A política padrão é negar quem não tem papel.
- **Endpoint da plataforma:** `MapPanelApi`/`MapAccountApi` (host errado → 404 antes da autenticação).
- **Ações sensíveis** gravam `IAuditLog` (sem conteúdo).
- **Rotas que mudam estado** com cookie exigem mesma origem (`SameOriginMiddleware`); chamadas serviço→serviço usam token Bearer.

## Front-end

- Componentes e tokens visuais vêm de `@sinedu/ui` (`packages/ui`). Não duplicar componentes nos apps; cores sempre por token (`bg-surface`, `text-foreground-secondary`...), nunca hex solto.
- Tema claro/escuro por `data-theme` no `<html>`; todo componente novo precisa funcionar nos dois.
- Gráficos seguem a skill `dataviz`: paleta validada (`--series-*`), um único eixo, legenda para 2+ séries, tooltip e **visão em tabela** (`ChartCard`). Estados sempre com ícone + texto.
- Enquanto a API correspondente não existe, dados ficam em `src/data/*.ts`, marcados como **fictícios** e com o item do backlog que os substitui.
- Nenhuma ferramenta de analytics, telemetria ou fonte externa (a fonte Inter é empacotada localmente).

## Convenções

- Código, identificadores e commits em **inglês**; documentação e textos de interface em **pt-BR**.
- Conventional Commits com escopo: `feat(tenant-api):`, `feat(platform-api):`, `fix(web):`, `feat(painel):`, `test:`, `docs:`.
- Branches `feat/...`, `fix/...`; nunca commitar direto na `main`.
- Rotas da escola **sem** `schoolId` (`/api/students/{id}`): a escola é o host.
- Migrations da escola num conjunto único aplicado a todos os bancos de escola; migrations da plataforma separadas. Nunca editar migration já aplicada.
- Datas em UTC no banco; exibição em `America/Sao_Paulo`.
- Ocorrências e registros protegidos são **versionados**: nunca apagar ou sobrescrever silenciosamente.
- Campos importados ausentes ficam vazios; **nunca** inferir dados nem mesclar alunos só pelo nome.
- Erros: `ProblemDetails` (RFC 9457), sem dados pessoais.
- Segredos nunca no repositório: Vault, `dotnet user-secrets` ou variáveis de ambiente.

## Dados de teste

**Nunca** usar dados reais de crianças em seeds, testes, prints ou demonstrações. Amostras em `.doc/amostras/` são sempre anonimizadas.

- Arquivos reais recebidos das escolas ficam em `.doc/files/` (no `.gitignore`). **Não abrir nem imprimir o conteúdo**: para entender a estrutura, imprima só cabeçalhos, contagens e formatos (nunca valores), e anonimize com `tools/anonimizacao/` — que confere no fim que nenhum valor real sobrou.
- Escopo de alunos (quem vê quais turmas, responsáveis e endereço) vem de `IViewerScopeProvider` (módulo School). Endpoints de aluno devem usá-lo e responder 404 fora do escopo. Testes de integração provisionam **pelo menos duas escolas**.

## Ordem de implementação

Operação, deploy e backup: [guia de operação](.doc/operacao/guia-de-operacao.md). Evidências de segurança: [relatório](.doc/relatorios/revisao-seguranca-mvp.md).

Ver [backlog](.doc/arquitetura/backlog.md): **0** esqueleto → **1** fundação multi-tenant e segurança → **2** cadastros → **3** importação → **4** módulos protegidos → **5** ocorrências e comunicações → **6** indicadores e implantação. A fase **P** (painel) corre em paralelo a partir do login único.

## Definição de pronto

- Testes passando, incluindo negação por papel e isolamento entre escolas.
- Migrations aplicáveis do zero num banco de escola novo.
- Contrato OpenAPI atualizado.
- `.doc/` atualizada se o comportamento documentado mudou.
