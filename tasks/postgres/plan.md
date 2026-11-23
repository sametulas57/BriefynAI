# Uygulama planı: SQL Server'dan PostgreSQL'e geçiş

Spec: `SPEC-postgres.md` (onaylı, 2026-11-23) · Harita: `SPEC-barindirma.md` · Görev listesi: `tasks/postgres/todo.md` · Durum: onaylandı (Ulaş, 2026-11-23)

Bu plan ayrı klasörde, çünkü `tasks/plan.md` ve `tasks/todo.md` arayüz yenileme 1'e ait.

## Özet

Uygulama SQL Server yerine PostgreSQL'le çalışacak. Veri sıfırdan başlıyor, kullanıcıya görünen hiçbir şey değişmiyor.

Sıra:
1. eski veritabanının yedeği;
2. sağlayıcıdan bağımsız hazırlık;
3. EF sağlayıcısının değişmesi;
4. Hangfire ve log hedefinin değişmesi;
5. compose ve veritabanı rolleri;
6. yalnız gerçek PostgreSQL'de koşan test grubu ve CI;
7. belgeler.

Sağlayıcı değişimi bölünemiyor: Görev 3, 4 ve 5 bitene kadar yerel Docker yığını açılmaz. Testler her görevde geçer, çünkü SQLite'ta koşuyorlar. Bu yüzden 3-5'in commit'leri Kontrol A geçene kadar push edilmez; main'deki yığın hiç bozuk kalmaz.

## Kodda baktıklarım (planın dayanağı, 2026-11-23)

- **Sağlayıcıya bağlı kod:** `Program.cs`'te dört yer:
  - `UseSqlServer` (`:46`);
  - Hangfire `UseSqlServerStorage` (`:110`);
  - `migrate` adımındaki `new SqlServerStorage(... PrepareSchemaIfNecessary = true)` (`:272`);
  - `WriteTo.MSSqlServer` (`:299`).
- **`ApplicationDbContext`:**
  - köşeli parantezli 4 filtreli index ve 10 kontrol kısıtı;
  - `Latin1_General_100_BIN2` (`:91`);
  - `IsSqlServer()` dalları (`:182`, `StampRowVersions`).
- **Eşzamanlılık:** `byte[] RowVersion` dört modelde (`Meeting`, `ActionItem`, `ZoomConnection`, `CalendarInvite`). Bu alana dayanan yerler:
  - `SpeakerNamingService.cs:147-159`: ExecuteUpdate'ten sonra yeni sürümü yeniden okuyor;
  - `ZoomService.cs:173, 412`: sürüm eşitse sil;
  - `Details.cshtml.cs:482`: görev güncellemesi.
- **Tarihler:** 19 `DateTime` alanı, hepsi `...Utc` adlı. Uygulamada `DateTime.Now` yalnız e-posta alt bilgisindeki yılda (`EmailService.cs:349`), veritabanına yazılmıyor. `CalendarInviteBuilder.cs:28` `Kind == Local` durumunu ele alıyor. UTC olmayan değer yazan yer görülmedi; Görev 2 bunu testle sabitler.
- **Ham SQL:**
  - `LogCleanupService.cs:26` köşeli parantezli;
  - `Logs` tablosu `InitialCreate` içinde ham SQL Server SQL'iyle kurulu;
  - testlerdeki ham SQL zaten çift tırnaklı.
- **Testler:**
  - `BriefynFactory` SQLite (bellek içi ya da dosya);
  - `ZoomTests.cs:771` `UseSqlServer("Server=unused")` ile bir DbContext kuruyor.
- **Kurulum:**
  - `db/init-app-user.sh` + iki `.sql` (`sqlcmd`);
  - compose'da `db`, `db-init`, `db-readonly` hep `platform: linux/amd64`;
  - `.env.example`'da `MSSQL_SA_PASSWORD`.
- **CI:** `.github/workflows/test.yml` `scripts/verify.sh`'yi koşuyor; veritabanı servisi yok.
- **Commit öncesi hook** yalnız doküman değiştiyse atlıyor, kod varsa `verify.sh` koşuyor.

## Mimari kararlar

