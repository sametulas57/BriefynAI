# Briefyn AI

## Önce oku
- `Backlog.md`: sıradaki iş buradan seçilir, biten madde çıkarılır.

## Çalıştırma ve test
- Yeni klonda bir kez `git config core.hooksPath .githooks`; yoksa commit öncesi kontrol çalışmaz.

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
- İnceleme bulgularından düşük olanlar `Backlog.md`'ye "sonra" diye yazılır.
- Kullanıcıya görünen metinde teknik terim yok; sade Türkçe.
- Ücretli servis ya da paket eklemek ve AI sağlayıcısını değiştirmek Ulaş'ın onayıyla (ADR-0001).
- Bütçe çok az: ucuz, sunucuyu yormayan çözüm; yerel model (Whisper/LLM) sunucuya kurulmaz. Toplantı verisi hiçbir sağlayıcıyla eğitim için paylaşılmaz, saklanmaz (ZDR, ADR-0001). Ses başarılı analizden sonra silinir, döküm şifreli saklanır (ADR-0002).
