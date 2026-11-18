# Revisão de segurança do MVP — evidências (F6-07)

**Data:** 18/11/2026 · **Versão:** branch `feat/fase-6` · **Escopo:** API da plataforma e da escola, três fronts e infraestrutura de implantação.

Este documento mostra como cada regra que bloqueia a release ([CLAUDE.md](../../CLAUDE.md) e spec §10.3) é garantida e com qual teste automatizado. Itens sem evidência automatizada estão marcados.

## 1. Resultado

| Suíte | Testes | Situação |
|---|---:|---|
| Backend: unitários | 42 | ✅ |
| Backend: arquitetura (dependências entre módulos) | 5 | ✅ |
| Backend: integração (Testcontainers, 3 escolas reais) | 257 | ✅ |
| Fronts (Vitest): escola 28, painel 3, conta 4 | 35 | ✅ |
| E2E (Playwright) contra o ambiente completo | 7 | ✅ |

Nenhum teste de vazamento ou de negação falhou. **Critério de bloqueio da spec §10.3: atendido.**

## 2. Regras bloqueantes e evidência

### 2.1 Sigilo de Saúde e Proteção

| Regra | Como é garantida | Evidência |
|---|---|---|
| Só DIRETOR/PEDAGOGO da escola do aluno | Política `ProtectedAccess` (papel + MFA + autenticação ≤ 15 min) em todas as rotas do módulo; aluno de fora → 404 | `ProtectedRecordsTests` (59): matriz por papel, outra escola, sessão de outra escola, step-up vencido, sem MFA |
| Professor da turma do aluno também não vê | A política ignora o vínculo de turma | `Professor_da_turma_do_aluno_continua_sem_acesso` |
| Nada em DTO comum (nem `null`) | DTOs e rotas separados; módulo isolado (nenhum outro depende dele) | `ProtectedDataLeakTests` (procura chaves `health`, `allerg`, `protection`…) e `Nenhum_modulo_depende_de_ProtectedRecords` |
| Nada em respostas, cabeçalhos, buscas, contagens, listas, indicadores, ocorrências, comunicações | Strings-sentinela gravadas e procuradas em 15 endpoints comuns, para 6 papéis | `Conteudo_protegido_nao_aparece_para_ninguem_alem_de_direcao_e_pedagogia` |
| Nada em tabelas brutas, S3, logs e e-mails | AES-256-GCM por versão (DEK por registro, chave da escola no Vault), anexos cifrados antes do S3, logs e auditoria sem conteúdo | `Bancos_armazenamento_logs_e_emails_nao_tem_conteudo_em_claro`: varre **todas as tabelas** da escola e da plataforma, todos os objetos do S3, todos os eventos de log (Serilog) e todos os e-mails, com controles positivos que provam que a varredura enxerga dados comuns |
| Ciphertext não pode ser movido entre alunos | AAD = escola, aluno, tipo, registro e versão | `Ciphertext_copiado_para_outro_aluno_nao_decifra` |
| Plataforma não decifra | Política do Vault `sinedu-platform` com `deny` em `transit/decrypt/tenant-*` e `transit/datakey/*` | `infra/vault/policies/platform.hcl` (revisão manual) |
| Negativas auditadas sem conteúdo | `protected_access_denied` com usuário, aluno e resultado | `Negativa_e_auditada_sem_conteudo` |
| Comunicação nunca leva dado sensível (critério 15) | Modelos só aceitam 5 marcadores neutros; não há texto livre por envio | `Envio_autorizado_gera_tentativa_sucesso_e_auditoria_com_texto_neutro`, `Modelo_so_aceita_marcadores_neutros_e_so_direcao_edita` e o teste de vazamento (envia e-mail ao lado das sentinelas) |
| Verificação real (fora dos testes) | Dump completo do banco da `escola-demo` e volume do SeaweedFS | Fase 4: 0 ocorrências da sentinela em 1,8 MB de dump; o controle (nome do aluno) aparece 3 vezes |

### 2.2 Isolamento entre escolas

| Regra | Evidência |
|---|---|
| Escola resolvida pelo `Host`; conexão, chave e bucket só dela | `TenantIsolationTests` (6), `HostRoutingTests` (15) |
| Sessão de uma escola não vale em outra | `Sessao_de_uma_escola_nao_vale_em_outra` (API) e E2E `A sessão de uma escola não autentica a API de outra` |
| IDs de outra escola → 404 em aluno, ocorrência, ata, Saúde, Proteção e indicadores | `Direcao_e_pedagogia_de_outra_escola_recebem_404`, `Diretor_de_outra_escola_nao_ve_nem_usa_aluno_de_outra_escola`, `Escola_b_nao_ve_turma_nem_alunos_da_escola_a` |
| Fila de e-mail processa cada escola no próprio escopo | `NotificationRetryWorker` usa o mesmo resolvedor de uma requisição; `Fila_reenvia_automaticamente_quando_chega_a_hora` |

### 2.3 Painel da plataforma

