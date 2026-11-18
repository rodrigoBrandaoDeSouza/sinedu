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

## Decisões da importação (Fase 3)

- **O arquivo enviado não é guardado.** Ficam o hash, os cabeçalhos e as linhas da aba escolhida (as outras abas são descartadas ao validar). O conteúdo das linhas é apagado 90 dias depois (P5); totais, ações e mensagens permanecem.
- **Confirmação síncrona**, uma unidade por aluno: uma linha com erro não derruba o lote e fica registrada como falha. Planilhas de até 20 mil linhas cabem nesse modelo; se aparecerem arquivos maiores, a confirmação passa para a fila de jobs.
- **Turma inexistente é criada** no ano letivo ativo, com aviso na prévia ("turma nova"), incluindo o código de origem quando vier no rótulo.
- **Mudança de turma na planilha** encerra a matrícula atual (remanejada) e abre outra — o histórico fica.
- **Pedagogia não importa** (P8): só Direção e Administração técnica.

## Achados da planilha real (para a importação — Fase 3)

- **CGM repetido:** o arquivo real tem um mesmo CGM em duas linhas. A importação precisa detectar e pedir revisão, não criar dois alunos.
- **Dois formatos de turma:** `6º A Manhã` (cadastro) e `6º Série A Manhã - <código>` (frequência). O mapeamento precisa aceitar os dois e guardar o código da turma de origem.
- **Texto livre em colunas de checklist:** nomes e cidades digitados por engano em colunas de documentos. A importação deve validar contra listas fechadas e marcar o resto para revisão.
- **Sem autorização de contato:** o arquivo não traz autorização para contatar o responsável. Importamos com `contato não autorizado` (spec §6.1); a escola registra a autorização depois.
- **Observações livres podem conter dados sensíveis:** nunca importar texto livre para o cadastro comum.

## Decisões da Fase 4 (Saúde e Proteção/Sigilo)

- **Uma ficha de Saúde por aluno**, versionada. Proteção/Sigilo tem **vários registros** por aluno (guarda, tutela, medida protetiva, restrição judicial, autorização de retirada, proteção da criança/adolescente, outro).
- **Em claro só o mínimo:** data de revisão da ficha de Saúde; tipo, situação (vigente/revogada) e validade da Proteção (P3). Todo o resto — inclusive o **motivo de cada alteração**, nomes de arquivos e a justificativa de exportação — fica cifrado (envelope AES-256-GCM, AAD = escola | aluno | tipo | registro | versão).
- **Toda alteração exige motivo** e gera versão nova; edição concorrente recebe 409 em vez de sobrescrever. Registro revogado não é editado: cria-se outro.
- **"Vencida" é calculada** na leitura (vigente com validade passada). Nenhuma rotina altera registros sozinha.
- **Pendências:** revisões de Saúde e validades de Proteção em até **30 dias** ou já vencidas, sem conteúdo.
- **Anexos protegidos:** PDF, PNG ou JPG (conferidos pelo conteúdo, não pela extensão), até 10 MB, cifrados na API antes do S3, com SHA-256 do ciphertext conferido no download. "Retirar" esconde o anexo da lista e fica auditado; o objeto cifrado continua guardado.
- **Exportação excepcional:** PDF com marca d'água (nome de quem exportou, data/hora e número da exportação) em todas as páginas, justificativa com 20+ caracteres guardada cifrada e evento `protected_exported` na auditoria. Gerado com QuestPDF (licença Community — ver pendência abaixo).
- **Importação de Saúde:** fluxo próprio (Direção/Pedagogia + step-up). O arquivo **não é guardado**: a prévia lê, devolve só contagens e nomes das colunas preenchidas, e descarta; a confirmação reenvia o arquivo com o motivo. Aluno **só por CGM**; CGM repetido, ausente ou desconhecido não é importado. Campo vazio não apaga o que existe. A importação de Proteção fica fora do MVP (registros dependem de documento e vigência individuais).
- **Negativas** nos endpoints protegidos respondem 403 uniforme (papel sem acesso) ou 404 (aluno fora desta escola) e são auditadas como `protected_access_denied`, sem conteúdo.
- **Rotação de chave:** rotacionar a chave da escola no Vault e rodar `rewrap-protected {escola}` (ver [runbook](arquitetura/runbook-rotacao-de-chave.md)).

## Decisões da Fase 5 (ocorrências e comunicações)

