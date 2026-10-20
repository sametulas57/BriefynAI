# ADR-0010: Şifreleme v3: kolona bağlı, anahtar numaralı

- Durum: Kabul edildi · Tarih: 2026-10-21

## Bağlam
v2 (ADR-0003) AES-256-GCM, ama şifreli değer hiçbir yere bağlı değildi: bir kolonun şifreli metni başka kolona kopyalanırsa da açılıyordu. Anahtar değişimi için biçimde anahtar numarası yoktu. Veri baştan kurulduğu için eski CBC yolu ve `reencrypt` artık gereksiz.

## Karar
- Biçim `v3:<anahtar no>:<Base64(nonce | şifreli | tag)>`, AES-256-GCM; ek doğrulama verisi (AAD) `Tablo.Kolon` (ör. `Meetings.Title`). Başka kolona kopyalanan değer açılmaz, uyarı işareti görünür.
- Anahtar `ENCRYPTION_KEY_V2` (numara 1). Yeni anahtar gelince numarası artar, eskisi okuma için tutulur.
- Satıra bağlama yapılmadı: EF değer dönüştürücüsü satırı görmüyor ve satır numarası ancak kayıttan sonra belli oluyor. Kalan risk (aynı kolonda satırlar arası kopyalama) zaten veritabanına yazma yetkisi gerektiriyor.
- Eski CBC çözme yolu, `reencrypt` komutu ve "düz ya da şifreli" yorum dönüştürücüsü silindi.

## Sonuçlar
17 Ekim 2026 öncesi yedekleri açmak için eski anahtar `.env`'de kalır, ama bu kod onları okuyamaz (eski sürüm gerekir). ADR-0003'ün biçim kısmının yerini aldı.

## Kaynak
iç proje notu
