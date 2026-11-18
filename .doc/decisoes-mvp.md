# Decisões do MVP — Sinedu

Complementa a especificação [MVP_Sistema_Integrado_Escolar_Claude.md](MVP_Sistema_Integrado_Escolar_Claude.md). Em caso de conflito, **a especificação prevalece**, exceto nos pontos abaixo explicitamente decididos.

Atualizado em: 2026-11-18

## Decididas com o responsável do produto

### Produto e stack

| # | Tema | Decisão |
|---|------|---------|
| D1 | Android | **Fora do MVP.** MVP é só web responsiva. A API nasce preparada para um app nativo depois (OIDC + PKCE já atende). |
| D2 | Stack | React + TypeScript + Vite; ASP.NET Core (.NET 10 LTS); PostgreSQL. [ADR 0001](adr/0001-stack-mvp.md). |
| D3 | Hospedagem | VPS própria com Docker. HashiCorp Vault para chaves e segredos; SeaweedFS (S3) para anexos. |
| D4 | Público | Redes estadual e municipal. Dados entram por upload de relatórios no MVP. |
| D8 | E-mail | SMTP genérico; Mailpit em desenvolvimento. |
| D12 | Ata/PDF | Cabeçalho configurável por escola (logo, nome, endereço, contatos) e campos de assinatura. |

### Plataforma multi-tenant — [ADR 0002](adr/0002-multi-tenancy-subdominio.md)

| # | Tema | Decisão |
|---|------|---------|
| D5 | Tenant | **Cada escola é um tenant**, em `{slug}.sinedu.com.br`. **Redes** existem só no painel, para agrupar escolas e associar contratos. Sem visão de rede no MVP. |
| D13 | Isolamento | **Um banco PostgreSQL por escola**, com papel de banco e chave de criptografia (Vault) próprios. |
| D14 | Painel gerencial | `painel.sinedu.com.br`, do dono da plataforma: cadastrar, provisionar, suspender, reativar e encerrar escolas; gerenciar redes, planos, contratos e faturas; ver métricas **agregadas** (sem dados pessoais de alunos). |
| D15 | Login | **Login único** em `conta.sinedu.com.br` (OIDC). A pessoa vê as escolas onde tem vínculo e troca entre elas sem novo login. Papéis e dados ficam no banco de cada escola. |
| D16 | Cobrança | **Controle manual**: planos, contratos (pagante = rede, escola ou outra entidade; nº de empenho/processo), faturas com vencimento, nº da nota fiscal e baixa manual. Sem gateway. Inadimplência ou contrato vencido **gera alerta**; a suspensão é decisão manual no painel. |
| D17 | Suporte | O suporte da plataforma só entra numa escola com **liberação do Diretor**, por até **24 h**, **somente leitura de dados comuns** (sem ocorrências e nunca Saúde/Proteção). Tudo auditado e visível ao Diretor, que pode revogar a qualquer momento. |
| D18 | Escala | Até **50 escolas** no primeiro ano: um servidor PostgreSQL + PgBouncer. Reavaliar acima de ~300. |
| D19 | Contas da plataforma | Contas de plataforma (`SUPER_ADMIN`, `SUPORTE`) **não podem ter papéis em escolas**. Quem também trabalha numa escola usa outra conta. |

### Segurança e papéis nas escolas

| # | Tema | Decisão |
|---|------|---------|
| D6 | MFA | TOTP obrigatório para SUPER_ADMIN, SUPORTE, ADMIN_TECNICO, DIRETOR e PEDAGOGO; opcional para PROFESSOR e CONSULTA. **Step-up** (reautenticação) ao abrir Saúde ou Proteção/Sigilo se a última autenticação forte tiver mais de 15 min. |
| D9 | Gestão de usuários | O painel provisiona a escola e convida **somente o primeiro Diretor**. Depois disso, **só um Diretor concede os papéis DIRETOR e PEDAGOGO** (quem lê dados sensíveis). Diretor e ADMIN_TECNICO da escola convidam PROFESSOR e CONSULTA. Toda concessão de DIRETOR/PEDAGOGO notifica por e-mail todos os diretores da escola. |
| D10 | Perfil CONSULTA | Concedido por turma(s), somente leitura de dados cadastrais comuns. |
| D11 | Supressão estatística | Contagens **< 5** aparecem como `<5` em exportações e relatórios agregados. |
| D20 | ADMIN_TECNICO | Passa a ser **papel da escola** (TI da escola ou da secretaria), com as restrições da spec. Não é mais papel global. |
| D7 | Amostras de importação | Planilhas reais **anonimizadas** em `.doc/amostras/`. Nunca versionar dados reais. |