- **Ciclo:** rascunho → registrada → em acompanhamento → concluída; cancelada a partir de qualquer estado formalizado, com justificativa (10+ caracteres). Concluída pode ser **reaberta** para acompanhamento. Rascunho é visível **só para quem o criou** e pode ser excluído por ele; registrada nunca é excluída (P4).
- **Revisões:** depois do registro, toda edição ou mudança de situação exige motivo e grava uma revisão com o estado completo (autor, data, motivo). Edição concorrente recebe 409. O aluno não muda depois do registro (cancela-se e registra-se outra).
- **Turma do fato:** a matrícula vigente na data local (São Paulo) do acontecimento. No dia de uma troca de turma, vale a turma nova. Fato sem matrícula na data é recusado.
- **Categorias** iniciais da spec + gravidade opcional (Leve, Moderada, Grave). Só a Direção cria, renomeia ou desativa categorias.
- **Formulário avisa** que violência, abuso e proteção vão para a aba Proteção/Sigilo — não há detecção automática de conteúdo.
- **Anexos de ocorrência:** PDF, PNG ou JPG até 10 MB no bucket da escola, com SHA-256 conferido no download; download só pela API.
- **Ata:** cabeçalho da escola (nome, código, endereço, telefone, e-mail), dados do fato, trilha de autoria com todas as versões e campos de assinatura (registro, Direção, responsável legal). Só para ocorrência formalizada.
- **Autorização de contato** do responsável exige **fundamento** escrito e guarda quem autorizou e quando. Sem autorização ou sem e-mail, o responsável aparece na prévia como "não pode receber", com o motivo.
- **Mensagens:** dois modelos neutros iniciais. Modelos só aceitam `{responsavel}`, `{aluno}` (primeiro nome), `{escola}`, `{data}` e `{contato_escola}` — **não existe marcador para descrição, categoria ou providências**, e não há texto livre por envio. Só a Direção edita modelos.
- **Envio:** autorização explícita na prévia; primeira tentativa na hora; falha registra erro **sanitizado** (tipo da falha, nunca endereço ou resposta do servidor) e agenda até 3 tentativas automáticas (1 e 5 minutos). Depois disso, reenvio manual. Pendente ou com falha pode ser cancelado.
- **Idempotência:** cada abertura do diálogo de envio gera um `requestId`; o mesmo pedido nunca gera duas mensagens. Um segundo pedido para o mesmo responsável e modelo exige confirmação ("enviar mesmo assim").
- **Fila:** um worker no Tenant.Host percorre as escolas ativas a cada minuto, cada uma no próprio escopo (conexão, chave e bucket dela). Desligável por `Notifications:RetryWorker`.
- **WhatsApp:** interface `INotificationProvider` pronta; o canal aparece como **desabilitado** até a integração oficial ser configurada (spec 6.2).

## Decisões da Fase 6 (indicadores e implantação)

- **O que conta:** ocorrências registradas, em acompanhamento ou concluídas, pela **data do fato** (fuso de São Paulo) e pela **turma gravada na ocorrência**. Rascunhos e canceladas ficam fora (P4).
- **Denominador:** alunos com matrícula não cancelada na turma em **algum dia do período**. Quem trocou de turma no bimestre conta nas duas turmas e uma vez no total da escola. A explicação aparece no rodapé do dashboard e nas exportações.
- **Período:** bimestre do calendário da escola (padrão: o bimestre de hoje) ou personalizado (até 400 dias). A comparação é com o bimestre anterior, inclusive o último do ano anterior; no personalizado, com o mesmo número de dias imediatamente antes.
- **Taxa por 100 alunos** = ocorrências ÷ matriculados × 100, com uma casa decimal. Turmas sem matrícula não têm taxa. O texto do gráfico lembra que a taxa não classifica turmas nem alunos.
- **Ocorrências por aluno:** página Relatórios, só para Direção e Pedagogia, em **ordem alfabética** (não é ranking) e **nunca exportada**.
- **Exportação (CSV/PDF):** só agregados (resumo, turmas, categorias e semanas). Contagens de **1 a 4 viram "<5"** (0 continua 0), e a taxa de uma turma com contagem suprimida sai como "—". CSV com BOM e ponto e vírgula, para o Excel em pt-BR, e células que começam com `=`, `+`, `-` ou `@` neutralizadas. Cada exportação fica na auditoria.
- **Na tela não há supressão:** o dashboard é visto só por quem já tem acesso aos registros individuais. A supressão vale para o que sai do sistema.
- **Implantação:** imagens próprias (APIs e Caddy com os fronts), certificado wildcard por desafio DNS, `X-Forwarded-*` aceitos só da rede do proxy, certificados OIDC persistentes obrigatórios com cookies seguros, PgBouncer com `auth_query` restrito a `t_*_app`, leitor do registro só com `SELECT` na view e backup por escola cifrado com **age** (chave privada fora da VPS). Primeiro administrador: `create-super-admin`, de uso único.

## Decisões da Fase P (painel) — primeira entrega

