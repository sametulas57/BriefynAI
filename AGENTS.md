# Briefyn AI

## Önce oku
- `Backlog.md`: sıradaki iş buradan seçilir, biten madde çıkarılır.

## Çalıştırma ve test
- Yeni klonda bir kez `git config core.hooksPath .githooks`; yoksa commit öncesi kontrol çalışmaz.
- Doğrulama: `scripts/verify.sh` (lint, biçim, tip, build, testler). Commit öncesi hook ve CI aynısını koşar.

## Proje kararları
- Aşama: yayında değil.
- Yeni sayfa mantığı `Services/`'e; sayfalardaki doğrudan veritabanı sorguları örnek alınmaz.
- Kullanıcıya görünen metinde teknik terim yok; sade Türkçe.
- Repo herkese açık: commit ve PR metninde yapay zekâ izi (`Co-Authored-By` satırı, ajan adı) ve kişisel not yok.
- Ücretli servis ya da paket eklemek ve AI sağlayıcısını değiştirmek Ulaş'ın onayıyla.
- Bütçe çok az: ucuz, sunucuyu yormayan çözüm; yerel model sunucuya kurulmaz. Toplantı verisi hiçbir sağlayıcıyla eğitim için paylaşılmaz, saklanmaz.
