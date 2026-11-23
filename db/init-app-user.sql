-- ADR-0009: the migrator owns the schema; the web role only reads and writes data.
SELECT format('CREATE ROLE briefyn_migrator LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE', :'migrator_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'briefyn_migrator')
\gexec
SELECT format('ALTER ROLE briefyn_migrator LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE', :'migrator_password')
\gexec
SELECT format('CREATE ROLE briefyn_app LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE', :'app_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'briefyn_app')
\gexec
SELECT format('ALTER ROLE briefyn_app LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE', :'app_password')
\gexec

SELECT 'CREATE DATABASE briefyn OWNER briefyn_migrator'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'briefyn')
\gexec
ALTER DATABASE briefyn OWNER TO briefyn_migrator;

\connect briefyn
REVOKE ALL ON DATABASE briefyn FROM PUBLIC;
GRANT CONNECT ON DATABASE briefyn TO briefyn_app;
ALTER SCHEMA public OWNER TO briefyn_migrator;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO briefyn_app;

-- New migrator objects include the Hangfire schema installed later by migrate.
ALTER DEFAULT PRIVILEGES FOR ROLE briefyn_migrator GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO briefyn_app;
ALTER DEFAULT PRIVILEGES FOR ROLE briefyn_migrator GRANT USAGE, SELECT ON SEQUENCES TO briefyn_app;
ALTER DEFAULT PRIVILEGES FOR ROLE briefyn_migrator GRANT USAGE ON SCHEMAS TO briefyn_app;

-- Reapply data privileges when restarting with an existing database.
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO briefyn_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO briefyn_app;
SELECT 'GRANT USAGE ON SCHEMA hangfire TO briefyn_app;
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA hangfire TO briefyn_app;
        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA hangfire TO briefyn_app'
WHERE EXISTS (SELECT FROM pg_namespace WHERE nspname = 'hangfire')
\gexec
