# Especificação do MVP — Sistema Integrado de Informações Escolares

**Versão:** 1.0 (escopo fechado)  
**Data:** 17/11/2026  
**Objetivo do documento:** orientar o Claude na arquitetura, planejamento e implementação de uma aplicação web para gestão consolidada de informações escolares e ocorrências pedagógicas.

> **Regra prioritária:** dados de saúde e registros sigilosos de proteção do aluno serão criptografados e **somente usuários com perfil de DIREÇÃO ou PEDAGOGIA, vinculados à escola do aluno, poderão acessá-los**. Professores e administradores técnicos **não** poderão visualizá-los por nenhum meio, inclusive API, exportação, logs, consultas administrativas ou interface. Falha nessa regra bloqueia a entrega do MVP.

---

## 1. Visão geral

Desenvolver uma aplicação web que consolide dados de alunos provenientes de relatórios escolares e, futuramente, de integrações autorizadas com sistemas oficiais. A aplicação permite consultar escolas, turmas, equipes e alunos; registrar ocorrências pedagógicas vinculadas ao histórico do aluno; comunicar responsáveis; e acompanhar indicadores bimestrais.

O sistema deverá atender a **mais de uma escola**, com isolamento rigoroso de dados entre escolas. A visão principal é institucional e pedagógica, não um portal aberto ao público.

### 1.1 Resultados esperados

1. Importar cadastros e matrículas sem recadastrar os alunos manualmente.
2. Consolidar, numa tela de aluno, dados cadastrais, vínculos escolares, responsáveis e histórico pedagógico.
3. Consultar alunos pela escola, série e turma.
4. Registrar, acompanhar e gerar ata de ocorrências.
5. Registrar notificações aos responsáveis e permitir envio por e-mail no MVP.
6. Produzir indicadores bimestrais confiáveis por aluno, turma e escola.
7. Proteger dados médicos e informações sigilosas com criptografia, controle estrito de acesso e auditoria.

## 2. Premissas e limites do MVP

- Aplicação **web responsiva**, prioritariamente para desktop, utilizável em tablets.
- Autenticação individual, sem contas compartilhadas.
- Uma pessoa pode ter vínculo com uma ou mais escolas, mas as permissões são verificadas **por escola e por papel**.
- **Importação assistida** de arquivos CSV e XLSX é a forma inicial obrigatória de alimentação. Importação de PDF textual estruturado pode ser acrescentada no MVP apenas para modelos de relatório identificados e testados; PDFs digitalizados/OCR não são dependência da primeira entrega.
- Integrações diretas com plataformas governamentais, incluindo eventual SERE/PR, serão modeladas por uma interface de conectores, mas **não são condição para liberar o MVP**. Não presumir API pública, autorização de scraping ou possibilidade de contornar login/captcha/MFA.
- Canais de comunicação: **e-mail com configuração de provedor** e registro de tentativa/resultado no MVP; **WhatsApp preparado arquiteturalmente**, mas ativado apenas após integração oficial autorizada e configuração de modelos/consentimentos quando aplicáveis.
- Não haverá aplicativo mobile nativo, portal de pais, prontuário clínico, aconselhamento médico, integração policial/judicial automática nem sistema de notas/frequência nesta fase.
- Cada escola parametriza ano letivo, bimestres e vínculos de profissionais.

## 3. Perfis e autorização

**Papéis:** `ADMIN_TECNICO`, `DIRETOR`, `PEDAGOGO`, `PROFESSOR` e `CONSULTA`.

- **Administrador técnico:** administra configurações, usuários, escolas, importações operacionais e saúde da aplicação, mas **não recebe permissão de leitura dos dados médicos nem dos registros sigilosos em texto claro**. Rotinas técnicas devem operar sem revelar tais conteúdos.
- **Diretor:** gerencia alunos, turmas, equipe, ocorrências, relatórios, saúde e proteção sigilosa **da(s) escola(s) de sua vinculação autorizada**.
- **Pedagogo:** acessa e mantém ocorrências, comunicações e os dois módulos sensíveis **da(s) escola(s) de sua vinculação autorizada**.
- **Professor:** consulta apenas dados acadêmico-cadastrais comuns de alunos das turmas às quais está vinculado. **Proibido acesso a qualquer dado dos módulos Saúde e Informações Sigilosas**, inclusive resumos, avisos, contagens, anexos e retornos indiretos de API. No MVP, não tem acesso às ocorrências privadas por padrão.
- **Consulta:** acesso somente aos dados comuns expressamente concedidos da escola correspondente; sem acesso aos módulos sensíveis e sem edição.

