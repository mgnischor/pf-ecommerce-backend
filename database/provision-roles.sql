-- Least-privilege PostgreSQL roles and schemas of the platform (ai/DATABASE.md §3.2, §5).
--
--   app_migrator  owns the schemas and runs the migrations bundle (DDL). Used only by the migration job.
--   app_runtime   what the API connects as: SELECT, INSERT, UPDATE on the tables the migrator creates. No DDL.
--                 No DELETE: business rows are logically deleted (deleted_at); a purge job gets its own grant.
--   app_readonly  reporting: SELECT only.
--
-- Run once per database, and again whenever a bounded context gets its schema, as a role that may create roles
-- (the bootstrap superuser). It is idempotent: re-running it rotates the passwords and repairs missing grants.
-- The passwords are session settings so they never appear in this file or in the shell history:
--
--   PGOPTIONS="-c pf.migrator_password=... -c pf.runtime_password=... -c pf.readonly_password=..." \
--     psql --set ON_ERROR_STOP=1 --dbname ecommerce --file database/provision-roles.sql
--
-- Nothing here touches a schema the platform does not own, and no role gets a superuser or role-creation right.

DO $roles$
DECLARE
    spec record;
    secret text;
BEGIN
    FOR spec IN
        SELECT * FROM (VALUES
            ('app_migrator', 'pf.migrator_password'),
            ('app_runtime',  'pf.runtime_password'),
            ('app_readonly', 'pf.readonly_password')
        ) AS roles(role_name, setting)
    LOOP
        secret := current_setting(spec.setting, true);
        IF secret IS NULL OR length(secret) < 16 THEN
            RAISE EXCEPTION 'Set % to a password of at least 16 characters before running this script.', spec.setting;
        END IF;

        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = spec.role_name) THEN
            EXECUTE format('ALTER ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD %L',
                           spec.role_name, secret);
        ELSE
            EXECUTE format('CREATE ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD %L',
                           spec.role_name, secret);
        END IF;
    END LOOP;

    -- DDL must never wait behind production traffic (ai/DATABASE.md §6.2).
    ALTER ROLE app_migrator SET lock_timeout = '10s';
    ALTER ROLE app_migrator SET statement_timeout = '15min';
    ALTER ROLE app_runtime SET statement_timeout = '30s';
    ALTER ROLE app_runtime SET idle_in_transaction_session_timeout = '60s';
    ALTER ROLE app_readonly SET default_transaction_read_only = on;
    ALTER ROLE app_readonly SET statement_timeout = '60s';
END
$roles$;

DO $schemas$
DECLARE
    schema_name text;
BEGIN
    -- One schema per bounded context that has persistence, named after it (ai/DATABASE.md §5).
    FOREACH schema_name IN ARRAY ARRAY['identity', 'catalog', 'inventory', 'customers']
    LOOP
        EXECUTE format('CREATE SCHEMA IF NOT EXISTS %I AUTHORIZATION app_migrator', schema_name);
        EXECUTE format('ALTER SCHEMA %I OWNER TO app_migrator', schema_name);

        -- The runtime and reporting roles can see the schema but never create objects in it.
        EXECUTE format('REVOKE ALL ON SCHEMA %I FROM PUBLIC', schema_name);
        EXECUTE format('GRANT USAGE ON SCHEMA %I TO app_runtime, app_readonly', schema_name);

        -- Tables the migrator creates from now on...
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA %I GRANT SELECT, INSERT, UPDATE ON TABLES TO app_runtime', schema_name);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA %I GRANT SELECT ON TABLES TO app_readonly', schema_name);

        -- ...and the ones that already exist (a re-run after a migration, or an adopted database).
        EXECUTE format('GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA %I TO app_runtime', schema_name);
        EXECUTE format('GRANT SELECT ON ALL TABLES IN SCHEMA %I TO app_readonly', schema_name);

        -- The migration history is the migrator's business alone.
        IF to_regclass(format('%I.__ef_migrations_history', schema_name)) IS NOT NULL THEN
            EXECUTE format('REVOKE ALL ON %I.__ef_migrations_history FROM app_runtime, app_readonly', schema_name);
        END IF;
    END LOOP;
END
$schemas$;

-- Nobody but the migrator creates objects in the default schema either.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
