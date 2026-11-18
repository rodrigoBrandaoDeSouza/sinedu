# ADR 0002 — Multi-tenancy por subdomínio, banco por escola e painel da plataforma

- **Status:** aceita
- **Data:** 2026-11-18

## Contexto

O Sinedu será operado como plataforma (SaaS) para escolas das redes estadual e municipal. O dono da plataforma precisa de um painel próprio para cadastrar e controlar as escolas, contratos e faturas. Cada escola deve funcionar no próprio subdomínio. A spec exige isolamento rigoroso entre escolas e proíbe que administradores contornem a autorização para ler dados sensíveis.

## Decisão

1. **Tenant = escola**, acessada por `{slug}.sinedu.com.br`.
2. **Um banco PostgreSQL por escola** (`sinedu_t_{slug}`), com **papel de banco próprio** e **chave de criptografia própria** no Vault (`transit/keys/tenant-{id}`).
3. **Plano de controle separado** (banco `sinedu_platform`) com o registro de escolas, redes, identidade, planos, contratos e faturas.
4. **Dois executáveis** (continua sendo monólito modular, não microsserviços):
   - `Sinedu.Platform.Host` → `conta.sinedu.com.br` (login único, OIDC) e `painel.sinedu.com.br` (painel do dono).
   - `Sinedu.Tenant.Host` → `*.sinedu.com.br` (aplicação da escola). Resolve a escola pelo `Host` e abre a conexão com o banco dela.
5. **Login único**: `conta.` é um provedor OpenID Connect (OpenIddict). Cada subdomínio de escola autentica via Authorization Code + PKCE e recebe um cookie de sessão **próprio do host** (`__Host-`). Papéis ficam no banco da escola.
6. **Suporte** da plataforma só entra numa escola com liberação temporária do Diretor, somente leitura de dados comuns, auditado.

## Alternativas consideradas

- **Tenant = rede** (com várias escolas dentro) — melhor para contas multiescola, mas o produto quer uma identidade e um endereço por escola.
- **Banco único com `tenant_id`** — operação mais simples, mas o isolamento depende de filtros na aplicação; um bug vaza dados entre escolas.
- **Schema por escola** — meio-termo; não oferece backup, restauração e exclusão por cliente tão limpos quanto o banco separado.
- **Cookie compartilhado em `.sinedu.com.br`** em vez de OIDC — mais simples, mas a sessão de uma escola valeria em todas, e o Android futuro precisaria de outro mecanismo.

## Consequências

- Vazamento entre escolas passa a exigir erro de **conexão**, não de filtro. O teste de isolamento continua obrigatório (subdomínio A com sessão de B).
- Exclusão de uma escola pode ser feita por **crypto-shredding** (destruir a chave da escola no Vault) além de remover o banco.
- Migrations rodam em todos os bancos de escola por um executor controlado pelo painel. A aplicação recusa uma escola cujo banco esteja em versão diferente da esperada.
- Até cerca de 50 escolas, um servidor PostgreSQL com PgBouncer basta. Acima de algumas centenas, rever (D18).
- Rotas da API da escola perdem o prefixo `/schools/{schoolId}`: a escola é o host.
- Indicadores entre escolas (visão de rede) exigem agregação pelo plano de controle — fora do MVP.