### 3.1 Matriz mínima de permissões

| Recurso | Admin técnico | Diretor | Pedagogo | Professor | Consulta |
|---|---|---|---|---|---|
| Configuração técnica, usuários e escolas | Sim | Parcial por escola | Não | Não | Não |
| Listagem escolar, turmas e dados cadastrais comuns | Operacional conforme necessidade | Sim | Sim | Só turmas vinculadas | Conforme concessão |
| Criar/editar ocorrência pedagógica | Não | Sim | Sim | Não | Não |
| Consultar histórico de ocorrências | Não | Sim | Sim | Não | Não |
| Autorizar e registrar comunicação de ocorrência | Não | Sim | Sim | Não | Não |
| Ver indicadores pedagógicos bimestrais | Não | Sim | Sim | Não | Não |
| **Visualizar ou editar Saúde** | **Não** | **Sim** | **Sim** | **NUNCA** | **Não** |
| **Visualizar ou editar Proteção/Sigilo** | **Não** | **Sim** | **Sim** | **NUNCA** | **Não** |
| Exportar relatórios comuns | Operacional e restrito | Sim | Sim | Não por padrão | Não |
| Exportar conteúdo sensível | **Não** | Excepcional, auditado | Excepcional, auditado | **NUNCA** | **Não** |

**Implementação obrigatória:** aplicar políticas no back-end a cada requisição (RBAC + escopo de escola, aluno e turma); UI escondendo botões é apenas defesa adicional. Quando um usuário tiver papéis diferentes em escolas diferentes, os privilégios de uma escola **não podem** ser herdados pela outra. Usar princípio do menor privilégio e impedir acesso por IDs manipulados (IDOR).

## 4. Estrutura de dados funcional

### 4.1 Escola, equipe e organização escolar

- **Escola:** identificador, nome, código institucional quando disponível, endereço, telefone, e-mail, status.
- **Ano letivo:** ano, início e fim, situação.
- **Bimestre:** identificação, data inicial/final e ano letivo; períodos configuráveis por escola.
- **Série/Etapa:** nome, código e ordenação.
- **Turma:** escola, ano letivo, série, nome/identificação, turno, capacidade opcional, status.
- **Profissional:** nome, função, contatos profissionais e vínculo com escola/turmas; incluir professores, direção e pedagogia.
- **Matrícula:** aluno, turma, ano letivo, situação, datas de início/fim, número/código externo quando existente; preservar histórico entre anos/turmas.

### 4.2 Cadastro comum do aluno

- Identificador interno estável (UUID ou equivalente) e códigos de origem.
- Nome completo, nome social quando informado, data de nascimento, identificação institucional, situação cadastral.
- Dados documentais **somente se necessários, fornecidos e autorizados**; não presumir que toda fonte possui todos os campos.
- Endereço completo, telefone(s), e-mail quando disponível.
- Responsáveis: nome, vínculo, telefone(s), e-mail(s), contato principal, autorização de contato e relacionamento N:N (um responsável pode estar vinculado a mais de um aluno).
- Escola, ano letivo, turma, série, turno, professores associados, diretor e equipe pedagógica.
- Histórico de matrículas, movimentações, origem e data da última atualização de cada conjunto de dados.
- Campos importados ausentes devem ficar vazios/`não informado`, **nunca** preenchidos por inferência.

### 4.3 Módulo protegido: Saúde e Informações Médicas

**Acesso exclusivo:** DIRETOR e PEDAGOGO autorizados para a escola do aluno.

Campos possíveis, todos opcionais segundo necessidade e fonte legítima:
- Tipo sanguíneo e fator Rh (se comprovadamente informado).
- Alergias e respectivos cuidados descritos pela fonte.
- Condições de saúde relevantes para o atendimento escolar.
- Medicamentos de uso informado, orientações de administração **sem gerar prescrições**.
- Restrições para atividades, necessidades de apoio e adaptações.
- Contatos de emergência e instruções autorizadas de primeiros cuidados.
- Documentos comprobatórios anexados de forma protegida.
- Origem da informação, data de atualização, responsável pelo registro e prazo de revisão.

**Não criar qualquer indicador de risco, diagnóstico inferido ou recomendação clínica automatizada.**

