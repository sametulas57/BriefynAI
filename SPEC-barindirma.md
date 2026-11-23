# Yetenek haritası: Barındırma ve veritabanı (Backlog 9)

Durum: harita onaylandı (Ulaş, 2026-11-22). Her modülün kendi spec'i `SPEC-<modül>.md` olarak yazılır, haritadaki sırayla. Açık sorular 1-3 `dosya-deposu` ve `sunucu` spec'lerinden önce cevaplanır.

## Amaç

Uygulama bugün yalnız bu Mac'te Docker ile çalışıyor. Bu iş onu ücretsiz ya da çok ucuz bir sunucuda çalışmaya hazır hâle getirir. Yayına açmak bu işin parçası değil; o iş Backlog madde 1'de.

Ulaş'ın kararları (2026-11-22):
- bütçe: ücretsiz tercih; çalışmazsa ayda en fazla 5 dolar; Azure kredisi işe yarıyorsa o da olur;
- veri yeri: fark etmez; tek şart, toplantı verisinin eğitimde kullanılmaması (AGENTS.md kuralı);
- mevcut veri silinebilir, PostgreSQL boş başlar;
- kapsam: yalnız hazırlık, uygulama internete açılmaz.

## Neden birden çok modül

Bu iş beş ayrı parçadan oluşuyor ve her biri tek başına doğrulanabilir. Biri değişse diğerlerinin gereksinimi değişmez. Örneğin dosya deposu R2'den başka bir yere taşınsa veritabanı işi değişmez. Bu yüzden tek bir spec yerine önce bu harita onaylanır.

## Modüller

