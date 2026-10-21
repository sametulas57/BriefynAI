# ADR-0011: Zoom bağlantısı (bulut kaydını alma)

- Durum: Kabul edildi · Tarih: 2026-10-21

## Bağlam
Ücretli Zoom kullananlar toplantıyı buluta kaydediyor; Briefyn'e almak için dosyayı elle indirip yüklemeleri gerekiyor. Zoom'un resmi API'si kişinin kendi hesabına izin vermesiyle (OAuth) bulut kayıtlarını okumaya izin veriyor. Ayrıntı: iç proje notu.

## Karar
- Zoom Marketplace'te kullanıcı düzeyinde "General app", yetki kodu akışı; yalnız okuma izinleri (`cloud_recording:read:list_user_recordings`, `cloud_recording:read:list_recording_files`). Briefyn Zoom'daki hiçbir şeyi silmez, değiştirmez.
- Bağlantı kişiye aittir (`ZoomConnections`, kullanıcı başına bir). Erişim ve yenileme anahtarları şifreleme v3 ile saklanır (ADR-0010), loglanmaz. Kesilince Zoom'da iptal edilir ve satır silinir; hesap silinince de.
- Alınan kayıt mevcut hatta girer: ses `IAudioStorage`'a, aynı taslak toplantı ve Hangfire işi (`MeetingSource.Zoom`). Yazıya dökme Groq'ta kalır (ADR-0001 değişmez). Zoom'un dökümü yalnız konuşmacı adı için kullanılır, işten sonra silinir (ADR-0002).
- İlk aşama elle seçerek alma; webhook ve herkese açık adres yok. Otomatik çekme ayrı karar.

## Sonuçlar
- Yeni dış bağımlılık: Zoom API'si. Ücret Briefyn'e değil, kullanıcının Zoom planına bağlı (bulut kaydı Pro ve üstü).
- Uygulama yayınlanana (Zoom beta onayı, en fazla 100 kullanıcı) kadar yalnız geliştiricinin hesabı bağlanabilir.
- `ZOOM_CLIENT_ID` / `ZOOM_CLIENT_SECRET` yalnız `.env`'de (ADR-0004); yoksa Zoom özelliği kapalı görünür.

## Kaynak
iç proje notu