- **Métricas (P-10):** cada banco de escola tem a view `metrics.v_counts` com 8 números da escola inteira: alunos ativos, turmas do ano ativo, usuários ativos, ocorrências dos últimos 30 dias, e-mails enviados no mês, e-mails com falha, armazenamento de anexos **comuns** e versão do schema. **Nada de Saúde/Proteção, nem contagem, nem tamanho de anexo protegido.**
- O papel `sinedu_metrics` (senha gerada no Vault pela plataforma no `migrate`) só tem `CONNECT` e `SELECT` nessa view. O teste confirma que ele não lê nenhuma tabela.
- **Coleta:** a cada 6 horas (`Metrics:Worker`), ou pelo botão "Coletar agora" (SUPER_ADMIN). Guardamos uma foto por escola a cada coleta, para a evolução de alunos ativos.
- **Atualização de escolas (P-05):** a página Escolas avisa quando há banco desatualizado (escola em manutenção) e o SUPER_ADMIN atualiza todas em lote.
- **Contas (P-12):** SUPER_ADMIN convida SUPER_ADMIN ou SUPORTE (e-mail que já tem conta de escola é recusado — D19), retira acesso (ninguém retira o próprio, e o último SUPER_ADMIN não sai), busca qualquer conta pelo e-mail para **desbloquear** ou **reiniciar o MFA**. O reinício exige motivo, derruba as sessões, apaga os códigos de recuperação e avisa a pessoa por e-mail. Tudo na auditoria da plataforma.

## SMTP por escola (painel)

- Cada escola pode ter **SMTP próprio**, configurado pelo SUPER_ADMIN no painel (Escolas → **E-mail**). Fica no Vault em `tenants/{id}/smtp`, junto das credenciais da escola. A senha **nunca volta** para a tela, e campo em branco ao editar mantém a atual.
- Saem pelo SMTP da escola: comunicações aos responsáveis, avisos de concessão de papel e convites para aquela escola. Saem pelo **SMTP padrão** (configuração do servidor, `Smtp:*`): e-mails das contas da plataforma e de escolas sem SMTP próprio.
- Uma alteração vale em até 1 minuto para a aplicação da escola (cache).
- **Teste de e-mail** (painel → Sistema): envia uma mensagem de teste pelo SMTP de uma escola (salvo, ou o do formulário antes de salvar) ou pelo padrão, e mostra a resposta do servidor sem a senha. Os testes ficam na auditoria da plataforma.

## Pendências (não bloqueiam o início)

- [ ] Domínio definitivo e acesso ao DNS (certificado wildcard via desafio DNS).
- [ ] Gerador de PDF: avaliar licença do QuestPDF para uso por órgão público. **Já em uso** na exportação excepcional (licença Community); trocar de biblioteca é local a `ExportService`.
- [ ] Prazos de retenção e descarte — definir com as redes e assessoria LGPD.
- [x] Amostras anonimizadas de importação (D7) — `.doc/amostras/alunos-2026-anonimizado.xlsx` (1.032 alunos, 34 turmas), carregada na `escola-teste`.
- [ ] Modelo de texto padrão das comunicações aos responsáveis — **dois modelos neutros já em uso**; validar o texto com as redes.
- [ ] Logo da escola no cabeçalho da ata (D12) — falta o upload de logo no perfil da escola.

### Pendências técnicas da Fase 1 (antes da homologação — F6-08)

- [x] **Chaves do OIDC:** certificados PFX persistentes (`Oidc:SigningCertificatePath`/`EncryptionCertificatePath`), obrigatórios fora do ambiente local.
- [ ] **PgBouncer para bancos de escola:** `auth_query` restrito a `t_*_app` pronto em `infra/prod/init-roles.sql`; validar na homologação.
- [ ] **Papel de leitura do registro:** `sinedu_registry_reader` criado por `infra/prod/init-roles.sql` (em dev ainda usa o usuário do Postgres); validar na homologação.
- [ ] **Vault em produção:** autenticação AppRole por host, com as políticas `sinedu-platform` e `sinedu-tenant-app` de `infra/vault/policies`.
- [x] **Proxy reverso:** `X-Forwarded-*` só da rede do proxy (`Sinedu:TrustedProxyNetwork`) e Caddyfile com wildcard em `infra/prod`.

### Pendências técnicas da Fase 4

- [ ] **Auditoria de acessos ao registro na tela** (matriz: Diretor vê quem acessou; P9 para Pedagogia). Os eventos já são gravados; falta a visualização.
- [ ] **Anexos de dev corrompidos:** anexos enviados antes da correção do cliente S3 (checksum em trailer) na `escola-demo` não abrem (a verificação de integridade bloqueia). Só afeta dados de teste.
