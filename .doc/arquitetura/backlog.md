# Backlog — Sinedu MVP

Ordenado por dependência. Cada item é um incremento executável (migration + API + front + testes + docs, quando aplicável). `Dep.` lista o que precisa estar pronto antes.

Legenda: 🔒 segurança bloqueante · 🧪 inclui teste de negação/isolamento obrigatório · ✅ concluído · 🟡 parcial

## Fase 0 — Esqueleto

| ID | Item | Dep. |
|----|------|------|
| F0-01 ✅ | Solução .NET: `Platform.Host`, `Tenant.Host`, `SharedKernel`, projetos de módulo, testes. Dois apps React/Vite: `web` (escola) e `painel` | — |
| F0-02 ✅ | `infra/docker-compose.yml`: PostgreSQL, PgBouncer, Mailpit, SeaweedFS (S3), Vault dev; bootstrap do Vault (engines, políticas `platform` e `tenant-app`) | — |
| F0-03 ✅ | CI: build, testes, lint, testes de arquitetura (NetArchTest) | F0-01 |
| F0-04 ✅ | Middlewares base: ProblemDetails, correlation id, Serilog sanitizado com `tenant_id`, cabeçalhos de segurança, `no-store`, rate limiting | F0-01 |
| F0-05 ✅ | Gerador de dados fictícios (escolas, turmas, alunos, responsáveis) — amostra anonimizada real + comando `import-sample` | F0-01 |
| F0-06 ✅ | Desenvolvimento local com subdomínios `*.localhost` (proxies do Vite e Kestrel) | F0-01 |

## Fase 1 — Fundação multi-tenant e segurança

| ID | Item | Dep. |
|----|------|------|
| F1-01 ✅ | Banco `sinedu_platform`: `tenants`, `networks`, `reserved_slugs` | F0-02 |
| F1-02 🔒 ✅ | Provisionamento de escola (job idempotente): banco + papéis de banco, migrations, chave Transit, bucket, seeds | F1-01 |
| F1-03 🔒🧪 ✅ | Tenant.Host: resolução de tenant pelo host, cache do registro, `TenantContext`, `DbContext` por escola, bloqueio de escola suspensa/encerrada ou com schema divergente | F1-02 |
| F1-04 🔒 ✅ | Contas da plataforma: Argon2id, bloqueio por tentativas, TOTP + códigos de recuperação, recuperação de senha | F1-01 |
| F1-05 🔒 ✅ | `conta.`: OpenIddict (Authorization Code + PKCE), validação de `redirect_uri` contra o registro de escolas, `user_tenant_memberships`, escolha de escola | F1-04 |
| F1-06 🔒🧪 ✅ | Login na escola: cliente OIDC, cookie `__Host-`, sessões revogáveis, checagem de `tenant_id` da sessão contra o host, logout | F1-03, F1-05 |
| F1-07 🔒 ✅ | Step-up (`auth_time` ≤ 15 min, `max_age=0`) | F1-06 |
| F1-08 🔒🧪 ✅ | Papéis da escola (`access.user_roles`), sincronização de vínculo com a plataforma, framework de policies, resposta 404 fora do escopo | F1-06 |
| F1-09 🔒 ✅ | Auditoria append-only na escola e na plataforma | F1-02 |
| F1-10 🔒 ✅ | `IEnvelopeCipher` (AES-256-GCM + AAD) e `IKeyProvider` (Vault Transit por escola; fake em testes unitários) | F0-02 |
| F1-11 🔒🧪 ✅ | Harness de testes: provisiona **duas escolas** em Testcontainers, fábrica de usuários por papel; `PermissionMatrixTests` e `TenantIsolationTests` prontos para receber endpoints | F1-08 |
| F1-12 🔒 ✅ | Convites: primeiro Diretor (pela plataforma) e convites internos da escola, com as salvaguardas da matriz §4 | F1-08 |
| F1-13 ✅ | Front escola: layout, rotas protegidas, troca de escola, tratamento de 401/step-up | F1-07 |

## Fase P — Painel gerencial (pode correr em paralelo com as fases 2–5)

