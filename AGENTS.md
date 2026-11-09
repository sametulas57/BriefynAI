# Briefyn AI: agent talimatları

Toplantı ses kaydı → yazıya dökme → özet (konu, kararlar, aksiyonlar) → şifreli arşiv, ekip içi paylaşım ve canlı bildirim.
Servisler (Docker Compose): `web` (ASP.NET Core Razor Pages, Identity, EF Core, Hangfire, SignalR), `ai_engine` (FastAPI; Groq üzerinden yazıya dökme ve özet), `db` (SQL Server 2022), `db-init` (uygulama kullanıcısı).

<!-- proje-cekirdegi: başla (kaynak: Beyin notes/proje-mimarisi.md; burada değiştirme, beyinde değiştir ve kopyala) -->
## Roller ve çalışma düzeni
- `CLAUDE.md` yalnız `@AGENTS.md` satırıdır; tek kaynak bu dosya.
- Yöntem Addy Osmani Agent Skills (`agent-skills` eklentisi): `/spec` → `SPEC.md`, `/plan` → `tasks/plan.md` + `tasks/todo.md`, uygulama, `/review` (Claude Code'da `/agent-skills:review`), `/ship`. Küçük iş tek oturumda; büyük işte her aşama yeni oturumda, devir sohbetten değil onaylı dosyalardan.
- Kodu Codex yazar: Claude `/build` çalıştırmaz; `tasks/todo.md`'deki sıradaki görev Codex'e verilir, Codex `incremental-implementation` + `test-driven-development` skill'leriyle yapar. Kullanıcı "Claude yapsın" derse Claude `/build` ile yazar.
- Güvenlik: kod yazılırken Addy `security-and-hardening`; denetim Claude Security eklentisi (`/claude-security`, kullanıcı çalıştırır): ajan main'e almadan önce dal için değişiklik taramasını (özellik boyutunda `high`), yayından önce tüm depo taramasını önerir; main'e alma koşulu değildir. `/ship`'teki güvenlik personası bu taramanın yerini tutmaz. Raporlar `CLAUDE-SECURITY-*/` klasöründe kalır, commitlenmez.
- Yalnız kullanıcının çalıştırabildiği komutların (`/spec`, `/plan`, `/test`, `/ship`, `/constraints`, `/claude-security`) sırası gelince ajan komutu tek satırda önerir; kendisi çalıştırmaz. `/review` yerine ajan aynı incelemeyi taze bir alt ajanla kendisi yaptırabilir.
<!-- proje-cekirdegi: bitir -->

## Önce oku
- `SPEC.md`, `tasks/plan.md`, `tasks/todo.md`: o anki iş (Addy `/spec`, `/plan`). Yeni özellik koddan önce `/spec` ile başlar; kabul ölçütleri teste dönüşür.
- `docs/yol-haritasi.md`: backlog (açık işler, planlanan özellikler). Sıradaki iş buradan seçilir; iş bitince maddesi çıkarılır.
- `docs/sabitler.md`: port, ad, yol gibi değişmeyen değerler; emin olamadığında önce buraya bak.
- `docs/kararlar/`: mimari kararlar (ADR). Bir kararı değiştirecek iş yapmadan önce ilgili ADR'yi oku; yeni mimari karar = yeni ADR.
- `docs/raporlar/`: araştırmalar ve denemeler. İşe başlamadan konuyla ilgili raporu oku; yeni araştırmayı buraya rapor olarak yaz.

## Aletler
- Veritabanına salt okunur bakış: `scripts/db-sorgu.sh --sema` (tablolar/kolonlar) ya da `scripts/db-sorgu.sh "SELECT ..."`. Yazamaz; hassas içerik zaten şifreli, parola özetleri kapalı. Yığın açık olmalı.
- Güncel doküman: dış kütüphane/API işinde önce resmi güncel dokümanı web'den oku; kalıcı önemi varsa `docs/raporlar/` altına kısa rapor yaz.

## Çalıştırma ve test
- Başlat: `docker compose up -d --build` · durdur: `docker compose stop` (iş bitince kendi açtığını durdur; `down -v` veriyi siler, asla kullanma). Açmadan önce `docker compose ps`: açıksa olanı kullan; başka oturumun kullandığı yığını kapatma.
- .NET testleri: `dotnet test MeetingWeb.Tests` · Python testleri: `ai_engine` içinde `pytest`.
- Önce test (kırmızı), sonra kod (yeşil). Testler yeşil olmadan "bitti" denmez.

## Doğrulama (testlere ek, 2026-10-18)
- **Tek komut (2026-10-19):** `scripts/dogrula.sh` (build + .NET testleri + pytest, sonuç tek satır GEÇTİ/KALDI). "Bitti" = bu komut geçti; rapora GEÇTİ satırını yaz. Her commit'ten önce `.githooks/pre-commit` aynı komutu çalıştırır (Claude da Codex de geçer; yalnız doküman değişikliği atlanır), kalırsa commit durur. Claude'da ayrıca Stop hook'u: kod değişip komut geçmediyse "bitti" dedirtmez. `--no-verify` kullanma. Yeni klonda bir kez: `git config core.hooksPath .githooks`.
- **Kontrol geçtiyse sorma:** testler geçtiyse ve arayüz işinde agent ekranda gördüyse Ulaş'a "kontrol eder misin" diye sorulmaz, "yapıldı" denir; Ulaş sonra bakar, istemediğini commit'ten geri aldırır (`git revert`). Tercih kararları yine sorulur.
- **Aynı hata iki kez:** bir hata ikinci kez çıkarsa teste dönüştürülür.
- **Ekranda kontrol:** arayüze dokunan işte "bitti" demeden önce agent uygulamayı tarayıcıda açar (http://127.0.0.1:8080 ya da önizleme portu), değişen sayfaları test hesabıyla kullanır ve gördüğünü raporlar. Değişiklik başına 1–3 küçük ekran görüntüsü; taşma, konum gibi ölçülebilenleri sayfaya komutla sor (2026-10-21, token).
- **Test hesabı (Ulaş, 2026-10-20):** her yerel portta (8080, önizlemeler) giriş **her zaman** `.env`'deki `TEST_USER_EMAIL` / `TEST_USER_PASSWORD` ile, agent'ın kendisi yapar; Ulaş'tan giriş istenmez. Hesap o veritabanında yoksa agent Kayıt Ol sayfasından açar (ad: "Test Kullanıcı"). Yalnız 127.0.0.1'de; şifre sohbete, loga, koda yazılmaz; Ulaş'ın gerçek hesabı kullanılmaz. **Zoom denemeleri (Ulaş, 2026-10-22):** yayına kadar test hesabı + ayrı bir deneme Zoom hesabı; Ulaş'ın kendi Briefyn/Zoom hesabı karıştırılmaz.
- **İnceleme:** üreten ile denetleyen ayrıdır; yazan oturum kendi işini onaylamaz. `main`'e almadan önce Addy `/review` (Claude Code'da `/agent-skills:review`) taze bir alt ajanla kod farkını inceler; arayüz işinde ayrıca ekranda kontrol yapılır. Gerçek para veya çok kritik riskte birden fazla bağımsız inceleme. Tur sınırı yok; bulgular düzeltilip inceleme temiz çıkana kadar sürer. "Düşük" bulgular yol haritasına "sonra" diye yazılır.

## Kod yerleşimi (2026-10-21)
- Sayfalar özellik klasöründe: `Pages/Auth` (giriş/kayıt), `Pages/Profile`, `Pages/Workspace`; toplantı sayfaları `Pages/` kökünde. Sayfa mantığı servise (`Services/`), varlıklar `Models/`, EF `Data/`.
- Dosya adı = sınıf adı, namespace = klasör. Saklanan sabit değerler tek yerde: rol `WorkspaceRole`, madde türü/kaynağı `MeetingPointKind`/`EntrySource`, kaydın kaynağı `MeetingSource`, çerezdeki profil alanı `UserClaimTypes`, oturum anahtarı `ActiveWorkspaceService.SessionKey`. Şema ADR-0009; şema değişikliği yeni migration, `migrate` servisi uygular. Ayar varsayılanları `appsettings.json`'da görünür.

## Kurallar
- Sırlar yalnız `.env`'de (git'e girmez); koda, compose'a, loglara sır yazma.
- Her iş ayrı dalda (aynı klasörde; ek maliyeti yok), push et. Ayrı klasör (worktree) yalnız aynı anda birden çok agent çalışırken: orada `.env`, `ai_engine/.venv` ve ilk derleme ek iş, 8080 yığını ana klasörün kodunu çalıştırır (Ulaş, 2026-10-21). **main'e alma (Ulaş, 2026-10-20):** `scripts/dogrula.sh` GEÇTİ, `/review` temiz (bulgular düzeltildi) ve arayüz işinde ekranda kontrol yapıldıysa agent işin sonunda sormadan main'e alır, push eder ve Ulaş'a söyler; Ulaş beğenmezse `git revert` ile geri alınır. **2026-10-21 (Ulaş):** migration, güvenlik ve şifreleme işleri de sormadan main'e alınır. **Yalnız şu sorulur:** var olan veriyi geri gelmeyecek şekilde silen ya da bozan bir adım (ör. veritabanını silmek); öncesinde yedek alınır.
- Yetki: kullanıcı yalnız üyesi olduğu workspace'in verisini görür/değiştirir (ADR-0005).
- Mevcut sayfaların görünümünü değiştirme; yeni öğeler mevcut stil ve bileşenlerle.
- Kullanıcıya görünen metinde teknik terim yok; sade Türkçe.
- Ücretli servis/paket ekleme ve AI sağlayıcısı değiştirme Ulaş'ın onayı olmadan yok (ADR-0001).
- **Kısıtlar (Ulaş; `/spec` bunları `SPEC.md` sınırlarına taşır):** bütçe çok az, ucuz ve sunucuyu yormayan çözümler; yerel model (Whisper/LLM) sunucuya kurulmaz. Toplantı verisi şirket verisidir: hiçbir sağlayıcıyla eğitim için paylaşılmaz, saklanmaz (ZDR); yeni sağlayıcı bu şartla seçilir (ADR-0001). Ses başarılı analizden sonra silinir, döküm şifreli saklanır (ADR-0002).
