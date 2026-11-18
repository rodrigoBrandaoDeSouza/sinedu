# Matriz de permissões — Sinedu MVP

Base: spec seção 3.1 + decisões D6, D9, D10, D17, D19, D20. Itens com ⚠️ são propostas a validar ([decisões](../decisoes-mvp.md#propostas-para-validação)).

## 1. Dois níveis de papel

| Nível | Papéis | Onde ficam | Onde valem |
|-------|--------|-----------|------------|
| **Plataforma** | `SUPER_ADMIN`, `SUPORTE` | `sinedu_platform.platform_roles` | Só em `painel.sinedu.com.br` |
| **Escola** | `DIRETOR`, `PEDAGOGO`, `ADMIN_TECNICO`, `PROFESSOR`, `CONSULTA`, `SUPORTE_PLATAFORMA` (temporário) | `access.user_roles` no banco de cada escola | Só no subdomínio daquela escola |

- Papéis de plataforma **nunca** valem dentro de uma escola. Contas de plataforma não recebem papéis de escola, exceto `SUPORTE_PLATAFORMA` liberado pelo Diretor (D17, D19).
- Papéis de uma escola **nunca** valem em outra: cada escola tem banco e sessão próprios.
- Vários papéis na mesma escola → união das permissões.

## 2. Painel da plataforma (`painel.`)

Legenda: ✅ sim · ❌ não · ⛔ **nunca**

| Ação | SUPER_ADMIN | SUPORTE |
|------|:-----------:|:-------:|
| Cadastrar escola, definir subdomínio, provisionar | ✅ | ❌ |
| Suspender, reativar, iniciar encerramento | ✅ | ❌ |
| Convidar o **primeiro** Diretor de uma escola | ✅ | ❌ |
| Convidar/conceder qualquer papel depois que a escola tem Diretor | ⛔ | ⛔ |
| Redes | ✅ | ver |
| Planos, pagantes, contratos, faturas, baixa de pagamento | ✅ | ❌ |
| Alertas de cobrança | ✅ | ver |
| Métricas agregadas por escola | ✅ | ✅ |
| Status de provisionamento, versão de schema, disparar migrations | ✅ | ver |
| Contas de plataforma (criar/revogar SUPER_ADMIN e SUPORTE) | ✅ | ❌ |
| Desbloquear conta ou resetar MFA de usuário de escola | ✅ (auditado, avisa o usuário por e-mail) | ❌ |
| Pedir acesso de suporte a uma escola | ✅ | ✅ |
| Auditoria da plataforma | ✅ | ❌ |
| Ver qualquer dado de aluno, equipe ou ocorrência | ⛔ | ⛔ |

## 3. Dentro da escola (`{escola}.`)

Legenda: ✅ sim · 📚 só turmas vinculadas/concedidas · ❌ não · ⛔ **nunca (teste bloqueante de release)**

Abreviações: **Dir** Diretor · **Ped** Pedagogo · **Adm** ADMIN_TECNICO · **Prof** Professor · **Cons** Consulta · **Sup** SUPORTE_PLATAFORMA (temporário, liberado pelo Diretor)

### Acesso e configuração

| Ação | Dir | Ped | Adm | Prof | Cons | Sup |
|------|:---:|:---:|:---:|:----:|:----:|:---:|
| Conceder/revogar DIRETOR e PEDAGOGO | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Conceder/revogar ADMIN_TECNICO | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Convidar/revogar PROFESSOR e CONSULTA | ✅ | ❌ | ✅ | ❌ | ❌ | ❌ |
| Conceder turmas ao perfil Consulta | ✅ | ❌ | ✅ | ❌ | ❌ | ❌ |
| Aprovar, recusar e revogar acesso de suporte | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Ver registro das sessões de suporte | ✅ | ❌ | ✅ | ❌ | ❌ | ❌ |
| Perfil da escola e cabeçalho da ata | ✅ | ❌ | ✅ | ❌ | ❌ | ver |
| Anos letivos, bimestres, séries, turmas | ✅ | ❌ | ✅ | ❌ | ❌ | ver |
| Equipe e vínculos professor↔turma | ✅ | ✅ ⚠️ P7 | ✅ | ❌ | ❌ | ver |
| Categorias, gravidades, modelos de e-mail | ✅ | ✅ | ✅ | ❌ | ❌ | ver |

### Cadastro comum

| Ação | Dir | Ped | Adm | Prof | Cons | Sup |
|------|:---:|:---:|:---:|:----:|:----:|:---:|
| Listar turmas (estrutura, nº de alunos) | ✅ | ✅ | ✅ | 📚 | 📚 | ✅ |
| Listar/buscar alunos | ✅ | ✅ | ❌ ⚠️ P6 | 📚 | 📚 | ✅ |
| Painel do aluno — identificação, matrícula, turma, equipe | ✅ | ✅ | ❌ | 📚 | 📚 | ✅ |
| Painel do aluno — responsáveis e contatos | ✅ | ✅ | ❌ | 📚 ⚠️ P2 | ❌ ⚠️ P2 | ✅ |
| Painel do aluno — endereço | ✅ | ✅ | ❌ | ❌ ⚠️ P2 | ❌ | ✅ |
| Histórico de matrículas | ✅ | ✅ | ❌ | 📚 | 📚 | ✅ |
| Editar cadastro manualmente | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |

### Importação

| Ação | Dir | Ped | Adm | Prof | Cons | Sup |
|------|:---:|:---:|:---:|:----:|:----:|:---:|
| Importação comum (upload, mapeamento, prévia, confirmar) | ✅ | ❌ ⚠️ P8 | ✅ | ❌ | ❌ | ❌ |
| Histórico e erros de importação | ✅ | ❌ | ✅ | ❌ | ❌ | ✅ |
| Resolver possíveis duplicados | ✅ | ❌ | ✅ | ❌ | ❌ | ❌ |
| Importação de Saúde/Proteção (fluxo dedicado) | ✅ | ✅ | ⛔ | ⛔ | ⛔ | ⛔ |

### Ocorrências e comunicação

| Ação | Dir | Ped | Adm | Prof | Cons | Sup |
|------|:---:|:---:|:---:|:----:|:----:|:---:|
| Criar, editar, registrar, concluir, cancelar ocorrência | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Ver rascunho | autor | autor | ❌ | ❌ | ❌ | ❌ |
| Ver ocorrências registradas / linha do tempo | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Gerar ata/PDF | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Preparar, autorizar e reenviar comunicação | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Ver histórico de comunicações | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |

### Indicadores

| Ação | Dir | Ped | Adm | Prof | Cons | Sup |
|------|:---:|:---:|:---:|:----:|:----:|:---:|
| Dashboard bimestral | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Ocorrências por aluno | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Exportar indicadores (CSV/PDF, supressão < 5) | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |

### Módulos protegidos — Saúde e Proteção/Sigilo

| Ação | Dir | Ped | Adm | Prof | Cons | Sup |
|------|:---:|:---:|:---:|:----:|:----:|:---:|
| Ver existência da aba / do registro | ✅ | ✅ | ⛔ | ⛔ | ⛔ | ⛔ |
| Ler conteúdo (step-up ≤ 15 min) | ✅ | ✅ | ⛔ | ⛔ | ⛔ | ⛔ |
| Criar/editar (nova versão, com justificativa) | ✅ | ✅ | ⛔ | ⛔ | ⛔ | ⛔ |
| Histórico de versões e anexos | ✅ | ✅ | ⛔ | ⛔ | ⛔ | ⛔ |
| Exportação excepcional (justificativa, marca d'água, auditada) | ✅ | ✅ | ⛔ | ⛔ | ⛔ | ⛔ |
| Revisões pendentes / medidas vencidas | ✅ | ✅ | ⛔ | ⛔ | ⛔ | ⛔ |
| Auditoria de acessos ao registro | ✅ | ❌ ⚠️ P9 | ⛔ | ⛔ | ⛔ | ⛔ |

**⛔ inclui:** contagens, alertas ("aluno possui registro"), ícones, campos `null` em DTO comum, anexos, buscas, exportações comuns, logs, cabeçalhos HTTP e mensagens de erro diferentes. Vale também para SUPER_ADMIN e SUPORTE no painel.

## 4. Salvaguardas contra contornar a regra de sigilo

O ponto fraco de qualquer controle por papel é alguém **se dar** o papel. Defesas:

1. O painel só convida o **primeiro** Diretor (escola sem Diretor ativo). Depois disso não há caminho no painel para conceder papéis na escola.
2. Só um Diretor concede DIRETOR e PEDAGOGO. ADMIN_TECNICO não concede papéis que leem dados sensíveis.
3. Toda concessão de DIRETOR ou PEDAGOGO envia e-mail a **todos** os diretores ativos e fica na auditoria da escola.
4. Contas de plataforma não podem ter papéis de escola (D19); `SUPORTE_PLATAFORMA` nunca tem acesso a `protected`.
5. Reset de MFA pelo SUPER_ADMIN avisa o usuário por e-mail e é auditado nos dois níveis.
6. O Platform.Host não tem permissão de `decrypt` no Vault nem papel de banco que leia `protected`.

## 5. Policies (implementação)

| Policy | Regra |
|--------|-------|
| `PlatformSuperAdmin` / `PlatformSupport` | Papel de plataforma; só registradas no Platform.Host |
| `SchoolMember` | Qualquer papel ativo na escola do host |
| `SchoolManager` | DIRETOR |
| `SchoolConfig` | DIRETOR ou ADMIN_TECNICO |
| `PedagogicalStaff` | DIRETOR ou PEDAGOGO |
| `StudentRead` | PedagogicalStaff, SUPORTE_PLATAFORMA vigente, ou PROFESSOR/CONSULTA com o aluno no escopo das turmas |
| `StudentContactRead` | Conforme P2 |
| `ProtectedRead` / `ProtectedWrite` | PedagogicalStaff + MFA + `auth_time` ≤ 15 min. Negada explicitamente se o usuário tiver só ADMIN_TECNICO/SUPORTE_PLATAFORMA. |
| `ImportCommon` | SchoolConfig |
| `ReadOnly` (filtro) | SUPORTE_PLATAFORMA: qualquer método não-GET → 403 |

Recurso fora do escopo → **404**, com `access_denied` na auditoria quando envolver aluno.

## 6. Testes que validam esta matriz

- `PermissionMatrixTests`: para **cada endpoint**, chama com cada papel + anônimo + **usuário de outra escola** + **sessão de outra escola reapresentada neste host**, e compara com esta tabela.
- `PlatformPanelTests`: SUPER_ADMIN e SUPORTE não conseguem nenhum dado de aluno pelo painel nem chamando a API da escola com o token do painel.
- `ProtectedDataLeakTests`: critérios 8–15 da spec, com strings-sentinela gravadas nos registros protegidos e varredura de JSON, cabeçalhos, logs, cache, tabelas brutas, objetos no SeaweedFS (S3), métricas da plataforma e dumps de backup.
- `TenantIsolationTests`: duas escolas provisionadas; nenhum recurso, sessão, job, anexo ou chave de uma é usável na outra.