### 4.4 Módulo protegido: Informações Sigilosas e Proteção do Aluno

**Acesso exclusivo:** DIRETOR e PEDAGOGO autorizados para a escola do aluno.

- Registros de guarda, tutelas, medidas protetivas e restrições judiciais documentadas.
- Pessoas autorizadas e não autorizadas a retirar o aluno, com fundamentação e vigência quando aplicável.
- Situações de violência doméstica ou proteção da criança/adolescente que a instituição esteja legitimamente autorizada a registrar e precise conhecer para sua atuação.
- Informações civis ou criminais **estritamente necessárias à proteção e à gestão escolar**, com fonte, contexto, data e nível de verificação; evitar alegações não verificadas e dados de terceiros sem necessidade.
- Documentos, autoridade/origem, data de emissão, início/fim de validade, situação vigente/revogada e observações relevantes.
- Histórico versionado de alterações e responsável por cada alteração.

**Regras:** manter dados protegidos fisicamente/logicamente separados do cadastro comum; impedir exposição desses dados em listas, buscas globais, dashboards, notificações, arquivos de exportação comuns ou respostas dos endpoints de aluno/turma. Não misturar informações de proteção com ocorrência disciplinar nem utilizá-las em ranking ou pontuação.

## 5. Gestão de ocorrências pedagógicas

Ocorrências são registros institucionais de fatos e providências, não diagnósticos ou rótulos sobre alunos.

**Campos obrigatórios:** escola, aluno, matrícula/turma no momento do fato, data/hora do acontecimento, categoria, descrição factual, usuário que registrou e data do registro.

**Campos opcionais:** gravidade administrativa parametrizada, local, envolvidos (se legítimo registrar), providências, encaminhamentos, anexos, estado (`rascunho`, `registrada`, `em_acompanhamento`, `concluida`), data de conclusão e observações.

Categorias iniciais configuráveis: indisciplina, conflito, atraso recorrente, desrespeito, dano material, outros. **Ocorrências de proteção/violência com informação sigilosa devem ser registradas no módulo protegido, e não expostas na classificação disciplinar.**

### 5.1 Fluxo

1. DIRETOR ou PEDAGOGO seleciona aluno ou turma e inicia ocorrência.
2. Preenche campos; sistema valida vínculo e permissões.
3. Salva em rascunho ou registra definitivamente.
4. Uma ocorrência registrada aparece na linha do tempo pedagógica do aluno.
5. Sistema permite gerar **ata/PDF para impressão** com dados pertinentes e trilha de autoria, sem inserir automaticamente conteúdo dos módulos protegidos.
6. Edições posteriores criam histórico de versão (autor, data, justificativa). Não apagar silenciosamente ocorrências formalizadas.
7. Usuário autorizado acompanha providências e encerra registro.

**Atenção:** alunos com nomes semelhantes são selecionados por identidade/matrícula; a turma preservada na ocorrência é a **turma da data do fato**, mesmo que o aluno mude de turma depois.

## 6. Comunicação com responsáveis

### 6.1 MVP

- A partir de uma ocorrência registrada, DIRETOR ou PEDAGOGO pode preparar comunicação para responsáveis vinculados e com contato disponível.
- Exibir destinatários, canal e prévia antes de confirmar envio.
- Implementar **envio por e-mail**, via provedor configurado, com fila/retry seguro quando aplicável.
- Registrar: ocorrência, destinatário, canal, texto/modelo enviado, autor da autorização, momento, situação (`pendente`, `enviado`, `falhou`, `cancelado`), erro técnico sanitizado e identificador do provedor se houver.
- Permitir reenviar após falha, evitando disparos duplicados acidentais (idempotência).
- Usar modelos neutros e **mínimo necessário**. Por padrão, convidar o responsável a contatar a escola sem detalhar informações íntimas. **Nunca incluir dados de saúde ou proteção sigilosa em mensagens automáticas.**
- Registrar preferências, autorizações e fundamento aplicável de comunicação; não assumir autorização universal.

### 6.2 Preparação para o futuro

Criar interface `INotificationProvider`, com implementações plugáveis (e-mail agora; WhatsApp Business API oficial futuramente). **Não usar automação não autorizada do WhatsApp Web.** O canal WhatsApp deve permanecer desabilitado até configuração, conformidade e testes.

## 7. Importação e atualização de dados

### 7.1 Entrada obrigatória

