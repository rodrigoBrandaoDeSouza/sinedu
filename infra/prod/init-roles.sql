-- Papéis de banco da plataforma em produção (rodar uma vez como administrador, depois do primeiro `migrate`).
-- Senhas via psql: \set ... ou -v. Exemplo:
--   psql -v platform_pw="'...'" -v reader_pw="'...'" -v bouncer_pw="'...'" -f init-roles.sql

-- Dono do banco da plataforma (usado só pelo Platform.Host).
CREATE ROLE sinedu_platform LOGIN PASSWORD :platform_pw;
CREATE DATABASE sinedu_platform OWNER sinedu_platform;

-- Leitor do registro de escolas (usado pelo Tenant.Host): só SELECT na view, nada mais.
CREATE ROLE sinedu_registry_reader LOGIN PASSWORD :reader_pw;
\connect sinedu_platform
GRANT CONNECT ON DATABASE sinedu_platform TO sinedu_registry_reader;
GRANT USAGE ON SCHEMA tenants TO sinedu_registry_reader;
GRANT SELECT ON tenants.v_registry TO sinedu_registry_reader;

-- PgBouncer: consulta de senha (auth_query) dos papéis t_{escola}_app sem guardar senhas em arquivo.
\connect postgres
CREATE ROLE pgbouncer_auth LOGIN PASSWORD :bouncer_pw;
GRANT pg_read_all_settings TO pgbouncer_auth;
CREATE OR REPLACE FUNCTION public.pgbouncer_lookup(p_user text, OUT usename text, OUT passwd text)
RETURNS record LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog AS
$$ SELECT usename::text, passwd::text FROM pg_shadow WHERE usename = p_user AND usename LIKE 't\_%\_app' $$;
REVOKE ALL ON FUNCTION public.pgbouncer_lookup(text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.pgbouncer_lookup(text) TO pgbouncer_auth;
-- No compose, AUTH_QUERY deve ser: SELECT usename, passwd FROM public.pgbouncer_lookup($1)
