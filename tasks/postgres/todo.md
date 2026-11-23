# Görevler: SQL Server'dan PostgreSQL'e geçiş

Plan: `tasks/postgres/plan.md` · Spec: `SPEC-postgres.md`.

Odaklı test komutu: `dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~<TestSınıfı>"`. Her görev commit'ten önce `scripts/verify.sh`'den (hook) geçer.

İşçi brifi: önce `incremental-implementation` ve `test-driven-development` skill'lerini oku. Yalnız değiştirdiğin yerin testlerini koş; tam takımı commit hook'u koşar.

## Aşama 0: Hazırlık

- [ ] **Görev 1: Eski veritabanının yedeği** (Ulaş, XS)
  - Kabul:
    - `~/Yedekler/briefyn-db-<tarih>/BriefynDB.bak` var;
    - `RESTORE HEADERONLY` yedeği okuyabiliyor.
  - Komut, Ulaş çalıştırır:
    - `docker exec briefyn-sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -Q "BACKUP DATABASE BriefynDB TO DISK='/var/opt/mssql/BriefynDB.bak'"`
    - ardından `docker cp briefyn-sql:/var/opt/mssql/BriefynDB.bak ~/Yedekler/briefyn-db-<tarih>/`
  - Bağımlılık: yok.

- [ ] **Görev 2: UTC kuralı testle, metin karşılaştıran sorgular listede** (S)
  - Kabul:
    - Bir test, değişen her varlığın `DateTime` alanlarının kayıtta `Kind == Utc` olduğunu SaveChanges üzerinden denetliyor; tam takımda kırılan yer yok ya da düzeltildi.
    - `Services/`, `Pages/`, `Hubs/` ve `Areas/`'ta veritabanına giden metin karşılaştırmaları (`==`, `Contains`, `StartsWith`, `EF.Functions.Like`; değişken ya da kullanıcı girdisiyle) listelenip bu dosyanın sonuna "Görev 2 notu" olarak yazıldı. Her biri için: büyük-küçük harf farkı sonucu değiştirir mi?
    - Değiştirenler normalize alana ya da açık karşılaştırmaya çekildi, testli.
  - Doğrula: odaklı testler; `verify.sh` (hook).
  - Bağımlılık: yok. SQL Server'dayken yapılır, davranışı değiştirmez.
  - Dosyalar: `MeetingWeb.Tests/Security/SchemaTests.cs` (ya da yeni `UtcTests.cs`), bulunursa ilgili servis.

## Aşama 1: Sağlayıcı değişimi (Kontrol A'ya kadar push yok)

- [ ] **Görev 3: EF sağlayıcısı ve şema PostgreSQL'e** (M-L)
  - Kabul:
    - `Microsoft.EntityFrameworkCore.SqlServer` çıktı, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.x girdi; `UseNpgsql`.
    - `ApplicationDbContext`:
      - filtreler ve kontrol kısıtları çift tırnakla, boolean `TRUE` ile yazılıyor;
      - `UseCollation` kalktı;
      - `IsSqlServer()` dalları `IsNpgsql()`.
    - Dört modelde `[Timestamp] public uint RowVersion`. `StampRowVersions` SQLite'ta rastgele `uint` veriyor. `SpeakerNamingService`'in talepten sonra sürümü yeniden okuma yolu duruyor; yorumundaki "SQL Server" PostgreSQL oldu.
    - `MeetingWeb/Migrations/` silindi, `dotnet ef migrations add InitialCreate` ile yeniden üretildi.
    - `Logs` tablosu migration'da PostgreSQL ham SQL'iyle, aynı kolonlar ve `IX_Logs_TimeStamp` ile kuruluyor.
    - `LogCleanupService` ham SQL'i çift tırnaklı.
    - `ZoomTests.cs:771` `UseNpgsql("Host=unused")`.
  - Doğrula: tam takım SQLite'ta geçiyor (hook); `dotnet ef migrations script` çıktısında köşeli parantez yok.
  - Bağımlılık: 1, 2.
  - Dosyalar: `MeetingWeb/MeetingWeb.csproj`, `MeetingWeb/Program.cs` (yalnız `UseNpgsql`), `MeetingWeb/Data/ApplicationDbContext.cs`, 4 model, `MeetingWeb/Migrations/*`, `MeetingWeb/Services/LogCleanupService.cs`, `MeetingWeb/Services/SpeakerNamingService.cs` (yorum), `MeetingWeb.Tests/Features/ZoomTests.cs`.

- [ ] **Görev 4: Hangfire ve log hedefi PostgreSQL'e** (S)
  - Kabul:
    - `Hangfire.SqlServer` ve `Serilog.Sinks.MSSqlServer` çıktı; `Hangfire.PostgreSql` 1.21.x ve `Serilog.Sinks.Postgresql.Alternative` 4.3.x girdi.
    - Web `UsePostgreSqlStorage(... PrepareSchemaIfNecessary = false)`; `migrate` adımı Hangfire şemasını kuruyor.
    - Serilog hedefi `Logs` kolonlarına açıkça eşlenmiş, `needAutoCreateTable = false`, yalnız uyarı ve üstü.
    - "MSSQL" yazıları temizlendi.
    - Hangfire.PostgreSql'in varsayılan şema adı ve istediği yetkiler kaynağından bakılıp buraya not edildi (Görev 5 kullanır).
  - Doğrula: build ve tam takım (hook). Testlerde Hangfire ve SQL log zaten kapalı (`isTesting`).
  - Bağımlılık: 3.
  - Dosyalar: `MeetingWeb/MeetingWeb.csproj`, `MeetingWeb/Program.cs`.

