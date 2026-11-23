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

- [x] **Görev 2: UTC kuralı testle, metin karşılaştıran sorgular listede** (S)
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

## Görev 2 notu

`MeetingWeb/Data/ApplicationDbContext.cs` içinde yalnız mevcut SQL Server dışı dalda bütün `DateTime` ve `DateTime?` alanlarına yazarken değeri değiştirmeyen, okurken `Kind` değerini UTC yapan dönüştürücü eklendi. SQL Server dalı ve RowVersion davranışı değiştirilmedi. `MeetingWeb.Tests/Infrastructure/UtcDateTimeGuard.cs` eklendi ve `MeetingWeb.Tests/Infrastructure/BriefynFactory.cs` içinde kaydedildi. Koruma hem senkron hem asenkron kayıtta yeni varlığın bütün tarihlerini, güncellenen varlığın yalnız değişmiş tarihlerini denetliyor. `MeetingWeb.Tests/Security/UtcDateTimeTests.cs` yerel ve türü belirtilmemiş tarihlerin reddini, nullable tarih güncellemesini, UTC tarihlerin ayrı bağlamdan UTC okunmasını ve değişmeyen tarihlerin denetlenmemesini sınar. Diğer değişen dosyalar `MeetingWeb/Services/CalendarSyncService.cs`, `MeetingWeb.Tests/Features/CalendarSyncTests.cs` ve bu görev notudur.

UTC olmayan veritabanı yazıcıları: `MeetingWeb/Services/CalendarSyncService.cs:268` gönderim hatasında `UpdatedAtUtc` alanına türü belirtilmemiş `DateTime.MinValue` yazıyordu; aynı tarih UTC olarak işaretlendi. `MeetingWeb.Tests/Features/CalendarSyncTests.cs:1268` başarısız iptal senaryosunda aynı değeri kaydediyordu; test verisi UTC olarak işaretlendi. `MeetingWeb.Tests/Features/CalendarSyncTests.cs:359` asıl posta hatasının koruma hatasıyla örtülmediğini de doğruluyor. Yeniden deneme ve sıra numarası davranışı odaklı takvim testlerinde korunuyor. Başka UTC olmayan model tarihi yazıcısı bulunmadı; SeedData modelin UTC varsayılanlarını, GoldSet açıkça UTC dönüşümünü kullanıyor.

Taramada görülen ama veritabanına yazmayan yerler de incelendi. `MeetingWeb/Services/EmailService.cs:349` içindeki `DateTime.Now.Year` yalnız e-posta metninin yılıdır. `MeetingWeb/Services/ZoomApiClient.cs:251` içindeki varsayılan tarih yalnız kayıt bilgisinde gösterilir; toplantının oluşturulma tarihine yazılmaz. `MeetingWeb/Services/SeriesAgendaService.cs:37` içindeki en küçük tarih bellekte sıralama içindir. `MeetingWeb/Services/ZoomWebhookHandler.cs:104` ayrıştırırken zaten `AdjustToUniversal | AssumeUniversal` kullanır. `MeetingWeb.Tests/Features/UserFacingTests.cs:30` ve `MeetingWeb.Tests/Features/CalendarInviteBuilderTests.cs:220` UTC olmayan değerlerle yalnız biçimlendirmeyi sınar, veritabanına yazmaz. Yeni UTC testindeki `MeetingWeb.Tests/Security/UtcDateTimeTests.cs:31` ve `MeetingWeb.Tests/Security/UtcDateTimeTests.cs:77` kasıtlı reddetme örnekleridir; `MeetingWeb.Tests/Security/UtcDateTimeTests.cs:94` ise değişmeyen bir alanın yazılmadığını sınar. Bunlar UTC'ye çevrilmedi ve koruma gevşetilmedi.

`MeetingWeb/Pages/History.cshtml.cs:78` ve `MeetingWeb/Pages/History.cshtml.cs:84` formdan gelen tarihleri yalnız sorgu sınırı olarak kullanıyor, model alanına yazmıyor. Karar gerek: PostgreSQL geçişinde bu parametrelerin UTC türü ve seçilen günün UTC günü mü Türkiye günü mü olduğu ayrıca ele alınmalı; kullanıcıya gösterilen gün aralığı bu görevde değiştirilmedi.

