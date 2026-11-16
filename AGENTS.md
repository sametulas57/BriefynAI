# Briefyn AI

## Önce oku
- `Backlog.md`: sıradaki iş buradan seçilir, biten madde çıkarılır.

## Çalıştırma ve test
- Yeni klonda bir kez `git config core.hooksPath .githooks`; yoksa commit öncesi kontrol çalışmaz.

## Dokunulmayacak yerler
- Kullanıcı yalnız üyesi olduğu workspace'in verisini görür ve değiştirir (ADR-0005).

## Proje kararları
- Her iş ayrı dalda, push edilir. Worktree yalnız aynı anda birden çok ajan çalışırken (8080 yığını ana klasörün kodunu çalıştırır).
- main'e alma Addy `ci-cd-and-automation` "PR Checks" düzeniyle (`gh pr merge --auto --squash`). Riskli işte (Addy `/build auto` durma listesi) PR açılır, Ulaş `/agent-skills:review` çalıştırıp onaylar; veri silen adımdan önce yedek.
- Yeni sayfa mantığı `Services/`'e; sayfalardaki doğrudan veritabanı sorguları örnek alınmaz.
- İnceleme bulgularından düşük olanlar `Backlog.md`'ye "sonra" diye yazılır.
- Kullanıcıya görünen metinde teknik terim yok; sade Türkçe.
- Ücretli servis ya da paket eklemek ve AI sağlayıcısını değiştirmek Ulaş'ın onayıyla (ADR-0001).
- Bütçe çok az: ucuz, sunucuyu yormayan çözüm; yerel model (Whisper/LLM) sunucuya kurulmaz. Toplantı verisi hiçbir sağlayıcıyla eğitim için paylaşılmaz, saklanmaz (ZDR, ADR-0001). Ses başarılı analizden sonra silinir, döküm şifreli saklanır (ADR-0002).
