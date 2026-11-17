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
- AI sağlayıcısını değiştirmek Ulaş'ın onayıyla; yerel model sunucuya kurulmaz. Toplantı verisi hiçbir sağlayıcıyla eğitim için paylaşılmaz, saklanmaz.