Metin karşılaştırmaları `Services`, `Pages`, `Hubs` ve `Areas` altında tarandı; `Controllers` klasörü yok. Şifreli alanlar, uygulamanın ürettiği GUID kimlikleri ve bellekte yapılan karşılaştırmalar listeye alınmadı. `Contains`, `StartsWith`, `EndsWith`, `EF.Functions.Like` ve `string.Equals` taraması da yapıldı. Veritabanına giden, bu dışlamalardan sonra kalan karşılaştırmalar aşağıdadır.

`MeetingWeb/Pages/Auth/Register.cshtml.cs:81` telefon numarasının başka hesapta olup olmadığını karşılaştırır. Etkilenmez: giriş doğrulaması yalnız rakam kabul eder, harf yoktur.

`MeetingWeb/Pages/Profile/Index.cshtml.cs:116` telefon numarasının başka hesapta olup olmadığını karşılaştırır. Etkilenmez: giriş doğrulaması yalnız rakam kabul eder, harf yoktur.

`MeetingWeb/Pages/Workspace/Join.cshtml.cs:50` davet jetonunun SHA-256 özetini karşılaştırır. Etkilenmez: hem yazarken hem ararken `WorkspaceInvitation.HashOf` aynı büyük harfli onaltılık gösterimi üretir; kullanıcı jetonunun harfini değiştirmek başka bir özet üretir.

`MeetingWeb/Pages/Workspace/Join.cshtml.cs:82` daveti kullanırken aynı jeton özetini karşılaştırır. Etkilenmez: iki taraf da aynı `HashOf` çıktısıdır.

`MeetingWeb/Services/MeetingSeriesService.cs:164` Zoom toplantı numarasından hesaplanan `ZoomMeetingKey` özetini karşılaştırır. Etkilenmez: yazma ve arama aynı `KeyedHash` yolunu kullanır; çıktı daima küçük harfli onaltılık gösterimdir.

`MeetingWeb/Services/ZoomService.cs:390` içe alınmış kaydın `ExternalRecordingId` değerini arar. Etkilenmez: SQL Server'da bu kolon zaten `Latin1_General_100_BIN2` kullanıyor, kayıt kimliğinin harf farkı bugün de ayrılıyor.

`MeetingWeb/Pages/Zoom/Recordings.cshtml.cs:64` Zoom kayıt kimliklerini `Contains` ile içe alınan kayıtlarla eşleştirir. Etkilenmez: karşılaştırılan `ExternalRecordingId` kolonu SQL Server'da zaten büyük-küçük harf ayırıyor.

`MeetingWeb/Pages/Workspace/Members.cshtml.cs:115` aynı ekipte aynı unvan adının bulunup bulunmadığını karşılaştırır. Karar gerek: PostgreSQL'de örneğin `Yönetici` ile `yönetici` farklı kabul edilebilir. Türkçe I/İ/ı/i eşitlik kuralı ve `(WorkspaceId, Name)` tekil indeksinin aynı kurala göre düzenlenmesi birlikte kararlaştırılmalı. Hazır normalize kolon yok; görünen adı büyük harfe çevirmek kullanıcı metnini değiştirir, normalize kolon ve indeks migration'ı ise bu görevin kapsamı dışındadır. Sorgu tek başına değiştirilmedi.

`MeetingWeb/Services/ZoomService.cs:246` bildirimdeki Zoom kullanıcı kimliğini `ZoomUserId` ile eşleştirir. Karar gerek: bu kolonda açık bir harf karşılaştırma kuralı yok; SQL Server'ın harf farkını yok sayması ile PostgreSQL'in ayırması farklı hesap eşleşmelerine yol açabilir. Dış sistemin verdiği kimliği büyük harfe çevirmek güvenli bir normalleştirme değildir; mevcut eşleşmenin mi yoksa birebir kimlik eşleşmesinin mi korunacağı kararlaştırılmalı.

