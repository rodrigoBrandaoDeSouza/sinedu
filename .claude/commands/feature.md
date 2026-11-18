---
description: Planeja uma funcionalidade ponta a ponta (API, web e Android)
argument-hint: <descrição da funcionalidade>
---

Planeje a funcionalidade: **$ARGUMENTS**

Antes de escrever código:

1. Leia `.doc/produto/` e verifique regras de negócio relacionadas.
2. Defina o contrato da API (endpoints, payloads, erros) e se ele é compatível com versões antigas do app Android.
3. Liste as mudanças por camada:
   - **API** — modelo, migration, regras, testes.
   - **Web** — telas, estados (carregando, vazio, erro), validações.
   - **Android** — telas Compose, ViewModel, cache offline, strings.
4. Aponte riscos e dúvidas em aberto.

Apresente o plano e aguarde aprovação antes de implementar. Ao final da implementação, atualize `.doc/` se o comportamento documentado mudou.
