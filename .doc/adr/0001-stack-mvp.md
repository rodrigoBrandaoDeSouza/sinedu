# ADR 0001 — Stack do MVP

- **Status:** aceita
- **Data:** 2026-11-18

## Contexto

A especificação do MVP (seção 12) sugere React + Vite e ASP.NET Core. O esboço inicial do repositório propunha Next.js + NestJS e um app Android no MVP. A spec exclui app nativo nesta fase e pede monólito modular, sem microsserviços.

## Decisão

- **Front-end:** React + TypeScript + Vite (SPA).
- **Back-end:** ASP.NET Core Web API, .NET 10 LTS, monólito modular (`Identity`, `Schools`, `Students`, `ProtectedRecords`, `Occurrences`, `Imports`, `Notifications`, `Analytics`).
- **Banco:** PostgreSQL com migrations EF Core.
- **Infra:** Docker Compose (dev) e VPS própria (prod), HashiCorp Vault para chaves, SeaweedFS (S3) para anexos.
- **Android:** fora do MVP; API projetada para recebê-lo depois.

## Alternativas consideradas

- **Next.js + NestJS** — TypeScript ponta a ponta; porém SSR traz pouco ganho em sistema interno autenticado e complica controle de cache de páginas sensíveis.
- **React + NestJS** — tipos compartilhados; diverge da spec sem ganho claro de segurança.

## Consequências

- Policies de autorização, Data Protection e Identity nativos do ASP.NET Core aceleram a Fase 1 (segurança).
- Contratos front/back via OpenAPI com geração de cliente TypeScript.
- Duas linguagens no repositório (C# e TypeScript).