`MeetingWeb/Services/ZoomWebhookHandler.cs:106` bağlantı kaldırma bildirimindeki Zoom kullanıcı kimliğini karşılaştırır. Karar gerek: `ZoomUserId` için yukarıdaki karar burada da uygulanmalı; hesap bağlantısı silindiği için eşleştirme kuralı varsayılmadı.

`MeetingWeb/Services/OverdueReminderService.cs:26` e-posta alanını null ve boş metinle karşılaştırır. Etkilenmez: başka bir e-posta adresiyle eşleştirme yapılmaz, boşluk kontrolünde harf yoktur.

`MeetingWeb/Services/CalendarSyncService.cs:237` e-posta alanını null ve boş metinle karşılaştırır. Etkilenmez: yalnız adresin varlığı denetlenir, harf karşılaştırması yapılmaz.

`MeetingWeb/Services/AudioCleanupService.cs:40` ses dosyası adını null ile karşılaştırır. Etkilenmez: dosya adı eşleştirmesi yapılmaz.

`MeetingWeb/Services/AudioCleanupService.cs:51` ses dosyası adını null ile karşılaştırır. Etkilenmez: dosya adı eşleştirmesi yapılmaz.

`MeetingWeb/Services/AccountDeletionService.cs:74` ses dosyası adını null ile karşılaştırır. Etkilenmez: yalnız dosya adının varlığı denetlenir.

`MeetingWeb/Pages/Workspace/Members.cshtml.cs:390` ses dosyası adını null ile karşılaştırır. Etkilenmez: yalnız dosya adının varlığı denetlenir.

E-posta araması `MeetingWeb/Pages/Auth/Login.cshtml.cs:69` ve `MeetingWeb/Services/PasswordResetService.cs:56` üzerinden Identity'nin `FindByEmailAsync` yolunu kullanır; normalize Identity alanları bu taramanın dışındadır. Rapor ve gündem alıcılarını seçen `MeetingWeb/Pages/Details.cshtml.cs:528` ile `MeetingWeb/Pages/Series/Show.cshtml.cs:72` karşılaştırmaları veriler okunduktan sonra bellekte `OrdinalIgnoreCase` kullanır. `Hubs` ve `Areas` altında kalan bir veritabanı metin karşılaştırması bulunmadı. Bu görevde metin karşılaştırması düzeltmesi yapılmadı; karar gerektirenler yukarıda açıklandı.

Çalıştırılan odaklı test komutu `dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~UtcDateTime"` oldu. İlk sandbox denemesinde test çalıştırıcısının yerel soket açması engellendi; testler başlamadı, geçme sayısı yoktur. Soket izniyle düzeltme öncesi koşuda 0 başarılı ve beklenen 8 başarısız test görüldü. Dönüştürücü ve koruma eklendikten sonra 8 başarılı, 0 başarısız ve 0 atlanan test görüldü. Değişmeyen tarih alanı senaryosu eklendikten sonraki son koşuda 10 başarılı, 0 başarısız ve 0 atlanan test görüldü.

`dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~CalendarSyncTests|FullyQualifiedName~CalendarSelfServiceTests"` komutu UTC koruması açıkken düzeltme öncesi çalıştırıldı; 79 başarılı, UTC ihlallerinden kaynaklanan 4 başarısız ve 0 atlanan test görüldü. `dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~CalendarSync|FullyQualifiedName~CalendarSelfServiceTests|FullyQualifiedName~CalendarLossTests|FullyQualifiedName~CalendarSweepTests"` komutu düzeltmeden sonra değişen test dosyasındaki beş sınıfı çalıştırdı; 110 başarılı, 0 başarısız ve 0 atlanan test görüldü. Koşularda NuGet güvenlik açığı kaynağına erişilemediğine dair NU1900 uyarısı vardı; test çalışmasını engellemedi. Tam test takımını ve `scripts/verify.sh` dosyasını çalıştırmadım. Git komutu kullanmadım; görev kutusunu işaretlemedim.
