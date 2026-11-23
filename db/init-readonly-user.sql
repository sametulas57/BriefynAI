-- ADR-0009: read-only access excludes credentials and session tokens.
SELECT format('CREATE ROLE briefyn_okur LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE', :'readonly_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'briefyn_okur')
\gexec
SELECT format('ALTER ROLE briefyn_okur LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE', :'readonly_password')
\gexec
GRANT CONNECT ON DATABASE briefyn TO briefyn_okur;
GRANT USAGE ON SCHEMA public, hangfire TO briefyn_okur;

-- Remove table grants before applying the current allowlist on every start.
SELECT format('REVOKE ALL ON TABLE %I.%I FROM briefyn_okur', schemaname, tablename)
FROM pg_tables
WHERE schemaname IN ('public', 'hangfire')
ORDER BY schemaname, tablename
\gexec

-- Column grants survive table-level REVOKE; clear them before rebuilding the list.
SELECT format('REVOKE ALL (%s) ON TABLE %I.%I FROM briefyn_okur',
              string_agg(format('%I', column_name), ', ' ORDER BY ordinal_position),
              table_schema, table_name)
FROM information_schema.columns
WHERE table_schema IN ('public', 'hangfire')
GROUP BY table_schema, table_name
ORDER BY table_schema, table_name
\gexec

SELECT CASE WHEN t.tablename = 'AspNetUsers' THEN
         format('GRANT SELECT (%s) ON TABLE %I.%I TO briefyn_okur',
                (SELECT string_agg(format('%I', c.column_name), ', ' ORDER BY c.ordinal_position)
                 FROM information_schema.columns c
                 WHERE c.table_schema = t.schemaname AND c.table_name = t.tablename
                   AND c.column_name NOT IN ('PasswordHash', 'SecurityStamp', 'ConcurrencyStamp')),
                t.schemaname, t.tablename)
       ELSE format('GRANT SELECT ON TABLE %I.%I TO briefyn_okur', t.schemaname, t.tablename)
       END
FROM pg_tables t
WHERE t.schemaname IN ('public', 'hangfire') AND t.tablename <> 'AspNetUserTokens'
ORDER BY t.schemaname, t.tablename
\gexec