Importar arquivos CSV e XLSX de alunos, responsáveis, turmas, profissionais e matrículas, incluindo arquivos com nomes de colunas diferentes, mediante **mapeamento assistido**.

**Processo:**

1. Usuário autorizado seleciona escola e tipo de importação.
2. Envia arquivo; sistema valida extensão, tamanho, conteúdo e varredura de anexos conforme infraestrutura.
3. Pré-visualiza linhas, detecta campos e permite ajustar mapeamento.
4. Valida campos obrigatórios, formatos, vínculos e inconsistências; mostra erros por linha.
5. Exibe prévia: novos, atualizações, possíveis duplicados, conflitos e registros rejeitados.
6. Usuário confirma importação; grava em lote, de forma transacional por unidades controladas e com resultado auditável.
7. Registra lote, fonte, executor, data, totais, erros e campo de última atualização.

**Regras de consolidação:** usar identificadores institucionais/matrícula quando confiáveis; fallback por combinação de atributos somente como **sugestão de correspondência**, nunca unir dois alunos automaticamente com base apenas no nome. Não substituir campos preenchidos por dados vazios sem confirmação. Não copiar vínculos entre escolas por coincidência de nome. Importações de saúde/sigilo só podem ser executadas por DIRETOR/PEDAGOGO autorizado, em fluxo dedicado criptografado, nunca por processamento administrativo comum.

### 7.2 Integrações governamentais posteriores

Projetar interface `ISchoolDataConnector` para conectores autorizados, com importação agendada apenas quando a origem disponibilizar meio permitido. Cada conector deve informar versão, escopo, autenticação, disponibilidade, limites e política de uso.

- Verificar documentação/convênio/permissão antes de qualquer implementação real.
- Não armazenar senhas em texto puro; usar cofre de segredos, expiração e renovação seguras.
- Não contornar captcha, segundo fator ou restrições de acesso.
- Se não houver API ou acesso formal, manter o fluxo de upload de relatórios.

## 8. Telas e navegação

1. **Login:** autenticação, recuperação de acesso e seleção de escola quando aplicável.
2. **Dashboard:** KPIs do bimestre selecionado, tendências, filtros de escola/ano/turma/categoria.
3. **Escolas e períodos:** escolas autorizadas, anos letivos, bimestres.
4. **Turmas:** busca, filtros, quantidade de alunos, professores vinculados; clicar abre alunos da turma.
5. **Lista de alunos:** nome, matrícula/código, turma, situação; filtros e paginação.
6. **Painel do aluno:** cabeçalho cadastral; responsáveis/contatos; endereço; turma, série, professores, diretor, pedagogia; histórico de matrículas; histórico de ocorrências e comunicações (para papéis autorizados). Abas **Saúde** e **Proteção/Sigilo** **existem somente para diretor e pedagogo autorizados**, sem vazamento por chamadas antecipadas de API.
7. **Ocorrências:** listagem com filtros, detalhe, novo registro, acompanhamento, geração da ata e notificações.
8. **Importações:** upload, mapeamento, prévia, confirmação, histórico e erros.
9. **Relatórios:** métricas por bimestre, turma, categoria e alunos; exportação comum apenas conforme perfil.
10. **Administração:** usuários, vínculos, equipe, categorias, modelos de e-mail, configuração de períodos e integração futura.

### 8.1 UX essencial

- Pesquisa por nome/código com cuidado para não expor dados a usuários sem vínculo.
- Layout responsivo; estados vazios, validação clara, paginação.
- Confirmações para operações irreversíveis/ações de envio.
- Badges de situação e distinção clara entre `rascunho` e `registrada`.
- Nunca enviar informações sensíveis a serviços de telemetria, analytics, autocomplete externo ou gravação de sessão.

## 9. Dashboard e relatórios bimestrais

**Filtros:** escola, ano letivo, bimestre, série, turma, categoria e período personalizado quando autorizado.

**Indicadores mínimos:**

- Total de alunos matriculados no recorte.
- Total de ocorrências registradas no recorte.
- Quantidade de alunos distintos com pelo menos uma ocorrência.
- Ocorrências por turma e por categoria.
- Ocorrências por aluno (consulta restrita a direção/pedagogia).
- Evolução por semana e comparação com o bimestre anterior quando houver dados.
- Percentual de ocorrências concluídas / em acompanhamento.
- Comunicações enviadas, pendentes e falhas.
- **Comparação entre turmas** com contagem absoluta e **taxa de ocorrências por 100 alunos matriculados**, destacando turmas com menores/maiores taxas e tendências; contextualizar o tamanho das turmas, evitar rotular crianças ou turmas como “melhores/piores”.

