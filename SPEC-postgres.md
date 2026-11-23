# Spec: SQL Server'dan PostgreSQL'e geçiş (`postgres` modülü)

Durum: onaylandı (Ulaş, 2026-11-23) · harita `SPEC-barindirma.md` · Backlog 9

## Amaç

Uygulama SQL Server yerine PostgreSQL ile çalışır. Neden:
- ücretsiz ve ucuz sunucularda PostgreSQL var, SQL Server yok ya da pahalı;
- PostgreSQL ARM'de doğal çalışıyor. SQL Server bugün bu Mac'te bile amd64 öykünmesiyle çalışıyor (`docker-compose.yml`, `platform: linux/amd64`).

Kullanıcı açısından hiçbir şey değişmez: bütün akışlar aynı çalışır, yalnız veritabanı değişir. Veri sıfırdan başlar (Ulaş, 2026-11-22); eski veritabanının yedeği alınır.

Başlama şartı karşılandı: Python servisini kaldırma bitti (`4618953`, 2026-11-22; CI yeşil).

## Değişecekler

Koddan bakıldı (2026-11-22):

1. **Paketler** (Ulaş onayladı, 2026-11-23; hepsi ücretsiz):
   - `Microsoft.EntityFrameworkCore.SqlServer` yerine `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.x. EF 10 kullanıyoruz; en son 10.0.4, 11.0 EF 11 içindir;
   - `Hangfire.SqlServer` yerine `Hangfire.PostgreSql` 1.21.x;
   - `Serilog.Sinks.MSSqlServer` yerine `Serilog.Sinks.Postgresql.Alternative` 4.3.x (serilog-contrib). Tabloyu kendisi kurmaz (varsayılan `needAutoCreateTable = false`); tablo bizim migration'ımızda kalır.
2. **`Program.cs`:**
   - `UseSqlServer` yerine `UseNpgsql`;
   - Hangfire `UsePostgreSqlStorage` ile kurulur. Web'de `PrepareSchemaIfNecessary = false`, `migrate` adımında `true` olur; bugünkü düzen korunur;
   - veritabanı log hedefi değişir ve "MSSQL" yazıları temizlenir.
3. **`ApplicationDbContext`:**
   - filtreli index'ler ve kontrol kısıtları köşeli parantez yerine çift tırnakla yazılır. Boolean filtre `"IsPersonal" = TRUE` olur; bu yazım hem PostgreSQL'de hem testlerdeki SQLite'ta çalışır;
   - `ExternalRecordingId`'deki `Latin1_General_100_BIN2` kalkar. PostgreSQL'in varsayılan karşılaştırması zaten büyük-küçük harf ayırıyor;
   - `IsSqlServer()` dalları `IsNpgsql()` olur.
4. **Eşzamanlılık alanı (`RowVersion`)**, `Meeting`, `ActionItem`, `ZoomConnection` ve `CalendarInvite` modellerinde:
   - `byte[]` yerine PostgreSQL'in `xmin` kolonuna bağlı `uint` olur;
   - Npgsql belgesine göre `[Timestamp] uint` yeterli ve satır her değiştiğinde güncelleniyor;
   - SQLite'taki testlerde `StampRowVersions` bugünkü gibi her kayıtta yeni değer verir;
   - `SpeakerNamingService`, ExecuteUpdate'ten sonra sürümün ilerlediğine güveniyor. PostgreSQL'de de ilerlediği bir testle gösterilir; belge bunu ExecuteUpdate için ayrıca yazmıyor.
5. **Tarih alanları** (19 `DateTime` alanı):
   - tahminim: Npgsql, `timestamp with time zone` kolonuna yazılan `DateTime`'ın UTC olmasını istiyor, değilse hata veriyor. Belgesine bu oturumda bakılmadı; plan adımında doğrulanır;
   - alanların çoğu `...Utc` adlı. Yine de UTC olmayan değer yazan yer kalmışsa bulunup düzeltilir.
6. **Büyük-küçük harf:**
   - SQL Server'ın varsayılan karşılaştırması büyük-küçük harf ayırmıyordu, PostgreSQL ayırıyor;
   - veritabanında metin karşılaştıran sorgular gözden geçirilir. Identity'nin `Normalized*` alanları bundan etkilenmez;
   - ilk taramada metin aramaları bellekte, `OrdinalIgnoreCase` ile yapılıyor (`History`, `Details`, `Series/Show`). Plan adımında tam liste çıkarılır.
7. **Migration'lar:** 14 migration ve snapshot silinir, PostgreSQL için tek `InitialCreate` üretilir. `Logs` tablosu ham SQL yerine PostgreSQL yazımıyla, aynı kolonlar ve `TimeStamp` index'iyle kurulur.
8. **`LogCleanupService`:** ham SQL'deki köşeli parantezler çift tırnak olur.
9. **Compose ve `db/` betikleri:**
   - `db`, `db-init` ve `db-readonly` PostgreSQL imajına geçer (ARM için de çalışan resmî `postgres` imajı, sabit ana sürüm);
   - roller bugünkü yetki düzenini korur:
     - `briefyn_app` yalnız okur ve yazar;
     - `briefyn_migrator` şemayı kurar, Hangfire'ın şeması dahil;
     - salt okunur `briefyn_okur` için `AspNetUsers`'ın parola, güvenlik damgası ve eşzamanlılık kolonları ile `AspNetUserTokens` kapalı kalır;
   - yeni tabloların yetkisi için `ALTER DEFAULT PRIVILEGES` kullanılır; Hangfire'ın kendi şemasına da uygulanır. Hangfire'ın hangi şemayı kullandığı ve hangi yetkiyi istediği README'de yazmıyor; kaynağından bakılır;
   - port yalnız `127.0.0.1`'de açık kalır. Birim adı değişir, eski SQL Server birimi yedeklendikten sonra kaldırılır.
10. **Eski veri:** geçişten önce `BriefynDB`'nin yedeği `~/Yedekler/briefyn-db-<tarih>/` altına alınır, ADR-0009'daki gibi.
11. **Belgeler:**
    - yeni ADR (ADR-0019: veritabanı PostgreSQL);
    - ADR-0006 ve ADR-0009'un SQL Server'a özgü kısımlarına "yerini aldı" notu;
    - README diyagramı ve `.env.example` güncellenir (`MSSQL_SA_PASSWORD` yerine PostgreSQL yönetici parolası).

## Komutlar

```
Doğrulama:  scripts/verify.sh
Yığın:      docker compose up --build
Migration:  dotnet ef migrations add InitialCreate --project MeetingWeb
Şema:       docker compose run --rm migrate
```

## Proje yapısı

Yeni klasör yok. Değişen yerler: `MeetingWeb/Program.cs`, `MeetingWeb/Data/ApplicationDbContext.cs`, `MeetingWeb/Models/` (4 model), `MeetingWeb/Migrations/` (baştan), `MeetingWeb/Services/LogCleanupService.cs`, `db/`, `docker-compose.yml`, `.env.example`, `docs/decisions/`, testlerden `BriefynFactory.cs` ve `ZoomTests.cs:771`.

## Kod biçimi

Bugünkü düzen sürer:
- yorumlar neden'i anlatır;
- kararlar ADR numarasıyla anılır;
- ham SQL'de tablo ve kolon adları çift tırnaklı. Testler zaten böyle yazıyor, örnek `SchemaTests.cs:131`:

```csharp
db.Database.ExecuteSqlRaw("DELETE FROM \"Workspaces\" WHERE \"Id\" = {0}", teamId)
```

## Test stratejisi

- Test takımı bugünkü gibi SQLite'ta kalır; hızlı ve dış bağımlılığı yok. Commit öncesi hook tam takımı koşar.
- SQLite PostgreSQL'e özgü şeyleri denemez: `xmin`, rol yetkileri, filtreli index'in gerçek yazımı, UTC kuralı. Bunlar için küçük ayrı bir PostgreSQL test grubu yazılır (Ulaş, 2026-11-23):
  - testler gerçek PostgreSQL'e, migration'la kurulmuş şemaya bağlanır; bağlantı bir ortam değişkeninden gelir;
  - CI'da `test.yml`'e PostgreSQL servis kapsayıcısı eklenir ve değişken orada verilir; bu grup CI'da her zaman koşar;
  - yerelde değişken yoksa grup "atlandı" diye görünür, sessizce geçmiş sayılmaz; `docker compose up db` ile yerelde de koşturulabilir;
  - yeni paket eklenmez (Testcontainers yok).
- Yeni davranış testleri:
  - `SpeakerNamingService` talebinden sonra sürüm ilerliyor;
  - bayat sürümle görev güncellemesi kullanıcıya "başkası değiştirdi" der (bugünkü testler SQLite'ta aynen geçmeli).

## Sınırlar

- Her zaman: `scripts/verify.sh` geçmeden commit yok; eski veritabanı silinmeden önce yedeklenir; uygulama kullanıcısı şema değiştiremez (ADR-0009 sürer).
- Önce sor: yukarıdaki üç paket dışında paket eklemek; Hangfire ya da log tablosunu uygulamanın kendisine kurdurmak; CI'da PostgreSQL servisinden başka değişiklik.
- Asla: `sa` benzeri bir süper kullanıcıyla uygulama bağlantısı; veritabanı portunu dış ağa açmak; testleri silmek ya da atlamak.

## Başarı ölçütleri

1. `scripts/verify.sh` geçiyor. Kodda, compose'da ve `db/` altında SQL Server'a ait paket, `UseSqlServer`, `sqlcmd` ya da `mssql` kalmamış (`grep -ri "sqlserver\|mssql\|sqlcmd"` yalnız ADR ve tarihçe gösterir).
2. Boş birimle `docker compose up --build` açılıyor:
   - `migrate` şemayı ve Hangfire tablolarını kurup çıkıyor;
   - `web` ARM Mac'te öykünmesiz başlıyor (compose'da `platform: linux/amd64` yok).
3. Elle denenir:
   - kayıt olma ve giriş;
   - ekip kurma ve davet;
   - toplantı yükleme, analizin Hangfire'da bitmesi;
   - görev atama ve tamamlama;
   - yorum ve tepki;
   - şifre sıfırlama e-postası;
   - takvim daveti.
4. Aynı görevi iki sekmede değiştirince ikincisi "başkası değiştirdi" uyarısı alıyor (`xmin` çalışıyor).
5. `briefyn_app` ile tablo kurma denemesi yetki hatası veriyor. `briefyn_okur` ile `AspNetUsers.PasswordHash` okuma denemesi yetki hatası veriyor.
6. Uyarı seviyesindeki bir log `Logs` tablosuna düşüyor; `LogCleanupService` 30 günden eskiyi siliyor.

## Cevaplanan sorular (Ulaş, 2026-11-23)

1. CI'a PostgreSQL servisiyle küçük bir test grubu eklenir (Test stratejisi).
2. Uyarı ve hata günlükleri veritabanında 30 gün kalır; `Serilog.Sinks.Postgresql.Alternative` eklenir. Üç paket onaylandı.
