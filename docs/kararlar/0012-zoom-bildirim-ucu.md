# ADR-0012: Zoom bildirim ucu (otomatik alma)

- Durum: Kabul edildi · Tarih: 2026-10-22

## Bağlam
ADR-0011 Zoom kaydını yalnız elle almaya izin verdi; otomatik çekmeyi ayrı karara bıraktı. Aşama 2'de (iç proje notu) kayıt bitince Zoom'un gönderdiği olayla (webhook) toplantı kendiliğinden alınacak. Bu, uygulamada ilk kez girişsiz, herkese açık bir uç demek. Zoom dokümanı ([Webhooks](https://developers.zoom.us/docs/api/webhooks/), [Marketplace olayları](https://developers.zoom.us/docs/api/marketplace/events/)) 2026-10-22'de yeniden okundu.

## Karar
- **Tek uç:** `POST /Zoom/Webhook`. Girişsiz, çerezsiz, antiforgery yok; kapı Zoom'un imzası. `Zoom:WebhookSecret` (`.env`: `ZOOM_WEBHOOK_SECRET`) yoksa uç 404 ve otomatik alma seçimi görünmez.
- **İmza:** `x-zm-signature` = `v0=` + hex(HMAC-SHA256(gizli anahtar, `v0:{x-zm-request-timestamp}:{ham gövde}`)). Ham baytlar üzerinden hesaplanır (JSON yeniden yazılmaz), sabit zamanlı karşılaştırılır. Zaman damgası saniye; 5 dakikadan eski ya da ileri ise reddedilir (eski isteğin tekrar oynatılması). Gövde en fazla 256 KB. Yanlışsa 401 (Zoom 4xx'i tekrar denemez). Uca yeni bir hız sınırı politikası (IP başına dakikada `Zoom:WebhookLimitPerMinute`).
- **Uç yalnız iş bırakır:** cevap 3 saniye içinde gitmeli; uç içinde Zoom'a ya da başka bir dış servise çağrı yok. `recording.completed` → `Zoom:TranscriptWaitMinutes` (10) sonra çalışacak iş; `recording.transcript_completed` → hemen çalışacak iş. İşe yalnız `host_id` ve toplantı `uuid`'si verilir. `endpoint.url_validation` imzalı cevapla (`plainToken`, `encryptedToken` hex) karşılanır. `app_deauthorized` (`payload.user_id`, `client_id` bizimki değilse yok sayılır) o Zoom kullanıcısının bağlantı satırlarını siler. Bilinmeyen olay 200, iş yok.
- **Olay gövdesine güvenilmez:** iş kaydı kullanıcının kendi anahtarıyla Zoom'dan yeniden sorar; ev sahibi (`host_id`) bağlantıdaki `ZoomUserId` değilse almaz. Olaydaki `download_token` hiçbir yere yazılmaz, işe verilmez, loglanmaz; indirme Aşama 1'deki gibi o an yenilenmiş erişim anahtarıyla.
- **Hedef ekip kişinin seçimi:** `ZoomConnections.AutoImportWorkspaceId` (boş = kapalı; ekip silinince boşalır). Kaydederken ve iş çalışırken üyelik denetlenir (ADR-0005); kişi ekipten çıktıysa seçim kapanır.
- **Tekillik:** `(WorkspaceId, ExternalRecordingId)` eşsiz indeksi; aynı kayıt için ikinci olay, Zoom'un tekrarı, bekleyen iş ya da elle "Al" ikinci toplantı açmaz. Bu yüzden döküm olayı gelince bekleyen işi iptal etmeye gerek yok: sonradan çalışan iş "zaten alındı" der.
- **Hata:** Zoom anahtarı reddederse bağlantı silinir (ADR-0011), iş tekrar denenmez. Geçici ağ hatasında Hangfire'ın kendi yeniden denemesi.

## Sonuçlar
- Yerelde tünel (cloudflared) açık olmalı; kapalıyken gelen olay kaybolabilir (Zoom 3 kez dener, toplam 85 dk). Kaçan kayıt "Zoom kayıtlarım"dan elle alınır.
- Marketplace'te iki yere aynı adres girilir: Event Subscriptions uç adresi ve Information sekmesindeki kaldırma bildirimi adresi.
- Aynı Zoom hesabını iki Briefyn kullanıcısı bağladıysa her biri kendi seçtiği ekibe alır (ikisi de o Zoom hesabına girerek bağladı).

## Kaynak
iç proje notu