**Cálculos:** contar somente ocorrências efetivamente `registradas` ou posteriores, pela data do fato no bimestre, respeitando escola e turma histórica. Documentar denominadores e política para bimestres com matrícula variável. Evitar ranking de alunos público, perfis de risco automatizados e uso de registros médicos/sigilosos em métricas.

**Exportação:** CSV/PDF dos indicadores autorizados, sem dados médicos, jurídicos ou registros de proteção. Aplicar supressão/agregação quando amostras muito pequenas puderem identificar menores indiretamente.

## 10. Segurança, privacidade e conformidade — inegociável

### 10.1 Criptografia

- **Trânsito:** HTTPS/TLS moderno e obrigatório; cookies seguros.
- **Repouso:** criptografia do banco, objetos/anexos e backups.
- **Campo/aplicação:** proteção criptográfica adicional em campos dos módulos Saúde e Proteção/Sigilo, preferencialmente com criptografia autenticada (por exemplo AES-256-GCM), com chaves fora do banco de dados, segregadas e geridas por KMS/cofre de segredos.
- **Senhas:** hash forte e apropriado (Argon2id ou alternativa reconhecida), nunca criptografia reversível.
- **Chaves:** segregação por ambiente, rotação planejada, acesso mínimo e trilha de operações; jamais armazenar em código, planilha, log ou repositório.
- **Buscas:** não descriptografar o acervo inteiro nem criar índices em texto claro com conteúdo médico/sigiloso.

### 10.2 Acesso e auditoria

- Cada rota/API consulta papel, escola e vínculo; usar `deny by default`.
- **Professor nunca pode obter qualquer conteúdo dos módulos sensíveis, inclusive via ID direto, relatórios, anexos, filtros, busca e metadados desnecessários.**
- **Administrador técnico não pode contornar a autorização funcional para ler conteúdo sensível.** Não implementar impersonação que contorne regra.
- Auditar visualizações, criações, alterações, exportações excepcionais e acessos negados aos dados protegidos: usuário, escola, aluno, ação, instante, origem e resultado, **sem gravar conteúdo sensível**.
- Proteção contra enumeração de IDs, vazamento entre escolas, CSRF/XSS/injeção, abuso de API e upload malicioso.
- Expiração de sessão, rate limiting, controle de tentativas de login, política de senhas e MFA recomendado/obrigatório para papéis privilegiados conforme implantação.
- Páginas sensíveis sem cache público, previews ou indexação por buscadores.
- Tratar dados de crianças/adolescentes pelo princípio do melhor interesse, finalidades específicas, minimização, acesso restrito, transparência e prazos de retenção definidos com a instituição responsável e assessoria jurídica/LGPD.
- Definir processo de retificação, revogação de acesso, revisão de medidas vencidas, retenção/descarte e resposta a incidente. **Não excluir documentos formalmente relevantes de maneira automática e indiscriminada.**

### 10.3 Critério de bloqueio de release

Qualquer teste que exponha dados médicos ou sigilosos a `PROFESSOR`, `CONSULTA` ou `ADMIN_TECNICO`, ou a um `DIRETOR`/`PEDAGOGO` de outra escola, **impede publicar o MVP**.

## 11. Modelo relacional inicial sugerido

> Nomes ilustrativos. Claude deve normalizar relacionamentos, chaves e índices durante a implementação.

- `schools`
- `school_years`
- `academic_terms` (bimestres por escola/ano)
- `grades`
- `classrooms`
- `users`
- `school_user_roles` (usuário + escola + papel)
- `staff_members`
- `staff_school_assignments`
- `teacher_classroom_assignments`
- `students`
- `student_enrollments`
- `guardians`
- `student_guardians`
- `student_addresses` (ou endereço versionado)
- `student_health_records` (**payload criptografado**, versões e metadados mínimos)
- `student_protection_records` (**payload criptografado**, versões e metadados mínimos)
- `secure_attachments` (referência a objeto criptografado, política por recurso)
- `occurrence_categories`
- `student_occurrences`
- `occurrence_revisions`
- `occurrence_attachments`
- `notifications`
- `notification_attempts`
- `import_batches`
- `import_batch_rows` (erros e status com mascaramento)
- `external_source_references`
- `audit_events`

