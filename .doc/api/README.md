# API

Duas APIs, cada uma no próprio host:

| API | Host | Prefixo |
|-----|------|---------|
| Escola | `{escola}.sinedu.com.br` | `/api/...` — sem `schoolId`, a escola é o host |
| Plataforma | `painel.sinedu.com.br` | `/api/...` — escolas, redes, contratos, faturas, métricas, suporte |

`conta.sinedu.com.br` expõe só os endpoints OIDC padrão (`/connect/authorize`, `/connect/token`, `/.well-known/openid-configuration`) e as telas de login.

- Contratos oficiais: `openapi-tenant.yaml` e `openapi-platform.yaml` (nesta pasta, quando criados).
- Toda alteração de endpoint começa pelo contrato.
- Mudanças incompatíveis exigem nova versão (`/v2`); o app Android (pós-MVP) terá versões antigas em uso.

## Mapeamento das rotas ilustrativas da spec (seção 13)

| Spec | Sinedu (no host da escola) |
|------|---------------------------|
| `GET /api/schools` | Lista de escolas da conta, no `conta.` (escolha de escola) |
| `GET /api/schools/{schoolId}/classrooms?year=` | `GET /api/classrooms?year=` |
| `GET /api/schools/{schoolId}/students/{studentId}` | `GET /api/students/{studentId}` |
| `GET/PUT .../students/{studentId}/health` | `GET/PUT /api/students/{studentId}/health` |
| `GET/PUT .../students/{studentId}/protection` | `GET/PUT /api/students/{studentId}/protection` |
| demais | mesmo caminho, sem `/schools/{schoolId}` |

## Padrões

- JSON em `camelCase`.
- Datas em ISO 8601 UTC.
- Paginação: `?page=1&pageSize=20` → `{ "items": [], "total": 0 }`.
- Erros: `ProblemDetails` (RFC 9457) com `code` em extensão; nunca dados pessoais na mensagem.
- Recurso fora do escopo do usuário → `404`.
