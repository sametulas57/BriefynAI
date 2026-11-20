# ADR-0016: Şifremi unuttum: Identity'nin sıfırlama kodu, güvenilir adresle bağlantı

- Durum: Kabul edildi · Tarih: 2026-11-21

## Bağlam
Şifresini unutan kullanıcı hesabına dönemiyordu; giriş sayfasındaki bağlantı yorum satırındaydı, şifre sıfırlama kodu yoktu. Yayın öncesi kapatılacak eksik olarak, arayüz yenilemesinden (Backlog 4) önce yapıldı. Kimlik doğrulama ASP.NET Core Identity (`AddDefaultIdentity`); oturum çerezleri her istekte güvenlik damgasıyla kontrol ediliyor (`ValidationInterval = 0`). `AllowedHosts` `*`, yani istekteki Host başlığı süzülmüyor. `App:PublicUrl` henüz boş (Backlog 1). Hangfire'da tek işçi var ve toplantı analizi dakikalar sürebiliyor. Ayrıntı `SPEC.md` ve `tasks/plan.md`.

## Karar
- **Kod:** Identity'nin kendi sıfırlama kodu (`GeneratePasswordResetTokenAsync` / `ResetPasswordAsync`), yalnız sıfırlama için ayrı bir sağlayıcıyla (`PasswordResetTokenProvider`, ömrü `PasswordReset:LinkLifetimeMinutes`, 60). Kod hiçbir yerde saklanmaz: Data Protection ile imzalı ve şifreli, içinde kullanıcı, amaç, zaman ve güvenlik damgası var. Şema değişikliği yok. Varsayılan sağlayıcının ömrü (1 gün) değişmedi.
- **Tek kullanım ve oturumlar:** şifre değişince güvenlik damgası değişir; o kod ve o ana kadar gönderilmiş öteki kodlar geçersiz olur, açık oturumlar bir sonraki istekte kapanır. Çok fazla yanlış denemeden gelen kilit kalkar.
- **Bağlantının adresi:** `App:PublicUrl` doluysa (geçerli bir http/https adresiyse) her zaman o. Boşsa yalnız istek yerel adrese geldiyse (`localhost`, `127.0.0.1`, `::1`) istekteki adres. Öteki durumda e-posta gitmez, uyarı günlüğe yazılır (e-posta adresi ve kod yazılmadan). Gerekçe: `AllowedHosts` `*` olduğu için istekteki adrese güvenmek, sahte Host başlığıyla kurbana saldırganın sitesine giden bağlantı göndermeye (bağlantı zehirlenmesi) yol açar. Karar ortam adına bağlanmadı: yerel Docker yığını da `Development` çalışıyor ve prod compose henüz yok. Kural `PasswordResetLink.BaseUrl`'de, saf ve testli.
- **Hesap varlığı sızmaz:** kayıtlı olan ve olmayan adres aynı cevabı ve aynı yönlendirmeyi alır. E-posta istekte değil Hangfire işinde gider, cevap süresi hesabın varlığını belli etmez. Kuyruğa yalnız kullanıcı kimliği ve bağlantı kökü yazılır; kod iş koşarken üretilir (ADR-0009).
- **Ayrı kuyruk:** sıfırlama ve "şifreniz değiştirildi" e-postaları `eposta` kuyruğuna girer; o kuyruğu tek işçili ikinci bir Hangfire sunucusu dinler. Mevcut özet, gündem ve takvim e-postaları `default`'ta kaldı.
- **Hız sınırı:** istek adresine göre "Şifremi unuttum" gönderimi 15 dakikada 5, yeni şifre kaydı 15 dakikada 10 (eşleşen sayfaya bakar). Hesaba göre saatte en çok 3 sıfırlama e-postası; aşılınca e-posta sessizce gitmez, cevap değişmez. Sayılar `PasswordReset` ayarlarında.
- **Kod sızmaz:** yeni şifre sayfası `Referrer-Policy: no-referrer` ve `Cache-Control: no-store` gönderir; kod formda gizli alanla taşınır; kod, bağlantı ve e-posta adresi uygulama günlüklerine yazılmaz (üretimdeki günlük süzgeciyle testli).
- **Yeni şifre:** Identity'nin şifre kurallarına uymalı (hatalar Türkçe, kayıt sayfası dahil) ve mevcut şifreyle aynı olamaz (Ulaş, elle denemede). Aynılık, bağlantı doğrulandıktan sonra kontrol edilir; geçersiz bağlantıyla şifre tahmini yapılamaz.
- **Bildirim:** şifre değişince kullanıcıya "Şifreniz değiştirildi" e-postası gider.

## Sonuçlar
- Hesap başına sayaç bellekte: uygulama yeniden başlayınca sıfırlanır, birden çok kopyada kopya başına sayar. Birden çok kopyaya geçilirse ortak depoya taşınmalı.
- `ForwardedHeaders` kurulu değil. Uygulama bir tünelin ya da vekil sunucunun arkasına geçince bütün istekler aynı adresten gelir; adres başına sınır herkesi birlikte kilitler (Zoom bildirim ucundaki sınır da). Yayın hazırlığında ele alınmalı (Backlog 1).
- Bağlantıdaki kod tarayıcı geçmişinde kalır; 1 saat ve tek kullanım bunu sınırlar.
- `App:PublicUrl` boşken herkese açık adreste sıfırlama e-postası gitmez; yayında ayar doldurulmalı (Backlog 1'de zaten var).
- Kayıtta e-posta doğrulaması yok; başkasının adresiyle açılmış bir hesaba sıfırlama bağlantısı o adresin sahibine gider. Ayrı iş.
- Yerel Docker yığınında gerçek e-postayla denendi: e-posta geldi, bağlantı çalıştı, yeni şifreyle girildi, ikinci kullanım reddedildi (`docs/raporlar/2026-11-21-sifre-sifirlama-deneme.md`).

## Kaynak
- ASP.NET Core Identity `DataProtectorTokenProvider` (10.0.8): süre `DateTimeOffset.UtcNow` ile kontrol ediliyor, `TimeProvider` kullanılmıyor
- `SPEC.md`, `tasks/plan.md`, `tasks/todo.md` (2026-11-21)
- [ADR-0007: Data Protection anahtarları kalıcı](0007-data-protection-anahtarlari-kalici.md)
- [ADR-0009: Veritabanı şeması v2 (baştan, tek migration)](0009-veritabani-semasi-v2.md)
- [ADR-0012: Zoom bildirim ucu (otomatik alma)](0012-zoom-bildirim-ucu.md)