| ID | Item | Dep. |
|----|------|------|
| P-01 ✅ | Login no `painel.` (contas de plataforma, MFA obrigatório), papéis SUPER_ADMIN/SUPORTE | F1-05 |
| P-02 ✅ | Escolas: listar, cadastrar (slug validado), acompanhar provisionamento, tentar de novo em caso de falha | P-01, F1-02 |
| P-03 ✅ | Suspender/reativar escola, com motivo e auditoria | P-02 |
| P-04 | Redes (cadastro e vínculo com escolas) | P-02 |
| P-05 🟡 | Migrations: versão de schema por escola, atualização em lote — comando `migrate-tenants` e endpoint prontos; falta a tela | P-02 |
| P-06 | Planos e pagantes | P-01 |
| P-07 | Contratos (escolas cobertas, vigência, empenho) | P-06, P-02 |
| P-08 | Faturas: lançamento, baixa manual, NF | P-07 |
| P-09 | Alertas de cobrança (job diário): fatura vencida, contrato vencendo, limite excedido | P-08 |
| P-10 🔒 | Métricas agregadas: view `metrics.v_counts` + papel `sinedu_metrics` + job noturno + tela | P-02 |
| P-11 🔒🧪 | Acesso de suporte: pedido no painel → aprovação do Diretor na escola → papel temporário somente leitura → expiração/revogação | P-01, F1-08 |
| P-12 🔒 | Contas de plataforma, desbloqueio e reset de MFA de usuários (auditado, com aviso por e-mail) | P-01 |
| P-13 | Encerramento de escola: exportação dos dados comuns, retenção, destruição de banco, bucket e chave | P-03 |
| P-14 🔒🧪 | `PlatformPanelTests`: nenhum dado de aluno alcançável pelo painel | P-10, P-11 |

## Fase 2 — Cadastros e navegação

| ID | Item | Dep. |
|----|------|------|
| F2-01 🧪 ✅ | Perfil da escola, anos letivos e bimestres | F1-08 |
| F2-02 🧪 ✅ | Séries e turmas | F2-01 |
| F2-03 🧪 ✅ | Profissionais e vínculos com turmas | F2-02 |
| F2-04 🧪 ✅ | Concessão de turmas ao perfil Consulta | F2-02, F1-12 |
| F2-05 🧪 ✅ | Alunos, matrículas (histórico), endereços versionados | F2-02 |
| F2-06 🧪 🟡 | Responsáveis N:N, autorização de contato — cadastro e exibição prontos; edição de responsáveis na tela fica para a importação (F3) | F2-05 |
| F2-07 🧪 ✅ | Escopo de aluno para Professor/Consulta | F2-03, F2-04, F2-05 |
| F2-08 🧪 ✅ | Busca de alunos paginada e restrita ao escopo | F2-07 |
| F2-09 ✅ | Front: períodos, turmas, lista de alunos, painel do aluno (sem abas sensíveis) | F2-01..F2-08 |
| F2-10 🟡 | Front: administração da escola (usuários, convites, vínculos, categorias, aprovação de suporte) — usuários e convites prontos | F1-12, F2-03, P-11 |
| F2-11 | Contrato OpenAPI + cliente TypeScript gerado | F2-05 |

## Fase 3 — Importação

**Pré-requisito externo:** amostras anonimizadas em `.doc/amostras/` (D7).

| ID | Item | Dep. |
|----|------|------|
| F3-01 ✅ | Upload seguro e leitura CSV/XLSX | F2-11 |
| F3-02 🟡 | Mapeamento assistido + presets por formato — sugestão automática por cabeçalho e conteúdo pronta; salvar mapeamentos personalizados ainda não | F3-01 |
| F3-03 ✅ | Validação por linha com erros mascarados | F3-02 |
| F3-04 ✅ | Consolidação: identificador institucional; sugestões por atributos; nunca mesclar só pelo nome; não sobrescrever com vazio | F3-03 |
| F3-05 ✅ | Prévia e resolução manual de duplicados/conflitos | F3-04 |
| F3-06 🧪 ✅ | Confirmação transacional por unidade (job), lote auditado, proveniência | F3-05 |
| F3-07 ✅ | Front: assistente de importação e histórico | F3-01..F3-06 |
| F3-08 ✅ | `ISchoolDataConnector` (interface, sem implementação real) | F3-06 |
| F3-09 ✅ | Critérios de aceite 1, 2 e 16 automatizados | F3-06 |

