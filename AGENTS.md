# Briefyn AI: agent talimatları

Toplantı ses kaydı → yazıya dökme → özet (konu, kararlar, aksiyonlar) → şifreli arşiv, ekip içi paylaşım. Docker Compose: `web` (ASP.NET Core Razor Pages, EF Core, Hangfire, SignalR), `ai_engine` (FastAPI, Groq), `db` (SQL Server 2022), `db-init`.

<!-- proje-cekirdegi: başla (kaynak: Beyin notes/proje-mimarisi.md; burada değiştirme, beyinde değiştir ve kopyala) -->
## Roller ve çalışma düzeni
- `CLAUDE.md` yalnız `@AGENTS.md` satırıdır; tek kaynak bu dosya.
- Yöntem Addy Osmani Agent Skills (`agent-skills` eklentisi): `/spec` → `SPEC.md`, `/plan` → `tasks/plan.md` + `tasks/todo.md`, uygulama, `/review` (Claude Code'da `/agent-skills:review`), `/ship`. Küçük iş tek oturumda; büyük işte her aşama yeni oturumda, devir sohbetten değil onaylı dosyalardan.
- Kodu Codex yazar: Claude `/build` çalıştırmaz. Plan onayı tek kapıdır: sonra `tasks/todo.md`'deki görevler sırayla Codex'e verilir, Codex her birini `incremental-implementation` + `test-driven-development` ile yapar, `todo.md`'de işaretler, commitler; Addy `/build auto` durma koşullarında (test yeşile dönmüyor, spec belirsiz, iş riskli ya da geri alınamaz) durur ve raporlar. Claude testi yeniden koşmaz (commit öncesi hook ve CI koşar), raporu okur. Kullanıcı "Claude yapsın" derse Claude `/build` ile yazar.
- Güvenlik: kod yazılırken Addy `security-and-hardening`; denetim Claude Security eklentisi (`/claude-security`, kullanıcı çalıştırır): ajan main'e almadan önce dal için değişiklik taramasını (özellik boyutunda `high`), yayından önce tüm depo taramasını önerir; main'e alma koşulu değildir. `/ship`'teki güvenlik personası bu taramanın yerini tutmaz. Raporlar `CLAUDE-SECURITY-*/` klasöründe kalır, commitlenmez.
- Yalnız kullanıcının çalıştırabildiği komutların (`/spec`, `/plan`, `/test`, `/review`, `/ship`, `/constraints`, `/code-simplify`, `/webperf`, `/claude-security`) sırası gelince ajan komutu tek satırda önerir; kendisi çalıştırmaz.
<!-- proje-cekirdegi: bitir -->

## Önce oku
- `SPEC.md`, `tasks/plan.md`, `tasks/todo.md`: o anki iş. Yeni özellik koddan önce `/spec` ile başlar.
- `docs/yol-haritasi.md`: backlog; sıradaki iş buradan seçilir, bitince maddesi çıkarılır.
- `docs/sabitler.md`: port, ad, yol gibi sabit değerler; emin olamadığında önce buraya bak.
- `docs/kararlar/`: ADR'ler. Kararı değiştirecek işten önce ilgili ADR okunur; yeni mimari karar = yeni ADR.
- `docs/raporlar/`: araştırmalar. İşe başlamadan ilgili rapor okunur; yeni araştırma buraya rapor olur.

## Aletler
- Veritabanına salt okunur bakış: `scripts/db-sorgu.sh --sema` ya da `scripts/db-sorgu.sh "SELECT ..."` (yığın açık olmalı). Yazamaz; hassas içerik şifreli, parola özetleri kapalı.

## Çalıştırma ve test
- Yeni klonda ya da worktree'de bir kez: `python3 -m venv ai_engine/.venv && ai_engine/.venv/bin/pip install -r ai_engine/requirements.txt -r ai_engine/requirements-dev.txt` ve `git config core.hooksPath .githooks`.
- Başlat `docker compose up -d --build`, durdur `docker compose stop`. Önce `docker compose ps`: açıksa olanı kullan, başka oturumun yığınını kapatma; iş bitince kendi açtığını durdur. `down -v` veriyi siler, asla kullanma.
- .NET testleri `dotnet test MeetingWeb.Tests` · Python testleri `ai_engine` içinde `pytest`.

## Doğrulama
- **Bitti = `scripts/dogrula.sh` GEÇTİ** (build + .NET testleri + pytest); rapora GEÇTİ satırını yaz. Pre-commit hook aynı komutu çalıştırır (yalnız doküman değişikliği atlanır), CI (`.github/workflows/test.yml`) aynı testleri her push'ta koşar. `--no-verify` yok.
- Kontrol geçtiyse Ulaş'a "kontrol eder misin" diye sorulmaz, "yapıldı" denir; Ulaş istemediğini `git revert` ile aldırır. Tercih kararları yine sorulur.
- **Test hesabı:** yerel portlarda giriş `scripts/test-giris.sh` ile: şifreyi `.env`'den kendisi okur, oturumu git dışı `.auth/test-oturumu.json`'a kaydeder; ajan Playwright CLI ile `state-load` eder. Oturum yoksa ya da süresi dolduysa betik yeniden çalıştırılır; Ulaş'tan giriş istenmez. Yalnız 127.0.0.1; şifre sohbete, loga, koda yazılmaz. Zoom denemeleri yayına kadar test hesabı + ayrı deneme Zoom hesabıyla; Ulaş'ın kendi hesapları kullanılmaz.
- İnceleme bulgularından düşük olanlar `docs/yol-haritasi.md`'ye "sonra" diye yazılır.

## Kod yerleşimi
- Sayfa mantığı PageModel'e değil servise (`Services/`) yazılır; varlıklar `Models/`, EF `Data/`.
- Saklanan sabitler tek yerde: rol `WorkspaceRole`, madde türü/kaynağı `MeetingPointKind`/`EntrySource`, kaydın kaynağı `MeetingSource`, çerezdeki profil alanı `UserClaimTypes`, oturum anahtarı `ActiveWorkspaceService.SessionKey`.
- Şema ADR-0009; şema değişikliği yeni migration, `migrate` servisi uygular. Ayar varsayılanları `appsettings.json`'da.

## Kurallar
- `docs/raporlar/`, `docs/gunluk/`, `docs/arsiv/` açık repoya girmez; post-commit hook ayarlıysa özel yedeğe alır.
- Sırlar yalnız `.env`'de; koda, compose'a, loglara sır yazılmaz.
- Her iş ayrı dalda, push edilir. Worktree yalnız aynı anda birden çok agent çalışırken (`.env`, `.venv`, ilk derleme ek iş; 8080 yığını ana klasörün kodunu çalıştırır).
- **main'e alma:** Addy `ci-cd-and-automation` "PR Checks" düzeniyle (dal korumalı, CI zorunlu, `gh pr merge --auto --squash`). Tek fark: zorunlu onay yalnız riskli işte (Addy `/build auto` durma listesi); orada PR açılır, Ulaş `/review` çalıştırıp onaylar, veri silen adımdan önce yedek.
- Yetki: kullanıcı yalnız üyesi olduğu workspace'in verisini görür/değiştirir (ADR-0005).
- Mevcut sayfaların görünümü değişmez; yeni öğeler mevcut stil ve bileşenlerle.
- Kullanıcıya görünen metinde teknik terim yok; sade Türkçe.
- Ücretli servis/paket ekleme ve AI sağlayıcısı değiştirme Ulaş'ın onayı olmadan yok (ADR-0001).
- **Kısıtlar (`/spec` bunları `SPEC.md` sınırlarına taşır):** bütçe çok az, ucuz ve sunucuyu yormayan çözüm; yerel model (Whisper/LLM) sunucuya kurulmaz. Toplantı verisi şirket verisidir: hiçbir sağlayıcıyla eğitim için paylaşılmaz, saklanmaz (ZDR); yeni sağlayıcı bu şartla seçilir (ADR-0001). Ses başarılı analizden sonra silinir, döküm şifreli saklanır (ADR-0002).