**Pontos de modelagem:** `school_id` obrigatório nos recursos escolares para escopo; FKs e índices por escola/ano/turma; alunos podem ter matrículas em anos diferentes; responsáveis N:N; integridade de histórico; transações; controle otimista de concorrência em edição. Não adicionar colunas sensíveis em tabelas de listagem comum.

## 12. Arquitetura sugerida (não obrigatória)

Adequada para implementação incremental com Claude:

- **Frontend:** React + TypeScript, Vite, componentes acessíveis e controle de sessão/rotas.
- **Backend:** ASP.NET Core em versão .NET LTS vigente, Web API com OpenAPI e validação de entrada.
- **Banco:** PostgreSQL com migrations e índices por escopo.
- **Arquitetura:** monólito modular, com módulos `Identity`, `Schools`, `Students`, `ProtectedRecords`, `Occurrences`, `Imports`, `Notifications` e `Analytics`.
- **Armazenamento:** objetos/anexos privados criptografados; URLs assinadas curtas somente após autorização no servidor.
- **Tarefas assíncronas:** fila ou job seguro para e-mails/importações grandes; reprocessamento idempotente.
- **Observabilidade:** logs estruturados sanitizados, métricas técnicas, tracing sem payloads pessoais.
- **Infraestrutura:** Docker Compose para desenvolvimento; configurações por ambiente; migrações, seeds de categorias e documentação para deploy.

Se escolher stack diferente, justificar o ganho e preservar os requisitos de segurança. Evitar microsserviços, IA, OCR complexo ou dependências pagas desnecessárias no MVP.

## 13. API — contratos funcionais ilustrativos

Autorização obrigatória em todos os endpoints, inclusive listagens e anexos:

```http
GET    /api/schools
GET    /api/schools/{schoolId}/classrooms?year=2026
GET    /api/schools/{schoolId}/classrooms/{classroomId}/students
GET    /api/schools/{schoolId}/students?query=...
GET    /api/schools/{schoolId}/students/{studentId}
GET    /api/schools/{schoolId}/students/{studentId}/enrollments
GET    /api/schools/{schoolId}/students/{studentId}/occurrences
POST   /api/schools/{schoolId}/students/{studentId}/occurrences
PATCH  /api/schools/{schoolId}/occurrences/{occurrenceId}
GET    /api/schools/{schoolId}/occurrences/{occurrenceId}/minutes
POST   /api/schools/{schoolId}/occurrences/{occurrenceId}/notifications
GET    /api/schools/{schoolId}/students/{studentId}/health
PUT    /api/schools/{schoolId}/students/{studentId}/health
GET    /api/schools/{schoolId}/students/{studentId}/protection
PUT    /api/schools/{schoolId}/students/{studentId}/protection
POST   /api/schools/{schoolId}/imports/preview
POST   /api/schools/{schoolId}/imports/{batchId}/confirm
GET    /api/schools/{schoolId}/imports/{batchId}
GET    /api/schools/{schoolId}/analytics/terms/{termId}
```

**Proibição:** endpoints comuns de aluno **jamais** devem serializar propriedades médicas/sigilosas, mesmo que `null` ou escondidas no frontend. Endpoints protegidos usam DTOs isolados, serialização explícita e política específica.

## 14. Critérios de aceite / testes obrigatórios

### Funcionais

1. Importar arquivo de exemplo com 2 turmas e respectivos alunos/responsáveis; dados aparecem corretamente em listas e painel.
2. Reimportar dados atualizados sem duplicar indevidamente alunos ou perder histórico.
3. Exibir professores, direção e pedagogia associados à escola/turma conforme vinculação.
4. Diretor/pedagogo cria ocorrência, registra providência, gera ata e a vê no histórico do aluno.
5. Notificação por e-mail gera tentativa, sucesso/falha e auditoria; falhas são reprocessáveis sem duplicidade automática.
6. Dashboard apresenta totais por bimestre, aluno, turma e categoria, comparação entre turmas e taxas por 100 alunos.
7. Histórico de matrícula mantém turma/ano de ocorrência antiga mesmo após transferência.

### Segurança (testes automatizados de integração e autorização)