- [ ] **Görev 5: Compose, roller ve `.env.example`** (M)
  - Kabul:
    - `db` resmî `postgres` imajında, sabit ana sürümle (Docker Hub'daki güncel kararlı sürüm, görevde yazılır), `platform` satırı yok. Port `127.0.0.1:5432`, birim `pg_data`.
    - `db-init` (`psql`, idempotent): veritabanı; `briefyn_migrator` (şema sahibi); `briefyn_app`, varsayılan yetkilerle yalnız okur/yazar, Hangfire şeması dahil.
    - `db-readonly` `migrate`'ten sonra çalışıyor; `briefyn_okur`'a kolon düzeyinde SELECT veriyor (AspNetUsers'ın üç kolonu ve `AspNetUserTokens` hariç, liste katalogdan).
    - `migrate` ve `web` bağlantı metinleri Npgsql biçiminde.
    - `.env.example`'da `MSSQL_SA_PASSWORD` yerine `POSTGRES_PASSWORD`.
    - `db/` altında `sqlcmd` kalmadı.
  - Doğrula, elle:
    - eski yığın durdurulur;
    - `docker compose up --build` boş `pg_data` ile açılır;
    - `migrate` 0 ile çıkar;
    - `web` başlar, `/tr` açılır.
  - Bağımlılık: 4.
  - Dosyalar: `docker-compose.yml`, `db/init-app-user.sh`, `db/init-app-user.sql`, `db/init-readonly-user.sql`, `.env.example`.

### Kontrol A
- [ ] `scripts/verify.sh` GEÇTİ.
- [ ] Spec başarı ölçütü 2: boş birimle açılış, öykünme yok.
- [ ] Spec başarı ölçütü 3, elle:
  - kayıt ve giriş;
  - ekip ve davet;
  - yükleme ve analiz;
  - görev;
  - yorum ve tepki;
  - şifre sıfırlama;
  - takvim daveti.
- [ ] Spec başarı ölçütü 4: iki sekmede aynı görev, ikincisine "başkası değiştirdi".
- [ ] Spec başarı ölçütü 6: uyarı logu `Logs`'a düşüyor.
- [ ] Push; CI yeşil.

## Aşama 2: Güvence

- [ ] **Görev 6: PostgreSQL test grubu ve CI servisi** (M)
  - Kabul:
    - `MeetingWeb.Tests/Postgres/`. Bağlantı `BRIEFYN_PG_TEST`'ten gelir; yoksa her test "atlandı" diye görünür, gerekçesiyle.
    - Her test sınıfı boş bir veritabanı açar ve migration'ı uygular.
    - Testler:
      - bayat `xmin` ile kayıt `DbUpdateConcurrencyException`;
      - ExecuteUpdate'ten sonra `xmin` ilerliyor (`SpeakerNamingService` talep yolu);
      - telefon filtreli tekliği (boş telefon birden çok kez olabiliyor);
      - kişisel alan kullanıcı başına bir;
      - bir kontrol kısıtı geçersiz değeri reddediyor;
      - UTC tarih yazılıp aynı okunuyor;
      - `briefyn_app` tablo kuramıyor;
      - `briefyn_okur` `PasswordHash` okuyamıyor.
    - `test.yml`'de `postgres` servisi, rol betiği adımı ve `BRIEFYN_PG_TEST`.
  - Doğrula:
    - yerelde `docker compose up -d db` + değişkenle odaklı koşu geçiyor;
    - değişkensiz koşuda grup "atlandı";
    - CI'da grup koşuyor (atlanmış sayısı 0).
  - Bağımlılık: Kontrol A.
  - Dosyalar: `MeetingWeb.Tests/Postgres/*` (3-4 dosya), `.github/workflows/test.yml`.

### Kontrol B
- [ ] CI yeşil ve PostgreSQL grubu orada koşmuş (günlükte test sayısı).

## Aşama 3: Kapanış

- [ ] **Görev 7: Belgeler** (S)
  - Kabul:
    - ADR-0019 "Veritabanı PostgreSQL" (bağlam: barındırma, ARM; karar; sonuçlar: veri sıfırlandı, yedek yeri);
    - ADR-0006 ve ADR-0009'un SQL Server'a özgü kısımlarına "yerini aldı: ADR-0019";
    - `docs/decisions/README.md` listesi;
    - README diyagramında PostgreSQL;
    - `SPEC-barindirma.md`'de `postgres` modülü bitti;
    - Backlog 9'dan "SQL Server'dan PostgreSQL'e geçiş" çıktı.
  - Doğrula: `grep -riE "sqlserver|mssql|sqlcmd"` yalnız ADR'ler, tarihçe ve bu görev listesinde çıkıyor.
  - Bağımlılık: Kontrol B.
  - Dosyalar: `docs/decisions/0019-*.md`, `docs/decisions/0006-*.md`, `docs/decisions/0009-*.md`, `docs/decisions/README.md`, `README.md`, `SPEC-barindirma.md`, `Backlog.md`.

### Kontrol C
- [ ] Spec'in 6 başarı ölçütü tek tek işaretlendi.
- [ ] Eski `briefynai_sql_data` birimi Ulaş onaylayınca silindi (yedek Görev 1'de).
- [ ] Ulaş'a hatırlatıldı: `/agent-skills:review postgres geçişi`.
