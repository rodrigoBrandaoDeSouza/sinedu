# Amostras de importação (anonimizadas)

Planilhas no formato real das escolas, com **todos os dados pessoais substituídos por dados fictícios**. Servem para desenvolver e testar a importação (Fase 3) e para popular escolas de teste.

> Arquivos reais recebidos das escolas ficam em `.doc/files/`, que está no `.gitignore` e nunca vai para o repositório.

## `alunos-2026-anonimizado.xlsx`

Origem: relatório "Documentos dos alunos 2026" de uma escola estadual (1.032 alunos, 34 turmas), gerado a partir do controle de documentos da secretaria. Anonimizado em 18/11/2026 com [tools/anonimizacao](../../tools/anonimizacao/).

### Aba `Original` — cadastro e controle de documentos

| Col. | Cabeçalho | Conteúdo na amostra |
|------|-----------|---------------------|
| 1 | `1704` | Nº de chamada na turma (mantido) |
| 2 | *(vazio)* | **CGM fictício** (10 dígitos), consistente com as abas de frequência |
| 3 | Nome do Aluno | Nome fictício |
| 4 | TURMA - 2026 | Mantida — formato `6º A Manhã`, `1ª D Noite` (série + turma + turno) |
| 5 | Data nascimento | Deslocada aleatoriamente (±90 dias, mesmo ano) |
| 6 | E-mail Institucional | Fictício em `@aluno.exemplo.edu.br` |
| 7 | Endereço | Rua, número e bairro fictícios |
| 8 | Responsável | Nome fictício |
| 9 | CPF / RG Responsável | CPF com dígitos verificadores **inválidos** (nunca coincide com um real) |
| 10–22 | Checklists de documentos | Só valores de lista fechada (`OK`, `FALTA`, `NÃO AUTORIZADO`, `CÓPIA`…); o que fugia disso virou `VERIFICAR` |
| 11 | RESPONS. P/ MATRICULA ALUNO | Vínculo (`MÃE`, `PAI`, `AVÓS`, `OUTROS`…) |
| 20 | FICHA DE SAÚDE | Só indica entrega (`OK`); **nenhum conteúdo de saúde** |
| 21 | TRANS. (HE FUND.) | Escola de origem substituída por `OUTRA ESCOLA` |
| 23 | FREQUÊNCIA | Valor numérico com pequena variação aleatória |
| 24 | SITUAÇÃO MATRÍCULA | Só a sigla (`SF`, `SFD`); detalhes removidos |
| 25 | TEL. DO RESP. | `(00) 9xxxx-xxxx` |
| 26 | OBSERVAÇÕES | **Removido** (texto livre) |

### Abas `frequência BI <data>` — presença por período

`ALUNO` (fictício, mesmo nome do cadastro para o mesmo CGM) · `CGM` (fictício) · `TURMA` (formato `9º Série A Manhã - 9000001`, com código de turma fictício) · `Média Geral de Presença` (fração 0–1).

### Particularidades do formato real (úteis para o mapeamento da importação)

- O cabeçalho do CGM está **vazio** e a coluna 1 tem um número solto como cabeçalho.
- A turma tem formatos diferentes entre abas: `1ª A Manhã` (cadastro) e `1º Série A Manhã - <código>` (frequência).
- Há colunas extras sem cabeçalho no fim e uma aba vazia (`Página1`) no arquivo original.
- Alguns alunos das abas de frequência não estão na aba de cadastro (transferidos/remanejados).
- Datas vêm como data do Excel; frequência às vezes como texto.

### Garantias da anonimização

- O mapeamento real → fictício existe só em memória durante a execução e **não é salvo**.
- Nenhum valor fictício gerado pode conter um valor real (é regerado se coincidir).
- Ao final, o script confere **todas** as células geradas contra os 6.771 valores pessoais da planilha original. Resultado: **0 ocorrências**.