## Padrões adotados sem consulta (não alteram escopo)

- Datas persistidas em UTC; interface em pt-BR e fuso `America/Sao_Paulo`.
- Senhas com Argon2id. Sessão da escola em cookie `__Host-` (`HttpOnly`, `Secure`, `SameSite=Lax` por causa do retorno do OIDC) + antiforgery.
- Criptografia por envelope: AES-256-GCM por registro, DEK cifrada pela chave da escola no Vault Transit.
- Bibliotecas livres: EF Core + Npgsql, OpenIddict, CsvHelper, ClosedXML, xUnit + Testcontainers; React Router e TanStack Query.
- Jobs assíncronos em fila no PostgreSQL (um banco de jobs por escola + fila da plataforma para provisionamento e migrations).
- Subdomínios reservados: `painel`, `conta`, `api`, `www`, `admin`, `app`, `mail`, `status`, `suporte`.
- Domínio `sinedu.com.br` é provisório e configurável.
- **Object storage: SeaweedFS** (S3-compatível, Apache 2.0) no lugar do MinIO, que deixou de publicar imagens Docker. O código usa só a API S3, então o provedor é trocável.

## Propostas para validação

Interpretações feitas na arquitetura. Valem como padrão até serem confirmadas ou alteradas.

| # | Proposta |
|---|----------|
| P1 | Aluno transferido para outra escola vira **novo registro no banco da escola de destino** (consequência de D13). O histórico da escola anterior não é visível. |
| P2 | **Professor** vê identificação, matrícula, turma, equipe e **responsáveis com contatos**; **não vê endereço**. Só alunos com matrícula ativa no ano vigente em turmas dele. **Consulta** não vê responsáveis nem endereço. |
| P3 | Em Proteção/Sigilo ficam em claro só tipo, situação (vigente/revogada) e data de validade, para listar vencimentos. Todo o resto é cifrado. |
| P4 | Ocorrência registrada não é excluída; pode ser **cancelada** com justificativa (fica no histórico, sai dos indicadores). |
| P5 | Dados brutos das linhas de importação são apagados 90 dias após a conclusão do lote. |
| P6 | **ADMIN_TECNICO da escola não acessa lista nem painel de alunos.** Vê alunos só nas telas de importação. |
| P7 | Pedagogo também pode manter vínculos professor↔turma. |
| P8 | Importação comum é feita por ADMIN_TECNICO e Diretor; Pedagogo só usa o fluxo dedicado de Saúde/Proteção. |
| P9 | Auditoria de acessos a registros protegidos é visível apenas ao Diretor. |
| P11 | **Encerramento de escola:** exporta os dados comuns para a escola, mantém o banco bloqueado por 90 dias e então destrói banco, anexos e chave no Vault (crypto-shredding). Prazos finais dependem de LGPD/contrato. |
| P12 | **Escola suspensa:** ninguém faz login (mensagem de suspensão); dados preservados; jobs pausados. |
| P13 | O painel mostra, por escola: nº de alunos ativos, turmas, usuários ativos, armazenamento, e-mails enviados no mês, falhas de jobs e versão do banco. Nenhum nome ou dado de aluno. |

## Pendências (não bloqueiam o início)

- [ ] Domínio definitivo e acesso ao DNS (certificado wildcard via desafio DNS).
- [ ] Gerador de PDF: avaliar licença do QuestPDF para uso por órgão público.
- [ ] Prazos de retenção e descarte — definir com as redes e assessoria LGPD.
- [ ] Amostras anonimizadas de importação (D7), necessárias antes da Fase 3.
- [ ] Modelo de texto padrão das comunicações aos responsáveis.

### Pendências técnicas da Fase 1 (antes da homologação — F6-08)

- [ ] **Chaves do OIDC:** hoje são efêmeras (reiniciar a plataforma invalida tokens em trânsito). Produção precisa de certificados de assinatura/criptografia persistentes.
- [ ] **PgBouncer para bancos de escola:** em dev a aplicação conecta direto no PostgreSQL. Em produção, configurar `auth_query` no PgBouncer para os papéis `t_*_app`.
- [ ] **Papel de leitura do registro:** o Tenant.Host lê `tenants.v_registry` com um papel próprio, só com `SELECT` nessa view (em dev usa o usuário do Postgres).
- [ ] **Vault em produção:** autenticação AppRole por host, com as políticas `sinedu-platform` e `sinedu-tenant-app` de `infra/vault/policies`.
- [ ] **Proxy reverso:** `UseForwardedHeaders` + Caddy com certificado wildcard.