| Modül | Ne yapar | Neye bağlı |
|---|---|---|
| `postgres` | SQL Server'dan PostgreSQL'e geçiş. Kapsamı: EF sağlayıcısı (Npgsql), Hangfire deposu, veritabanı log hedefi; `[Timestamp]` alanları PostgreSQL karşılığına; filtreli index ve kontrol kısıtlarının yazımı; migration'lar tek `InitialCreate`; compose'da PostgreSQL ve üç giriş (uygulama, migrator, salt okunur, parola kolonları kapalı). Veri sıfırdan başlar, eski veritabanının yedeği alınır. | Python servisini kaldırma: bitti (`4618953`, 2026-11-22). |
| `durum-disarida` | Kapsayıcı silinip yeniden kurulunca hiçbir şey kaybolmaz. Data Protection anahtarları veritabanında durur; dosya günlüğü (`logs/`) kalkar, günlük konsola ve veritabanına yazılır; kullanılmayan `wwwroot/uploads` yolu ve `/uploads` koruması silinir. | `postgres` |
| `dosya-deposu` | Ses kayıtları ve profil fotoğrafları S3 uyumlu bir nesne deposunda durur (`IAudioStorage`'ın ikinci uygulaması). Yerelde bugünkü disk uygulaması kalır. | yok |
| `uretim-imaji` | Yayın imajı: SDK ile derlenir, yalnız çalışma ortamıyla (aspnet) çalışır; Tailwind CSS'i imaj içinde derlenir; ffmpeg kurulu; ARM64 için de derlenir. Ayrı bir üretim compose'u (web ve PostgreSQL; sırlar `.env`'den; `dotnet watch` yok, kaynak kod bağlanmaz). Backlog 1'deki "prod compose" ve "üretim imajı CSS" maddeleri burada kapanır. | `postgres`, `durum-disarida` |
| `sunucu` | Seçilen sunucu kurulur, üretim compose'u orada çalışır. Günlük PostgreSQL yedeği nesne deposuna alınır, geri yükleme bir kez denenir. Uygulama internete açılmaz: yalnız Ulaş'ın erişebildiği bir yoldan (SSH tüneli ya da IP kısıtı) denenir. | `uretim-imaji`, `dosya-deposu` |

Sıra: `postgres` → `durum-disarida` → `uretim-imaji` → `sunucu`. `dosya-deposu` bağımsız; ilk üçle aynı anda yürüyebilir.

## Barındırma önerisi

Kaynaklara 2026-11-22'de bakıldı.

### Baktıklarım

- **Oracle Cloud Always Free** (resmî belge): ARM sunucuda ayda 1.500 OCPU saati ve 9.000 GB saati, yani "2 OCPU ve 12 GB bellek". 200 GB disk, 20 GB nesne deposu, ayda 10 TB dışarı veri. Belgedeki kural: 7 gün boyunca işlemci (95. yüzdelik), ağ ve bellek kullanımı (ARM'de) üçü birden %20'nin altında kalırsa sunucu "boşta" sayılır ve geri alınabilir. Kuralın kullandıkça öde hesaplarına uygulanıp uygulanmadığı belgede yazmıyor. Yan kaynaklara göre bu sınır 2026'da 4 OCPU / 24 GB'tan yarıya indirilmiş.
- **Azure for Students** (resmî sayfa): 100 dolar kredi 12 ay içinde kullanılır, öğrenci kaldıkça yıllık yenilenir. 12 ay boyunca ücretsiz olanlar: B1s, B2pts v2 (ARM) ve B2ats v2 sunucularından her biri için ayda 750 saat; PostgreSQL Flexible Server B1MS için ayda 750 saat, 32 GB disk ve 32 GB yedek. App Service'in ücretsiz katmanı günde 1 saatle sınırlı. Sayfadaki kullanım şartı: yalnız eğitim, ticari olmayan araştırma ya da bu amaçlarla yazılım geliştirmek, denemek ve göstermek.
- **Neon ücretsiz plan** (resmî SSS, 2026-11-03): proje başına ayda 100 hesaplama saati (CU-saat), 1 GB depolama. 5 dakika istek gelmezse uyuyor ve bu kapatılamıyor.
- **Hetzner CAX11** (yalnız yan kaynaklar, resmî fiyat sayfasına bakılmadı): 2026 Haziran zammından sonra ayda yaklaşık 5,99 avro + KDV. Bu 5 dolar sınırının üstünde. Ekim 2026'da bazı bölgelerde satın alınamadığı da yazılmış.
- **Cloudflare R2** (Backlog 9 notu): 10 GB'a kadar ücretsiz, dışarı aktarma ücretsiz.

### Tahminim (denenmedi)

- **Neon uymaz.** Hangfire veritabanını sürekli yokluyor, bu yüzden veritabanı uyumaz. En küçük boyutta (0,25 CU) bile ayda yaklaşık 180 CU-saat eder, ücretsiz sınır 100. Supabase ve Render da Backlog'daki notlara göre uyuyor.
- **Azure for Students yalnız deneme ortamına uyar.** Ücretsiz sunucuların belleği küçük. Daha önemlisi, "ticari olmayan" şartı yüzünden gerçek kullanıcılı bir yayın için uygun görünmüyor. Yayında Microsoft for Startups (Backlog 1) asıl yol olur.
- **Önerim: Oracle Always Free, AB bölgesinde (ör. Frankfurt).** Web, PostgreSQL ve ffmpeg tek ARM sunucuda üretim compose'uyla çalışır. Dosyalar Oracle'ın kendi nesne deposunda (S3 uyumlu, 20 GB) ya da R2'de durur. Ücreti 0.
  - Risk 1, boşta geri alma: kullanıcı yokken sunucu "boşta" sayılabilir. Çözüm hesabı kullandıkça öde hesabına yükseltip bütçe uyarısı koymak olabilir; ama bu kart ister ve kuralın o hesaba uygulanmadığı doğrulanmadı.
  - Risk 2: Oracle sınırı habersiz düşürebiliyor (2026'da yarıya indirdiği yazılıyor). Bu yüzden imaj ve compose sağlayıcıdan bağımsız kalır; gerekirse aynı compose 5 dolarlık bir sunucuya taşınır.
- **ARM uyumu:** PostgreSQL ARM'de doğal çalışıyor. SQL Server bugün bu Mac'te bile amd64 öykünmesiyle çalışıyor (`docker-compose.yml`'de `platform: linux/amd64`). PostgreSQL'e geçiş hem yereli hem Oracle ARM'i rahatlatır.

## Başarı ölçütleri (bütün iş için)

1. `scripts/verify.sh` geçiyor; SQL Server'a ait paket, betik ve compose servisi kalmamış.
2. Yerelde `docker compose up` PostgreSQL ile açılıyor: kayıt, toplantı yükleme, analiz, yorum ve davet akışları çalışıyor.
3. Web kapsayıcısı silinip yeniden kurulunca oturumlar düşmüyor, yüklenen ses ve profil fotoğrafları duruyor.
4. Üretim imajı ARM64'te derleniyor; içinde SDK ve kaynak kod yok; Tailwind CSS'i imajda derlenmiş.
5. Seçilen sunucuda üretim compose'u çalışıyor, internetten açık değil; günlük yedek alınıyor ve bir kez geri yükleme denenmiş.
6. Aylık maliyet 0 (Oracle) ya da en fazla 5 dolar.

## Sınırlar

- Her zaman: her modül kendi spec'i ve `tasks/` planıyla yürür; kararlar ADR'ye yazılır (PostgreSQL ve barındırma için birer ADR).
- Önce sor: ücretli hesap açmak ya da karta bağlamak; yeni paket eklemek (Npgsql, Hangfire.PostgreSql, Serilog PostgreSQL hedefi, S3 istemcisi); bulut hesabı açmak (Ulaş açar).
- Asla: toplantı verisini eğitimde kullanan bir sağlayıcı; sunucuya yerel yapay zekâ modeli kurmak (AGENTS.md); uygulamayı bu iş kapsamında internete açmak.

## Açık sorular

1. Oracle Always Free hesabını Ulaş açabilir mi? Kayıt kart doğrulaması istiyor; bölge ve kapasite sorunları sık yazılıyor. Açılamazsa ikinci seçenek ne olsun: 5 doları biraz aşan Hetzner mi, yoksa yalnız deneme için Azure for Students mı?
2. Boşta geri alma riskine karşı Oracle hesabı kullandıkça öde hesabına yükseltilsin mi (kart bağlanır, bütçe uyarısı konur)?
3. Nesne deposu Oracle'ın kendi deposu mu, Cloudflare R2 mi? Oracle tek sağlayıcıda kalır; R2 sunucu değişse de yerinde kalır.
4. `sunucu` modülü bu işte mi kalsın, yoksa sunucuya kurulum yayın hazırlığıyla (Backlog 1) birlikte mi yapılsın?
5. ~~Python'u kaldırma bitmeden `postgres` başlamasın mı?~~ Cevaplandı: Python kaldırma bitti (2026-11-22).

## Kaynaklar

- Oracle Always Free belgesi: https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm
- Azure for Students: https://azure.microsoft.com/en-us/free/students
- Neon ücretsiz plan SSS: https://neon.com/faqs/free-plan-limits-and-quotas
- Hetzner fiyatları (yan kaynak): https://vpsdex.com/en/activity/hetzner-cloud-pricing-2026/
