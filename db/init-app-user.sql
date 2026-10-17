-- S9a: the web app connects with its own login instead of "sa".
-- briefyn_app can read, write and change the schema (EF migrations, Hangfire, log table) of the Briefyn
-- database only; it has no server-wide rights. Idempotent: safe to run on every start; it also keeps the
-- password in sync with APP_DB_PASSWORD in .env.
IF DB_ID(N'$(DB_NAME)') IS NULL
    CREATE DATABASE [$(DB_NAME)];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(APP_DB_USER)')
    CREATE LOGIN [$(APP_DB_USER)] WITH PASSWORD = N'$(APP_DB_PASSWORD)', DEFAULT_DATABASE = [$(DB_NAME)], CHECK_POLICY = ON;
ELSE
    ALTER LOGIN [$(APP_DB_USER)] WITH PASSWORD = N'$(APP_DB_PASSWORD)';
GO
USE [$(DB_NAME)];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(APP_DB_USER)')
    CREATE USER [$(APP_DB_USER)] FOR LOGIN [$(APP_DB_USER)];
ELSE
    ALTER USER [$(APP_DB_USER)] WITH LOGIN = [$(APP_DB_USER)]; -- re-links the user after a database restore
ALTER ROLE db_datareader ADD MEMBER [$(APP_DB_USER)];
ALTER ROLE db_datawriter ADD MEMBER [$(APP_DB_USER)];
ALTER ROLE db_ddladmin ADD MEMBER [$(APP_DB_USER)];
GO
