-- Şemaya ve veriye bakmak için salt okunur giriş (2026-10-18).
-- briefyn_okur yalnız Briefyn veritabanını okuyabilir; yazamaz, şema değiştiremez, sunucu yetkisi yoktur.
-- Hassas içerik zaten v2 ile şifreli (ADR-0003); parola özetleri ve oturum jetonları ayrıca kapalı.
-- Idempotent: her açılışta çalışır, parolayı .env'deki READONLY_DB_PASSWORD ile eşitler.
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(RO_DB_USER)')
    CREATE LOGIN [$(RO_DB_USER)] WITH PASSWORD = N'$(RO_DB_PASSWORD)', DEFAULT_DATABASE = [$(DB_NAME)], CHECK_POLICY = ON;
ELSE
    ALTER LOGIN [$(RO_DB_USER)] WITH PASSWORD = N'$(RO_DB_PASSWORD)';
GO
USE [$(DB_NAME)];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(RO_DB_USER)')
    CREATE USER [$(RO_DB_USER)] FOR LOGIN [$(RO_DB_USER)];
ELSE
    ALTER USER [$(RO_DB_USER)] WITH LOGIN = [$(RO_DB_USER)];
ALTER ROLE db_datareader ADD MEMBER [$(RO_DB_USER)];
IF OBJECT_ID(N'dbo.AspNetUsers') IS NOT NULL
    DENY SELECT ON dbo.AspNetUsers (PasswordHash, SecurityStamp, ConcurrencyStamp) TO [$(RO_DB_USER)];
IF OBJECT_ID(N'dbo.AspNetUserTokens') IS NOT NULL
    DENY SELECT ON dbo.AspNetUserTokens TO [$(RO_DB_USER)];
GO
