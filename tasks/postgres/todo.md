# Görevler: SQL Server'dan PostgreSQL'e geçiş

Plan: `tasks/postgres/plan.md` · Spec: `SPEC-postgres.md`.

Odaklı test komutu: `dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~<TestSınıfı>"`. Her görev commit'ten önce `scripts/verify.sh`'den (hook) geçer.

İşçi brifi: önce `incremental-implementation` ve `test-driven-development` skill'lerini oku. Yalnız değiştirdiğin yerin testlerini koş; tam takımı commit hook'u koşar.

## Aşama 0: Hazırlık

- [x] **Görev 1: Eski veritabanının yedeği** (Ulaş, XS) — alındı: `~/Yedekler/briefyn-db-2026-11-23/BriefynDB.bak`, 12 MB (Ulaş, 2026-11-23; `RESTORE VERIFYONLY` çıktısını Claude görmedi)
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

- [x] **Görev 2b: Geçmiş sayfasında tarih filtresi Türkiye günü** (S)
  - Karar (Ulaş, 2026-11-23): seçilen gün Türkiye saatiyle 00:00-24:00. Bugün UTC günü (Türkiye saatiyle 03:00-03:00) uygulanıyor.
  - Kabul: `History.cshtml.cs:78-84` başlangıç ve bitiş günü `Europe/Istanbul` saat diliminde günün başı olarak alınır, UTC'ye çevrilir (`Kind == Utc`) ve öyle sorgulanır. Gece 00:00-03:00 (Türkiye) arası oluşturulan toplantı o günün filtresinde çıkar, bir önceki günün filtresinde çıkmaz.
  - Doğrula: sınır durumlarını sınayan odaklı test; `verify.sh` (hook).
  - Bağımlılık: yok (SQL Server'dayken yapılır).
  - Dosyalar: `MeetingWeb/Pages/History.cshtml.cs`, ilgili test dosyası.

## Aşama 1: Sağlayıcı değişimi (Kontrol A'ya kadar push yok)

- [x] **Görev 3: EF sağlayıcısı ve şema PostgreSQL'e** (M-L)
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
    - Ekip unvanı (`WorkspaceTitle.Name`) PostgreSQL'de büyük-küçük harf ayırmayan ICU karşılaştırma kuralıyla (nondeterministic collation, `HasCollation` + `UseCollation`, yalnız Npgsql dalında): aynı ekipte "Yönetici" varken "yönetici" eklenemez (Ulaş, 2026-11-23; bugünkü davranış). Görev 6'da PostgreSQL testi.
    - `ZoomUserId` birebir eşleşir, kod değişmez; ADR-0019'a not (Ulaş, 2026-11-23).
  - Doğrula: tam takım SQLite'ta geçiyor (hook); `dotnet ef migrations script` çıktısında köşeli parantez yok.
  - Bağımlılık: 1, 2.
  - Dosyalar: `MeetingWeb/MeetingWeb.csproj`, `MeetingWeb/Program.cs` (yalnız `UseNpgsql`), `MeetingWeb/Data/ApplicationDbContext.cs`, 4 model, `MeetingWeb/Migrations/*`, `MeetingWeb/Services/LogCleanupService.cs`, `MeetingWeb/Services/SpeakerNamingService.cs` (yorum), `MeetingWeb.Tests/Features/ZoomTests.cs`.

- [x] **Görev 4: Hangfire ve log hedefi PostgreSQL'e** (S)
  - Kabul:
    - `Hangfire.SqlServer` ve `Serilog.Sinks.MSSqlServer` çıktı; `Hangfire.PostgreSql` 1.21.x ve `Serilog.Sinks.Postgresql.Alternative` 4.3.x girdi.
    - Web `UsePostgreSqlStorage(... PrepareSchemaIfNecessary = false)`; `migrate` adımı Hangfire şemasını kuruyor.
    - Serilog hedefi `Logs` kolonlarına açıkça eşlenmiş, `needAutoCreateTable = false`, yalnız uyarı ve üstü.
    - "MSSQL" yazıları temizlendi.
    - Hangfire.PostgreSql'in varsayılan şema adı ve istediği yetkiler kaynağından bakılıp buraya not edildi (Görev 5 kullanır).
  - Doğrula: build ve tam takım (hook). Testlerde Hangfire ve SQL log zaten kapalı (`isTesting`).
  - Bağımlılık: 3.
  - Dosyalar: `MeetingWeb/MeetingWeb.csproj`, `MeetingWeb/Program.cs`.

- [x] **Görev 5: Compose, roller ve `.env.example`** (M)
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

### Kontrol A (2026-11-23, Claude; yerel yığın, boş `pg_data`)
- [x] `scripts/verify.sh` GEÇTİ (Görev 5 commit'inde, 1730/1730).
- [x] Spec başarı ölçütü 2: `docker compose up -d --build` boş birimle açıldı; `db-init`, `migrate`, `db-readonly` 0 ile çıktı; `platform` satırı yok, `postgres:18` ARM'de öykünmesiz; `/tr` 200.
- [ ] Spec başarı ölçütü 3, elle (tarayıcı + `psql` ile veritabanından bakıldı):
  - [x] kayıt: iki test hesabı (`kontrol-a1@example.test`, `kontrol-a2@example.test`), kişisel alanlar, tarihler `timestamptz` UTC;
  - [x] ekip ve davet: ekip kuruldu, davet bağlantısıyla ikinci kullanıcı katıldı, davet kullanıldı işaretlendi; "Yönetici" varken "yönetici" eklenemedi ("Bu adda bir unvan zaten var.", `tr_ci`);
  - [x] yükleme ve analiz: `say` ile üretilen Türkçe ses (m4a, 165 KB) yüklendi; Groq'la ~3 sn'de tamamlandı; 8 madde, 2 görev; ses silindi; Hangfire işi `Succeeded`;
  - [x] görev: atama ve bitiş tarihi (`DueDate` = 2026-12-15), tamamlama, elle görev ekleme; takvim eşitleme işi `Succeeded` (test hesaplarında takvim daveti kapatıldı, e-posta gitmedi);
  - [x] yorum ve tepki: yorum ve beğeni yazıldı;
  - [x] sayfalar: Geçmiş (tarih filtresiyle: 23 Kasım'da toplantı var, 22 Kasım'da yok), Görevlerim, Sor, Seriler 200;
  - [ ] şifre sıfırlama: denenmedi, gerçek e-posta gönderir (Ulaş);
  - [ ] takvim daveti e-postası: denenmedi, gerçek e-posta gönderir (Ulaş).
- [x] Spec başarı ölçütü 4: arayüzden iki sekmeyle tetiklenemiyor (form sürümü taşımıyor; çakışma yalnız aynı isteğin okuma ile yazma arasında olur). Veritabanında gösterildi: aynı eski `xmin` ile ilk UPDATE 1 satır, ikincisi 0 satır (EF'in çakışma saydığı durum). Uygulama yolu Görev 6'da PostgreSQL testiyle sınanır.
- [x] Spec başarı ölçütü 5 (yerel yığında): `briefyn_app` tablo kuramıyor (`permission denied for schema public`), `briefyn_okur` `AspNetUsers`'ı parola kolonuyla okuyamıyor (`permission denied for table AspNetUsers`), e-postayı okuyabiliyor.
- [x] Spec başarı ölçütü 6: iki uyarı satırı `Logs`'a düştü (`Level` = Warning, `TimeStamp` UTC). `LogCleanupService` silme yolu denenmedi (30 günlük kayıt yok).
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

## Görev 2b notu

Değişen dosyalar: `MeetingWeb/Pages/History.cshtml.cs`, `MeetingWeb.Tests/Features/HistoryDateFilterTests.cs` ve bu notun eklendiği `tasks/postgres/todo.md`. Geçmiş sayfasında seçilen günün başlangıcı ve ertesi günün başlangıcı `Europe/Istanbul` saat diliminden UTC'ye çevrilerek sorgulanıyor. İki toplantı saati için dört test durumu, toplantının Türkiye gününde göründüğünü ve komşu günde görünmediğini denetliyor.

Çalıştırılan komut: `dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~History"`. İlk denemede test çalıştırıcısının yerel soket açması engellendi; testler başlamadı. Soket izniyle düzeltme öncesi koşuda 10 başarılı, beklenen 2 başarısız ve 0 atlanan test görüldü. Düzeltme sonrası aynı komutla 12 başarılı, 0 başarısız ve 0 atlanan test görüldü. NuGet güvenlik açığı kaynağına erişilemediği için NU1900 uyarısı vardı; testleri engellemedi.

Tam test takımını ve `scripts/verify.sh` komutunu çalıştırmadım. Git komutu çalıştırmadım; görev kutularını değiştirmedim.

## Görev 3 notu

- Değişen dosyalar: `MeetingWeb/MeetingWeb.csproj`, `MeetingWeb/Program.cs` (yalnız EF sağlayıcısı), `MeetingWeb/Data/ApplicationDbContext.cs`, `MeetingWeb/Models/Meeting.cs`, `MeetingWeb/Models/ActionItem.cs`, `MeetingWeb/Models/ZoomConnection.cs`, `MeetingWeb/Models/CalendarInvite.cs`, `MeetingWeb/Services/LogCleanupService.cs`, `MeetingWeb/Services/SpeakerNamingService.cs` (yalnız yorum), `MeetingWeb.Tests/Features/ZoomTests.cs`, `MeetingWeb.Tests/Features/SpeakerNamingTests.cs`, yeni `MeetingWeb.Tests/Security/PostgresModelTests.cs` ve bu not. Eski 13 migration'ın 26 dosyası silindi; `MeetingWeb/Migrations/20261123120226_InitialCreate.cs`, tasarım dosyası ve `ApplicationDbContextModelSnapshot.cs` yeniden üretildi.
- Npgsql paketi 10.0.4 oldu. Hangfire ve Serilog'un SQL Server paketleri korundu. Dört sürüm alanı `uint` oldu; PostgreSQL'de `xmin`, SQLite'ta her kayıtta rastgele sayı kullanılıyor. Konuşmacı testindeki SQLite tetikleyicisi de sayı üretecek biçime uyarlandı; test silinmedi veya atlanmadı. Silme ilişkileri ve bunların mevcut yorumları değiştirilmedi.
- `dotnet restore MeetingWeb`: ilk deneme ağ kısıtı nedeniyle NU1301 verdi; ağ erişimiyle tekrarında başarılı. Test projesinin ilk odaklı test denemesi de bağımlılıkları indirirken NU1301 verdi; `dotnet restore MeetingWeb.Tests` ağ erişimiyle başarılı oldu. Test çalıştırıcısının yerel soket açması sandbox içinde engellendiği için sonraki testler sandbox dışında çalıştırıldı.
- Tasarım zamanı komutlarında `ConnectionStrings__DefaultConnection="Host=localhost;Database=design;Username=x;Password=x"` ve `EncryptionSettings__KeyV2="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="` kullanıldı. İlk migration komutu host başlangıcında bir dakikadan uzun beklediği için durduruldu. Sonraki başarılı komutlarda ek olarak `ASPNETCORE_ENVIRONMENT=Testing` verildi ve sandbox dışında çalıştırıldı. Başka tasarım zamanı ayarı gerekmedi.
- `MeetingWeb` içinde bu üç ortam değişkeniyle `dotnet ef migrations add InitialCreate --output-dir Migrations`: başarılı; derleme geçti. Up sonunda istenen `Logs` tablosu ve `IX_Logs_TimeStamp`, Down başında tabloyu kaldıran SQL var. Dört `xmin` eşlemesi ve Türkçe, büyük-küçük harf ayırmayan `tr_ci` kuralı üretildi.
- Aynı dizin ve değişkenlerle `dotnet ef migrations script --idempotent -o /tmp/briefyn-pg.sql`: başarılı; derleme geçti. `wc -l /tmp/briefyn-pg.sql`: **675 satır**. `rg -n "\[|nvarchar" /tmp/briefyn-pg.sql` eşleşme vermedi. İstenen `grep -n "\[" MeetingWeb/Migrations/*InitialCreate.cs | grep -v "\[DbContext\|\[Migration\|\[\]\|new\[\]"` komutu da eşleşme vermedi. Ek Python denetimi dört xmin eşlemesini, betikte köşeli parantez/nvarchar olmadığını, tr_ci kuralını ve Logs tablosunu doğruladı. İlk ek Python denetiminde `False` sözcüğünün harf büyüklüğüne ilişkin denetim yanlıştı; denetim düzeltildi ve geçti, üretilen SQL değişmedi.
- EF komutları araç 10.0.9 / çalışma zamanı 10.0.15 sürüm farkı uyarısı yazdı; paket sürümleri kapsam dışında değiştirilmedi. Tasarım zamanı host durdurulduğunda mevcut Program hata yakalaması `HostAbortedException` kaydı yazdı; iki EF komutu da 0 çıkış koduyla tamamlandı ve dosyaları üretti.
- `dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~PostgresModel"`: önce 5 test beklenen nedenle başarısız oldu (dört alan byte[] idi, tr_ci yoktu); model değişikliğinden sonra **5 başarılı, 0 başarısız, 0 atlanan**.
- `dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~ZoomTests|FullyQualifiedName~SchemaTests|FullyQualifiedName~PostgresModel|FullyQualifiedName~SpeakerNaming|FullyQualifiedName~Calendar|FullyQualifiedName~UtcDateTime"`: ilk çalıştırmada **313 başarılı, 0 başarısız, 0 atlanan**; yeni migration ve Logs SQL'i eklendikten sonraki son çalıştırmada da **313 başarılı, 0 başarısız, 0 atlanan**. Değişen diğer test dosyası SpeakerNamingTests bu filtreye dahil. SQL Server'a özgü davranış yüzünden kalan test hatası yok.
- Çalıştırmadım: tam test takımı, `scripts/verify.sh`, ayrı biçim/lint denetimi, gerçek PostgreSQL üzerinde migration uygulaması ve uygulama akışları. Git komutu çalıştırmadım; commit veya PR oluşturmadım. `.env` dosyası okumadım. Hangfire, Serilog hedefi, Docker/compose, `db/`, CI, README ve ADR işleri bu görevin dışında bırakıldı. Kutucuklar işaretlenmedi.

## Görev 4 notu

Değişen dosyalar: `MeetingWeb/MeetingWeb.csproj`, `MeetingWeb/Program.cs` ve bu notun eklendiği `tasks/postgres/todo.md`. `Hangfire.SqlServer` yerine `Hangfire.PostgreSql` **1.21.1**, `Serilog.Sinks.MSSqlServer` yerine `Serilog.Sinks.Postgresql.Alternative` **4.3.0** eklendi. `dotnet restore MeetingWeb` ilk sandbox denemesinde NuGet adresine erişemediği için NU1301 verdi; ağ erişimiyle tekrarında başarılı oldu. `MeetingWeb/obj/project.assets.json` içinde iki paket de istenen sürümde, ortak Npgsql **10.0.4** olarak çözüldü; bağımlılık çakışması yok.

Hangfire web kaydı `UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString), new PostgreSqlStorageOptions { PrepareSchemaIfNecessary = false })` kullanıyor. ADR-0009 yorumu korundu. `migrate`, `PrepareSchemaIfNecessary = true` olan tek seçenek nesnesini hem `NpgsqlConnectionFactory(connectionString, storageOptions)` hem `PostgreSqlStorage(connectionFactory, storageOptions)` kurucusuna vererek depoyu bir kez oluşturuyor. Paket DLL'sinde bu kurucuların ve web için kullanılan metodun eski olarak işaretlenmediği doğrulandı. Kurucu, kurulum metodunu çağırıyor. Pakette `AllowDegradedModeWithoutStorage` varsayılanı `true`: kurulum hatası tekrar denemelerden sonra yutulabiliyor. Bu nedenle yalnız `migrate` seçeneklerinde `false` verildi; Hangfire kurulamazsa şema hazır mesajına geçilmiyor. Kaynak: [1.21.1 depolama kurucusu](https://github.com/hangfire-postgres/Hangfire.PostgreSql/blob/1.21.1/src/Hangfire.PostgreSql/PostgreSqlStorage.cs) ve [web kayıt API'si](https://github.com/hangfire-postgres/Hangfire.PostgreSql/blob/1.21.1/src/Hangfire.PostgreSql/PostgreSqlBootstrapperConfigurationExtensions.cs).

Görev 5 için yetki bulguları: `~/.nuget/packages/hangfire.postgresql/1.21.1/lib/netstandard2.0/Hangfire.PostgreSql.dll` yansıma ve IL okumasıyla incelendi; gömülü 21 kurulum SQL'i (`Install.v3.sql`–`Install.v23.sql`) `/tmp/briefyn-task4-inspect/` altında çıkarıldı.

- (a) `PostgreSqlStorageOptions.SchemaName` varsayılanı **`hangfire`**.
- (b) `Install.v3.sql`, şema yoksa `CREATE SCHEMA "hangfire"` çalıştırıyor; tabloları, indeksleri ve SERIAL dizilerini kuruyor. Sonraki betikler şemayı yükseltiyor. Bu iş şema yetkili migrator'a ait; şema ilk kez kurulacaksa veritabanında CREATE yetkisi de gerekir. Web kaydındaki `PrepareSchemaIfNecessary = false` bu kurulumu çalıştırmaz.
- (c) Uygulama rolüne veritabanına CONNECT, `hangfire` şemasına USAGE, şemadaki tablolara SELECT/INSERT/UPDATE/DELETE ve SERIAL/bigserial dizilerine USAGE gerekir. Yeni nesneler için varsayılan yetkiler migrator rolü adına hem tablolar hem diziler için tanımlanmalı. Kuyruk sorgusu `FOR UPDATE SKIP LOCKED` ile satır kilitliyor; dağıtık kilitler `hangfire.lock` tablosundaki SELECT/INSERT/UPDATE/DELETE işlemleriyle tutuluyor. DLL'nin çalışma zamanı SQL'inde açık `LOCK TABLE`, advisory lock çağrısı, CREATE TEMP veya TRUNCATE bulunmadı; bunlar için ek yetki gerekmiyor. LISTEN/NOTIFY kodu var; `EnableLongPolling` varsayılanı `false` ve bu görevde açılmadı. Uygulamaya şema/tablo oluşturma veya sahiplik yetkisi verilmesi gerekmiyor. Bunlar paket incelemesinin bulguları; gerçek PostgreSQL rol denemesi çalıştırmadım.

Serilog, `public` şemasındaki `Logs` tablosuna yalnız Warning ve üstünü yazacak biçimde ayarlandı; `needAutoCreateTable: false`. Kolonlar adları ve türleri açık verilerek eşlendi: `Message` → `RenderedMessageColumnWriter(Text)`, `MessageTemplate` → `MessageTemplateColumnWriter(Text)`, `Level` → `LevelColumnWriter(renderAsText: true, Varchar)`, `TimeStamp` → `TimestampColumnWriter(TimestampTz)`, `Exception` → `ExceptionColumnWriter(Text)`, `Properties` → `PropertiesColumnWriter(Text)`. `Id` eşlenmedi; migration'daki kimlik kolonu doldurur. `ApplyLevels()`, Console ve iki File hedefi aynı kaldı. Başlangıç metni `Veritabanı loglamasına geçiliyor...` oldu; test yorumlarındaki SQL Server ifadeleri güncellendi.

Adların korunması, kurulan 4.3.0 DLL'sindeki `SinkHelper.GetInsertQuery` ve `GetCopyCommand` incelenip aynı altı kolonla bellekte çağrılarak doğrulandı. İkisi de şema, tablo ve kolon adlarını çift tırnaklıyor. Üretilen INSERT:

```sql
INSERT INTO "public"."Logs" ("Message", "MessageTemplate", "Level", "TimeStamp", "Exception", "Properties") VALUES (@Message, @MessageTemplate, @Level, @TimeStamp, @Exception, @Properties);
```

`useCopy: false` seçildi: istenen sade, parametreli INSERT yolu kullanılıyor; COPY'nin gerçek tabloya yazılması bu görevde denenmedi. Paketle gelen `HowToUse.md` de tablo/kolon adlarının büyük-küçük harfe duyarlı olduğunu ve `useCopy: false` ile INSERT kullanıldığını açıklıyor: [kullanım belgesi](https://github.com/serilog-contrib/Serilog.Sinks.Postgresql.Alternative/blob/8bf3effd34924f259e3df64b763f10efeff1ca0f/HowToUse.md). Yansıma için yalnız `/tmp/briefyn-task4-inspect/` altında geçici, paketsiz bir .NET projesi kullanıldı; ilk denemede eksik paylaşılan framework referansı yüzünden araç durdu, geçici projeye `Microsoft.AspNetCore.App` referansı eklenince inceleme ve sorgu üretimi başarılı oldu. Depoya araç veya ek paket eklenmedi.

Çalıştırılan doğrulamalar:

- `dotnet build MeetingWeb --nologo`: başarılı, **0 hata, 0 uyarı**; eski API uyarısı yok.
- `rg -n 'MSSqlServer|SqlServerStorage|Hangfire\.SqlServer' MeetingWeb.Tests`: eşleşme yok; test dosyası değiştirmek gerekmedi.
- `dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~Hangfire|FullyQualifiedName~Log|FullyQualifiedName~PostgresModel"`: ilk denemede test projesinin NuGet erişimi NU1301 ile durdu, testler başlamadı. Ağ ve yerel soket erişimiyle aynı komutun tekrarında **78 başarılı, 0 başarısız, 0 atlanan**. Bu testler yeni Hangfire/Serilog veritabanı hedeflerini çalıştırmıyor (`isTesting`); API, kolon ve adların doğruluğu paket incelemesiyle doğrulandı.

Çalıştırmadım: tam test takımı, `scripts/verify.sh`, ayrı biçim/lint denetimi, gerçek PostgreSQL üzerinde Hangfire kurulumu ve log yazma denemesi. Git komutu çalıştırmadım, `.env` dosyası okumadım, görev kutularını değiştirmedim. `docker-compose.yml`, `db/` ve diğer kapsam dışı dosyalara dokunmadım. Görev 5'in rol/compose kurulumu ve sonraki gerçek PostgreSQL doğrulaması bekliyor.


## Görev 5 notu

Değişen dosyalar: `docker-compose.yml`, `db/init-app-user.sh`, `db/init-app-user.sql`, `db/init-readonly-user.sql`, `.env.example` ve bu notun eklendiği `tasks/postgres/todo.md`. Görev kutuları değiştirilmedi.

Veritabanı ve iki kurulum servisi için resmî, Debian tabanlı `postgres:18` seçildi; mimariyi zorlayan satırlar kaldırıldı. Veritabanı konteyneri `briefyn-pg`, yerel port `127.0.0.1:5432:5432`, kalıcı birim `pg_data:/var/lib/postgresql`. PostgreSQL 18 imajı birimi `/var/lib/postgresql` yolunda tanımlar; varsayılan veri dizini `/var/lib/postgresql/18/docker` olur. Kaynak: [resmî imaj belgesi](https://github.com/docker-library/docs/blob/master/postgres/README.md#pgdata) ve [resmî Dockerfile](https://github.com/docker-library/postgres/blob/master/18/trixie/Dockerfile). Denemede Debian **13.7**, PostgreSQL **18.7** ve aynı veri dizini görüldü. ICU desteği, `tr_ci` kuralının migration ile kurulmasıyla doğrulandı.

ADR-0009 ayrımı korundu: yalnız kurulum servisleri süper kullanıcı `postgres` ile bağlanır; `migrate`, `briefyn_migrator` ile şemayı kurar; web, `briefyn_app` ile yalnız veri okur/yazar. Varsayılan yetkiler, sonradan kurulan `hangfire` şemasını, tablolarını ve dizilerini de kapsar. Okuyucu izinleri her başlangıçta tablolardan ve kolonlardan kaldırılıp güncel kolon listesiyle yeniden verilir; tablo düzeyindeki REVOKE tek başına eski kolon izinlerini kaldırmadığı için kolon izinleri de temizlenir.

Adım 5 denemesi gerçek `.env` kullanılmadan, yalnız `/tmp/briefyn-pgtest.env` içindeki rastgele deneme parolaları ve 32 baytlık base64 şifreleme anahtarıyla yapıldı. Compose'un kullandığı diğer değişkenler boş bırakıldı. Okuyucu parolasında hem tek tırnak hem `$` vardı. `docker compose version`: **v5.7.1**; `!override` desteklendi. `/tmp/briefyn-pgtest.override.yml` yalnız veritabanı portunu `127.0.0.1:55432:5432` yaptı. Bütün Compose komutlarında `-p briefynpgtest --env-file /tmp/briefyn-pgtest.env -f docker-compose.yml -f /tmp/briefyn-pgtest.override.yml` kullanıldı.

Çalıştırılan denemeler ve gerçek sonuçları:

- GEÇTİ: `bash -n db/init-app-user.sh` ve `docker compose ... config --quiet`. `rg -n 'sqlcmd|MSSQL|SQL Server|platform:|BriefynDB|sql_data' docker-compose.yml db .env.example` eşleşme bulmadı (çıkış 1).
- GEÇTİ: ilk dilimde `up -d db db-init`; veritabanı sağlıklı, `db-init` çıkış 0.
- GEÇTİ: `up --build db db-init migrate db-readonly`; veritabanı sağlıklı, `db-init`, `migrate` ve `db-readonly` çıkış 0. Günlükte `migrate: veritabanı şeması hazır.` görüldü. EF migration ve Hangfire kurulumu tamamlandı. Web başlatılmadı.
- GEÇTİ: `run --rm db-init` ve `run --rm db-readonly` tekrarları çıkış 0; var olan veritabanı, roller, tablolar ve Hangfire şemasıyla kurulum tekrar çalıştı.
- GEÇTİ: `run --rm --no-deps -e READONLY_DB_PASSWORD= -e PGHOST=invalid-host.invalid db-readonly` çıkış 0; boş parolada bağlantı kurulmadan işlem bitti.
- GEÇTİ, beklenen ret: `briefyn_app` ile `CREATE TABLE public.x(i int)` çıkış 1, `ERROR:  permission denied for schema public`.
- GEÇTİ, beklenen ret: `briefyn_app` ile `CREATE SCHEMA y` çıkış 1, `ERROR:  permission denied for database briefyn`.
- GEÇTİ: `briefyn_app` ile `SELECT count(*) FROM "AspNetUsers"` sonucu **0**, çıkış 0.
- GEÇTİ: önce `\d hangfire.counter` ile gerçek kolonlar okundu: `id bigint`, `key text`, `value bigint`, `expireat timestamp with time zone`; `id` bir diziden gelir. `briefyn_app` ile `INSERT INTO hangfire.counter(key, value) VALUES ('t', 1)` sonucu `INSERT 0 1`; ardından aynı satır silindi, dönen değer `t|1`, sonuç `DELETE 1`.
- GEÇTİ, beklenen ret: `briefyn_okur` ile `AspNetUsers` tablosundaki `PasswordHash`, `SecurityStamp`, `ConcurrencyStamp` kolonlarının her biri ayrı ayrı sorgulandı; her biri çıkış 1, `ERROR:  permission denied for table AspNetUsers`.
- GEÇTİ: `briefyn_okur` ile `SELECT "Email" FROM "AspNetUsers"` çıkış 0, boş sonuç.
- GEÇTİ, beklenen ret: `briefyn_okur` ile `SELECT * FROM "AspNetUserTokens"` çıkış 1, `ERROR:  permission denied for table AspNetUserTokens`.
- GEÇTİ: `briefyn_okur` ile `SELECT count(*) FROM hangfire.job` sonucu **0**, çıkış 0.
- GEÇTİ, beklenen ret: `briefyn_okur` ile `hangfire.counter` tablosuna INSERT çıkış 1, `ERROR:  permission denied for table counter`.
- GEÇTİ: `briefyn_migrator` ile `pg_tables.tableowner` denetiminde `public` şemasındaki **22**, `hangfire` şemasındaki **12** tablonun tamamının sahibi `briefyn_migrator`; başka sahipli tablo sayısı **0**.
- GEÇTİ: `pg_collation` sonucu `tr_ci|i|f` (ICU, deterministik değil); `to_regclass` sonucu `"Logs"`, tablo var.
- GEÇTİ: üç rolde de superuser, veritabanı kurma ve rol kurma bayrakları `false`. Uygulamanın veritabanında, `public` ve `hangfire` şemalarında CREATE yetkisi yok; sahip olduğu veritabanı/şema/ilişki sayısı **0**. Hangfire dizilerinin tümünde USAGE var.
- GEÇTİ: ek katalog sorguları bütün tablolarda uygulamanın SELECT/INSERT/UPDATE/DELETE yetkilerini, okuyucunun hiçbir tabloda yazma yetkisi olmadığını ve bütün kolonların okuma izinlerinin yukarıdaki istisnalarla birebir eşleştiğini doğruladı; üç sonuç da `t`.

İlk SQL denemesinde **13/20** kontrol geçti; okuyucuya ait yedi kontrol `FATAL:  password authentication failed for user "briefyn_okur"` ile durdu. Sebep test aracının `docker compose config --format json` çıktısındaki `$` → `$$` kaçışını gerçek parola sanmasıydı. Geçici konteynerin parola uzunluğu ve karakter sayıları karşılaştırıldı; gerçek deneme parolasıyla bağlantı çıkış 0 verdi. Yalnız `/tmp` altındaki test aracı kendi oluşturduğu deneme dosyasını okuyacak şekilde düzeltildi; depo betiklerini değiştirmek gerekmedi. Son koşuda **20/20** kontrol geçti. SQL bağlantıları `-h db` ve her rolün kendi `PGPASSWORD` değeriyle kuruldu; yerel parolasız bağlantı kullanılmadı.

Temizlik GEÇTİ: `docker compose ... down -v --rmi local` çıkış 0; geçici konteynerler, `briefynpgtest_pg_data`, `briefynpgtest_default` ve `briefynpgtest-migrate:latest` kaldırıldı. Ardından `docker ps -a`, `docker volume ls --filter name=briefynpgtest`, `docker network ls --filter name=briefynpgtest` ve `docker image ls --filter 'reference=briefynpgtest*'` geçici kaynak göstermedi; sabit adlı `briefyn-pg` de yok. Oluşturulan sekiz `/tmp` dosyası, deneme env ve override dahil, silindi ve yoklukları denetlendi. Mevcut `briefyn-sql` son listede çalışmaya devam ediyordu; ona ve mevcut yığının kaynaklarına işlem yapılmadı.

Ulaş için, `docker compose up` öncesi:

- Gerçek `.env` dosyasına yeni `POSTGRES_PASSWORD` değişkenini güçlü bir parolayla ekle. Bu parola PostgreSQL süper kullanıcısınındır; bağlantılarda yalnız `db-init` ve `db-readonly` kullanır.

Açık sorun yok. Çalıştırmadım: .NET test takımı, `scripts/verify.sh`, ayrı lint/biçim denetimi ve web/HTTP kontrolleri. Git komutu kullanmadım; gerçek `.env` dosyasını okumadım veya değiştirmedim. .NET kodu, paketler, migration dosyaları, CI, README ve ADR dosyaları değiştirilmedi.
