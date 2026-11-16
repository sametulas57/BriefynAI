# Briefyn AI

## Önce oku
- `docs/yol-haritasi.md`: backlog; sıradaki iş buradan seçilir, biten madde çıkarılır.
- `docs/sabitler.md`: port, ad, yol gibi sabit değerler.

## Çalıştırma ve test
- Yeni klonda ya da worktree'de bir kez: `python3 -m venv ai_engine/.venv && ai_engine/.venv/bin/pip install -r ai_engine/requirements.txt -r ai_engine/requirements-dev.txt` ve `git config core.hooksPath .githooks`.
- Başlat `docker compose up -d --build`, durdur `docker compose stop`; önce `docker compose ps`, açıksa olanı kullan. `down -v` veriyi siler, asla kullanma.
- Bitti = `scripts/dogrula.sh` GEÇTİ (build + .NET testleri + pytest). Pre-commit hook ve CI aynısını koşar.
- Veritabanına salt okunur bakış: `scripts/db-sorgu.sh --sema` ya da `scripts/db-sorgu.sh "SELECT ..."` (yığın açık olmalı).
- Yerelde giriş `scripts/test-giris.sh` ile: şifreyi `.env`'den okur, oturumu `.auth/test-oturumu.json`'a yazar, Playwright CLI `state-load` eder; süresi dolunca yeniden çalıştırılır. Yalnız 127.0.0.1; şifre sohbete, loga, koda yazılmaz. Zoom denemeleri test hesabı ve ayrı deneme Zoom hesabıyla, Ulaş'ın kendi hesaplarıyla değil.

## Kod yerleşimi
- Sayfa mantığı PageModel'de değil `Services/`'te; varlıklar `Models/`, EF `Data/`.
- Saklanan sabitler tek yerde: rol `WorkspaceRole`, madde türü/kaynağı `MeetingPointKind`/`EntrySource`, kaydın kaynağı `MeetingSource`, çerezdeki profil alanı `UserClaimTypes`, oturum anahtarı `ActiveWorkspaceService.SessionKey`.
- Şema ADR-0009; şema değişikliği yeni migration, `migrate` servisi uygular. Ayar varsayılanları `appsettings.json`'da.

## Dokunulmayacak yerler
- `docs/raporlar/`, `docs/gunluk/`, `docs/arsiv/` açık repoya girmez.
- Sırlar yalnız `.env`'de; koda, compose'a, loglara sır yazılmaz.
- Kullanıcı yalnız üyesi olduğu workspace'in verisini görür ve değiştirir (ADR-0005).
- Mevcut sayfaların görünümü değişmez; yeni öğeler mevcut stil ve bileşenlerle.

## Proje kararları
- Her iş ayrı dalda, push edilir. Worktree yalnız aynı anda birden çok ajan çalışırken (8080 yığını ana klasörün kodunu çalıştırır).
- main'e alma Addy `ci-cd-and-automation` "PR Checks" düzeniyle (`gh pr merge --auto --squash`). Riskli işte (Addy `/build auto` durma listesi) PR açılır, Ulaş `/agent-skills:review` çalıştırıp onaylar; veri silen adımdan önce yedek.
- İnceleme bulgularından düşük olanlar `docs/yol-haritasi.md`'ye "sonra" diye yazılır.
- Kullanıcıya görünen metinde teknik terim yok; sade Türkçe.
- Ücretli servis ya da paket eklemek ve AI sağlayıcısını değiştirmek Ulaş'ın onayıyla (ADR-0001).
- Bütçe çok az: ucuz, sunucuyu yormayan çözüm; yerel model (Whisper/LLM) sunucuya kurulmaz. Toplantı verisi hiçbir sağlayıcıyla eğitim için paylaşılmaz, saklanmaz (ZDR, ADR-0001). Ses başarılı analizden sonra silinir, döküm şifreli saklanır (ADR-0002).