## Fase 4 — Módulos protegidos

| ID | Item | Dep. |
|----|------|------|
| F4-01 🔒🧪 ✅ | Saúde: registro versionado cifrado, step-up, auditoria | F1-07, F1-09, F1-10, F2-05 |
| F4-02 🔒🧪 ✅ | Proteção/Sigilo: vários registros, vigência, revogação, versões | F4-01 |
| F4-03 🔒🧪 ✅ | Anexos seguros (cifrados na API, download só via API) | F4-01 |
| F4-04 🔒 ✅ | Revisões pendentes / medidas vencidas | F4-02 |
| F4-05 🔒🧪 🟡 | Importação de Saúde/Proteção (fluxo dedicado) — Saúde pronta; Proteção fica no cadastro manual (registros dependem de documento e vigência) | F4-02, F3-03 |
| F4-06 🔒 ✅ | Exportação excepcional com justificativa, marca d'água e auditoria | F4-02 |
| F4-07 ✅ | Front: abas Saúde e Proteção carregadas sob demanda só para perfis autorizados; modal de step-up | F4-01..F4-03 |
| F4-08 🔒🧪 🟡 | `ProtectedDataLeakTests` completo (inclui métricas da plataforma, suporte e backups) — varre respostas, cabeçalhos, tabelas brutas da escola e da plataforma, S3, logs e e-mails; métricas (P-10) e backups entram quando existirem | F4-01..F4-06, P-10, P-11 |
| F4-09 🔒 ✅ | Rotação de chave da escola (`rewrap`) e runbook | F4-01 |

## Fase 5 — Ocorrências e comunicações

| ID | Item | Dep. |
|----|------|------|
| F5-01 🧪 ✅ | Ocorrência: rascunho → registrada → em acompanhamento → concluída/cancelada; turma da data do fato | F2-05 |
| F5-02 🧪 ✅ | Revisões com autor, data e justificativa | F5-01 |
| F5-03 🧪 ✅ | Anexos de ocorrência | F5-01 |
| F5-04 ✅ | Linha do tempo no painel do aluno | F5-01 |
| F5-05 🟡 | Ata em PDF com cabeçalho da escola — nome, código, endereço e contatos; **logo** fica para quando houver upload de logo da escola | F5-02 |
| F5-06 ✅ | `INotificationProvider` + SMTP; WhatsApp desabilitado | F0-02 |
| F5-07 🧪 ✅ | Comunicação: prévia, autorização, idempotência, fila, retry, reenvio | F5-01, F5-06, F2-06 |
| F5-08 ✅ | Modelos de e-mail neutros | F5-07 |
| F5-09 ✅ | Front: ocorrências completo | F5-01..F5-08 |
| F5-10 ✅ | Critérios de aceite 4, 5, 7 e 15 automatizados | F5-07 |

## Fase 6 — Indicadores, qualidade e implantação

| ID | Item | Dep. |
|----|------|------|
| F6-01 🧪 | Indicadores da seção 9 da spec (data do fato, turma histórica, denominador documentado) | F5-01 |
| F6-02 | Comparação entre turmas, taxa por 100 alunos, evolução semanal, bimestre anterior | F6-01 |
| F6-03 🧪 | Exportação CSV/PDF com supressão < 5 | F6-01 |
| F6-04 | Front: dashboard e relatórios | F6-01..F6-03 |
| F6-05 | E2E (Playwright): login único, troca de escola, fluxos principais, painel | F6-04, P-08 |
| F6-06 | Documentação: instalação local, variáveis, migrations, testes, deploy, backup/restauração por escola, provisionamento | — |
| F6-07 🔒 | Revisão de segurança final + relatório de testes + matriz validada (entregável de evidência) | todos |
| F6-08 | Homologação na VPS: Caddy com certificado wildcard, Vault, backups cifrados por escola | F6-06 |

## Caminho crítico

`F0-01 → F1-01 → F1-02 → F1-03 → F1-05 → F1-06 → F1-08 → F2-01 → F2-02 → F2-05 → F2-07 → (F3 / F4 / F5) → F6-01 → F6-07`

Em paralelo: F1-10 (criptografia) desde o início; Fase P a partir de F1-05.
