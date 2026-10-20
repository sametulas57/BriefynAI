-- S9a / ADR-0009: the web app connects with its own login instead of "sa".
-- briefyn_app can only read and write the Briefyn database; the schema (EF migrations, Hangfire, log table) is set up
-- by the one-shot "migrate" service with briefyn_migrator, which may change the schema of this database only.
-- Neither has server-wide rights. Idempotent: safe to run on every start; it also keeps the passwords in sync with .env.
IF DB_ID(N'$(DB_NAME)') IS NULL
    CREATE DATABASE [$(DB_NAME)];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(APP_DB_USER)')
    CREATE LOGIN [$(APP_DB_USER)] WITH PASSWORD = N'$(APP_DB_PASSWORD)', DEFAULT_DATABASE = [$(DB_NAME)], CHECK_POLICY = ON;
ELSE
    ALTER LOGIN [$(APP_DB_USER)] WITH PASSWORD = N'$(APP_DB_PASSWORD)';
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(MIGRATOR_DB_USER)')
    CREATE LOGIN [$(MIGRATOR_DB_USER)] WITH PASSWORD = N'$(MIGRATOR_DB_PASSWORD)', DEFAULT_DATABASE = [$(DB_NAME)], CHECK_POLICY = ON;
ELSE
    ALTER LOGIN [$(MIGRATOR_DB_USER)] WITH PASSWORD = N'$(MIGRATOR_DB_PASSWORD)';
GO
USE [$(DB_NAME)];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(APP_DB_USER)')
    CREATE USER [$(APP_DB_USER)] FOR LOGIN [$(APP_DB_USER)];
ELSE
    ALTER USER [$(APP_DB_USER)] WITH LOGIN = [$(APP_DB_USER)]; -- re-links the user after a database restore
ALTER ROLE db_datareader ADD MEMBER [$(APP_DB_USER)];
ALTER ROLE db_datawriter ADD MEMBER [$(APP_DB_USER)];
IF IS_ROLEMEMBER(N'db_ddladmin', N'$(APP_DB_USER)') = 1
    ALTER ROLE db_ddladmin DROP MEMBER [$(APP_DB_USER)];

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(MIGRATOR_DB_USER)')
    CREATE USER [$(MIGRATOR_DB_USER)] FOR LOGIN [$(MIGRATOR_DB_USER)];
ELSE
    ALTER USER [$(MIGRATOR_DB_USER)] WITH LOGIN = [$(MIGRATOR_DB_USER)];
ALTER ROLE db_datareader ADD MEMBER [$(MIGRATOR_DB_USER)];
ALTER ROLE db_datawriter ADD MEMBER [$(MIGRATOR_DB_USER)];
ALTER ROLE db_ddladmin ADD MEMBER [$(MIGRATOR_DB_USER)];
GO