1. **Eşzamanlılık:** `[Timestamp] uint RowVersion`, PostgreSQL'in `xmin`'ine bağlı (Npgsql belgesi). SQLite'ta `StampRowVersions` her kayıtta rastgele `uint` verir; bugünkü `Guid` baytlarının karşılığı. Alan adı aynı kalır; değişen yalnız tipi.
2. **Filtre ve kısıt yazımı:** çift tırnak, boolean için `TRUE`. SQLite (testler) ve PostgreSQL aynı metni kabul ediyor; sağlayıcıya göre dal açılmaz.
3. **`Logs` tablosu:** migration'da ham SQL'le PostgreSQL yazımıyla kurulur. Kolonlar aynı (`Id`, `Message`, `MessageTemplate`, `Level`, `TimeStamp`, `Exception`, `Properties`), `TimeStamp` index'li. Serilog hedefi bu kolonlara açıkça eşlenir, tabloyu kendisi kurmaz.
4. **Hangfire:** web `PrepareSchemaIfNecessary = false`, `migrate` adımı `true` (bugünkü düzen).
5. **Roller:**
   - `briefyn_migrator` şema sahibi;
   - `briefyn_app` için `ALTER DEFAULT PRIVILEGES` ile yalnız SELECT/INSERT/UPDATE/DELETE (ve dizi kullanımı); Hangfire'ın şemasına da aynısı;
   - `briefyn_okur`'a tablo düzeyinde değil kolon düzeyinde SELECT verilir. PostgreSQL'de tablo yetkisi varken kolon kapatılamaz; bu yüzden `AspNetUsers`'ta parola, güvenlik damgası ve eşzamanlılık kolonları dışındakiler verilir, `AspNetUserTokens` hiç verilmez.
6. **PostgreSQL test grubu:**
   - `MeetingWeb.Tests/Postgres/`;
   - bağlantı `BRIEFYN_PG_TEST` ortam değişkeninden gelir;
   - değişken yoksa testler xUnit `Skip` gerekçesiyle "atlandı" görünür;
   - CI'da `test.yml`'e PostgreSQL servisi ve rol betiği eklenir.
7. **Yeni paket:** yalnız spec'te onaylanan üç paket.

## Görev listesi

Ayrıntı, kabul ölçütleri ve dosyalar: `tasks/postgres/todo.md`.

### Aşama 0: Hazırlık
- Görev 1: Eski veritabanının yedeği (Ulaş, XS)
- Görev 2: UTC kuralını testle sabitle, metin karşılaştıran sorguları listele (S)

### Aşama 1: Sağlayıcı değişimi (Kontrol A'ya kadar push yok)
- Görev 3: EF sağlayıcısı ve şema PostgreSQL'e (M-L)
- Görev 4: Hangfire ve log hedefi PostgreSQL'e (S)
- Görev 5: Compose, roller ve `.env.example` (M)
- Kontrol A: yığın boş birimle açılıyor, elle akışlar, push

### Aşama 2: Güvence
- Görev 6: PostgreSQL test grubu ve CI servisi (M)
- Kontrol B: CI'da grup koşuyor ve geçiyor

### Aşama 3: Kapanış
- Görev 7: ADR-0019, eski ADR notları, README, Backlog (S)
- Kontrol C: spec'in 6 başarı ölçütü; eski SQL Server birimi Ulaş onaylayınca silinir; inceleme hatırlatması

## Riskler

| Risk | Etki | Önlem |
|---|---|---|
| 3-5 arası yığın açılmıyor | Orta | Commit'ler Kontrol A'ya kadar push edilmez; üç görev arka arkaya yapılır |
| `xmin` ExecuteUpdate'ten sonra ilerlemezse `SpeakerNamingService` talebi bozulur | Yüksek | Görev 6'da PostgreSQL testi; Görev 3'te kod yeniden okuma yolunu korur |
| UTC olmayan tarih çalışma anında hata verir | Orta | Görev 2 testi; Görev 6'da gerçek PostgreSQL'e yazma testi |
| Büyük-küçük harf farkı bir sorgunun sonucunu değiştirir | Orta | Görev 2 listesi; etkilenen sorgu varsa aynı görevde düzeltilir |
| Kolon düzeyi yetki yeni kolonda okuru kör bırakır | Düşük | Betik her açılışta yeniden çalışır ve kolon listesini katalogdan üretir (kapalı üç kolon dışında hepsi) |
| Hangfire.PostgreSql'in istediği yetki README'de yok | Orta | Görev 4'te kaynağından bakılır; Görev 5'te web yalnız okur/yazar yetkisiyle iş kuyruğuna iş koyup alabiliyor mu elle denenir |

## Açık soru

Yok. Görev 1 Ulaş'ın: yedek komutu `sa` parolasını kullanıyor; bu oturumda Claude'un bu parolayla komut çalıştırması engellendi (2026-11-22).