8. Professor acessando `GET .../health` ou `.../protection` recebe bloqueio; conteúdo não aparece em JSON, cabeçalhos, logs, cache ou anexos.
9. Professor tentando exportar, consultar busca global ou adivinhar ID de registro protegido continua bloqueado.
10. Admin técnico também não obtém dados protegidos em texto claro.
11. Diretor e pedagogo de **outra escola** recebem bloqueio, inclusive após trocar IDs nas URLs.
12. Diretor e pedagogo da escola correta podem consultar/atualizar conteúdo sensível após autenticação apropriada; acesso fica auditado.
13. Dados médicos/sigilosos persistidos não ficam legíveis em consultas diretas às tabelas ou a objetos brutos sem as chaves da aplicação.
14. Arquivos e backups seguem criptografia e regras de autorização.
15. Comunicação de ocorrência não inclui informações médicas ou de proteção; relatórios analíticos também não.
16. Importação com conflito solicita revisão, não mescla alunos de escolas distintas silenciosamente.

**Entregável de evidência:** suite de testes, relatório de execução, matriz de permissões validada e instruções de como repetir os testes localmente.

## 15. Ordem de implementação recomendada

**Fase 1 — Fundação e segurança**
- Estrutura do monólito modular, banco, migrações, login, papéis por escola, políticas de autorização, auditoria, criptografia e testes de isolamento.

**Fase 2 — Cadastros e navegação**
- Escolas, períodos, séries, turmas, profissionais, alunos, responsáveis, matrículas; listas e painel do aluno.

**Fase 3 — Importação**
- CSV/XLSX, mapeamento, prévia, validação, deduplicação assistida, confirmação, histórico e erros.

**Fase 4 — Módulos protegidos**
- Saúde e Proteção/Sigilo com criptografia de campos e anexos, UI dedicada, auditoria e testes impeditivos de release.

**Fase 5 — Ocorrências e comunicações**
- Registro, revisões, linha do tempo, geração de ata, modelos e envio de e-mail auditado.

**Fase 6 — Indicadores e qualidade**
- Dashboard bimestral, comparação de turmas, exportação segura, testes end-to-end, documentação e deploy.

O Claude pode detalhar o backlog e propor pequenos ajustes técnicos, mas não alterar as regras funcionais e de sigilo sem aprovação explícita.

## 16. Fora do escopo / roadmap

- Conexão automática com SERE/PR ou outros sistemas oficiais (até confirmação de integração autorizada).
- Captura por crawler ou navegação automatizada autenticada em plataformas oficiais sem autorização.
- WhatsApp Business API em produção (apenas contrato/estrutura para integrar).
- Leitura/OCR de PDFs digitalizados em massa.
- Aplicativo mobile nativo, portal do aluno/responsável.
- Notas, frequência, boletim e demais módulos acadêmicos não solicitados.
- Inteligência artificial para classificação de comportamento, predição de risco ou tratamento de dados médicos/sigilosos.
- Rankings públicos ou julgamentos automáticos de alunos.

## 17. Instrução final para o Claude — como iniciar o desenvolvimento

> **Você é o responsável técnico por implementar este MVP.** Leia integralmente a especificação, trate os itens de segurança da seção 10 e os testes da seção 14 como bloqueadores de entrega e **não invente integrações externas**.
>
> 1. Produza primeiro uma arquitetura resumida, diagrama dos módulos, modelo de dados, matriz de permissões e backlog ordenado por dependências.
> 2. Identifique decisões técnicas que não alteram o escopo e adote padrões simples, seguros e sustentáveis.
> 3. Construa o sistema por incrementos executáveis: migrations, API, frontend, testes e documentação de cada módulo.
> 4. Desde a primeira etapa, implemente autorização por escola e papel, criptografia e testes de negação de acesso. Não deixe a segurança para o final.
> 5. Crie dados fictícios para desenvolvimento/testes; **nunca** use dados reais de crianças em seeds, prints ou demonstrações.
> 6. Entregue instruções de instalação local, variáveis de ambiente, migrations, execução de testes e deploy. Não inclua credenciais no repositório.
> 7. Antes de considerar concluído, demonstre os critérios de aceite e os testes de vazamento de dados protegidos.
>
> **Regra de interpretação:** em caso de ambiguidade, priorize privacidade, menor privilégio e histórico confiável. Não ampliar escopo sem sinalizar a decisão.

---

**Status:** escopo funcional do MVP fechado conforme conversa. Integrações governamentais e WhatsApp ficam preparados, porém não ativados nesta primeira entrega.