| Regra | Evidência |
|---|---|
| Endpoints do painel só no host `painel.` (host errado → 404 antes da autenticação); API do painel exige login de plataforma; bloqueio após 5 senhas erradas; login não revela se o e-mail existe | `HostRoutingTests`, `PlatformTests` (17) |
| Painel não lê dados de alunos | Arquitetura: `Modulos_da_plataforma_nao_dependem_de_modulos_da_escola`; E2E `Super admin vê as escolas e nenhum dado de aluno`. Os únicos acessos do Platform.Host a bancos de escola são DDL (provisionamento e migrations) |
| Contas da plataforma não recebem papel em escola (D19) | `Conta_da_plataforma_nao_recebe_papel_na_escola` |
| Primeiro SUPER_ADMIN só uma vez | `Primeiro_super_admin_so_pode_ser_criado_uma_vez` |

### 2.4 Autorização, sessões e entrada

| Item | Evidência |
|---|---|
| `deny by default` (política padrão exige papel) e matriz completa por endpoint | `PermissionMatrixTests` (36), `OccurrenceTests` (39), `CommunicationTests` (10), `AnalyticsTests` (27), `StudentScopeTests` (29) |
| CSRF: rota que muda estado com cookie exige mesma origem | `Post_com_cookie_de_outra_origem_e_bloqueado` |
| Sessão revogada perde acesso na hora; papel expirado não vale | `Sessao_revogada_perde_acesso_imediatamente`, `Papel_expirado_nao_da_acesso` |
| TOTP não pode ser reusado (replay) | `TotpService.Verify` recusa passo já usado; exercitado pelo E2E |
| Upload: tipo pelo conteúdo, tamanho máximo, nome saneado | `Anexo_recusa_formatos_nao_permitidos`, `Anexo_de_ocorrencia_so_por_formato_permitido_e_download_pela_api`, `ImportTests` |
| Integridade do armazenamento (SHA-256 no download) | Pegou um defeito real do cliente S3 na Fase 4, corrigido |
| Exportação CSV sem fórmulas (injeção no Excel) | `Csv_neutraliza_formulas_em_nomes_digitados_pela_escola` |
| Supressão estatística (D11) | `Exportacao_so_tem_agregados_com_supressao_de_contagens_pequenas` |
| Erros sem dados pessoais (ProblemDetails; e-mail com falha guarda só o tipo do erro) | `Falha_fica_registrada_sem_dados_e_pode_ser_reenviada` (o erro simulado traz o endereço, e ele não aparece) |
| Cache: `no-store` em rotas protegidas, atas e exportações | `Ficha_de_saude_e_versionada_e_nao_sobrescreve_em_silencio`, `Anexo_e_cifrado_no_armazenamento_e_baixado_so_pela_api` |

## 3. Achados desta revisão

| # | Achado | Severidade | Situação |
|---|---|---|---|
| 1 | Exportação CSV aceitava célula começando com `=`, `+`, `-` ou `@` (nomes de turma e categoria digitados pela escola), o que permitiria fórmula ao abrir no Excel | Média | **Corrigido** e coberto por teste |
| 2 | Atrás de proxy, a API via só o IP do proxy (rate limit por IP inútil, IP errado na auditoria) | Média | **Corrigido**: `X-Forwarded-*` aceitos só da rede do proxy (`Sinedu:TrustedProxyNetwork`) |
| 3 | Chaves OIDC efêmeras em produção (reiniciar invalidava sessões em andamento) | Baixa | **Corrigido**: certificados PFX obrigatórios com cookies seguros; a aplicação não sobe sem eles |
| 4 | Não havia como criar o primeiro administrador fora do `seed-dev` | Operacional | **Corrigido**: `create-super-admin`, de uso único |
| 5 | Aviso de desenvolvimento: os segredos de dev (senha, TOTP, token do Vault) estão no README | Informativo | Aceito: só valem em `*.localhost`; produção exige segredos próprios |

## 4. Pendências que dependem do ambiente real

- [ ] **Homologação na VPS (F6-08):** os arquivos estão prontos (`infra/prod`), mas falta acesso à VPS e ao DNS. Lá, validar: certificado wildcard, `init-roles.sql`, PgBouncer com `auth_query`, Vault com unseal e AppRole, backup e restauração de uma escola.
- [ ] **Teste de restauração de backup** numa escola de homologação, incluindo a leitura de uma ficha de Saúde depois de restaurar.
- [x] **Métricas da plataforma (P-10):** `metrics.v_counts` sem nada protegido; o papel `sinedu_metrics` só lê a view (`Papel_de_metricas_so_le_a_view_de_contagens`); as fotos de coleta ficam no banco da plataforma, coberto pela varredura do teste de vazamento. Painel sem dados de aluno: `Nenhum_dado_de_aluno_e_alcancavel_pelo_painel` (P-14).
- [ ] **Teste de intrusão externo** antes da produção com dados reais.
- [ ] **Licença do QuestPDF** (Community) para uso por órgão público (pendência de decisões).
- [ ] **LGPD:** prazos de retenção e descarte com as redes (pendência de decisões).

## 5. Como reproduzir

```bash
dotnet test apps/api/Sinedu.slnx              # backend (Docker ligado)
npm run lint && npm run typecheck && npm test # fronts
npm run e2e                                   # ambiente local rodando (guia de operação §1)
```
