# Görevler: Şifremi unuttum

Plan: `tasks/plan.md` · Spec: `SPEC.md`. Odaklı test komutu: `dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~PasswordReset"`. Her görev commit'ten önce `scripts/verify.sh`'den (hook) geçer.

## Aşama 1: İstekten yeni şifreye ilk dilim

- [x] **Görev 1: Bağlantı kökü kuralı ve 1 saatlik sıfırlama sağlayıcısı** (S)
  - Kabul: `PasswordResetLink.BaseUrl` PublicUrl doluysa onu (sondaki `/` silinmiş), boşsa yalnız `localhost` / `127.0.0.1` / `[::1]` için istekteki adresi, öteki durumda `null` veriyor. `PasswordResetTokenProvider` kayıtlı, `IdentityOptions.Tokens.PasswordResetTokenProvider` ona bağlı, ömrü `PasswordReset:LinkLifetimeMinutes` (60); varsayılan sağlayıcının ömrü değişmedi. Sağlayıcının `TimeProvider` kullanıp kullanmadığı yazıldı (plan riski 1).
  - Doğrula: odaklı test (kök kuralının bütün durumları; üretilen kod doğrulanıyor, ömrü geçmiş kod reddediliyor).
  - Bağımlılık: yok.
  - Dosyalar: `MeetingWeb/Services/PasswordResetLink.cs`, `MeetingWeb/Services/PasswordResetTokenProvider.cs`, `MeetingWeb/Program.cs`, `MeetingWeb/appsettings.json`, `MeetingWeb.Tests/Features/PasswordResetTests.cs`.

- [x] **Görev 2: Bağlantı isteme: sayfa, servis, e-posta kuyruğu** (M)
  - Kabul: Girişte "Şifremi unuttum" bağlantısı var; `/Auth/ForgotPassword` formu kayıtlı ve kayıtlı olmayan adres için aynı metni ve durum kodunu gösteriyor. Kayıtlı adreste `eposta` kuyruğunda tek iş var; argümanlarında e-posta adresi ve kod yok. İş koşunca e-posta gidiyor; içindeki bağlantı kökten başlıyor ve geçerli bir kod taşıyor. Host `saldirgan.example` ve PublicUrl boşken iş kuyruğa girmiyor, uyarı adres ve kod içermiyor. Oturum açıkken sayfa ana sayfaya yönlendiriyor. `Program.cs`'te `eposta` kuyruğunu dinleyen tek işçili ikinci Hangfire sunucusu var. İki yeni handler `WorkspaceBoundaryTests`'te.
  - Doğrula: odaklı test; `WorkspaceBoundaryTests`.
  - Bağımlılık: 1.
  - Dosyalar: `MeetingWeb/Services/PasswordResetService.cs`, `MeetingWeb/Services/IEmailService.cs` + `EmailService.cs` (`SendPasswordResetAsync`), `MeetingWeb/Pages/Auth/ForgotPassword.cshtml(.cs)`, `MeetingWeb/Pages/Auth/Login.cshtml`, `MeetingWeb/Program.cs`, testler.

- [x] **Görev 3: Yeni şifre: sayfa, sıfırlama, bildirim** (M)
  - Kabul: Geçerli bağlantıyla `/Auth/ResetPassword` formu açılıyor; geçersiz, bozuk, süresi dolmuş ya da kullanılmış bağlantıda form yerine "Bu bağlantı artık geçerli değil" ve yeni bağlantı isteme yolu var. Kaydedince yeni şifreyle giriş oluyor, eskisiyle olmuyor; aynı bağlantı ve önceden gönderilmiş ikinci bağlantı reddediliyor; önceden açık oturum giriş sayfasına düşüyor; kilitli hesap açılıyor; "şifreniz değişti" e-postası `eposta` kuyruğuna giriyor. Sayfa `Referrer-Policy: no-referrer` ve `Cache-Control: no-store` gönderiyor. Akış boyunca yakalanan günlüklerde kod yok. İki yeni handler `WorkspaceBoundaryTests`'te.
  - Doğrula: odaklı test; `WorkspaceBoundaryTests`.
  - Bağımlılık: 2.
  - Dosyalar: `MeetingWeb/Services/PasswordResetService.cs`, `MeetingWeb/Services/IEmailService.cs` + `EmailService.cs` (`SendPasswordChangedAsync`), `MeetingWeb/Pages/Auth/ResetPassword.cshtml(.cs)`, testler.

### Kontrol A: yerel Docker yığınında gerçek e-posta (devam etmeden önce)
- [x] `scripts/verify.sh` GEÇTİ (`0055b9b`'nin commit öncesi kontrolü, .NET 620/620).
- [x] Web konteyneri yeniden başlatıldı; iki Hangfire sunucusu açıldı, biri yalnız `eposta`'yı dinliyor.
- [x] Ulaş kendi tarayıcısında denedi: e-posta geldi, bağlantı açıldı, yeni şifre kaydedildi, yeni şifreyle girildi, aynı bağlantı ikinci kez reddedildi (Ulaş'ın aktarımı). Hangfire'da 3 sıfırlama ve 2 "şifreniz değişti" işi, beşi de `eposta` sunucusunda `Succeeded`.
- [ ] E-postanın düştüğü klasör yazılmadı.
- [x] Bulunan eksik: mevcut şifre yeni şifre olarak kabul edildi → spec karar 11, Görev 4-5 ile birlikte düzeltildi.
- Not: `docs/raporlar/2026-11-21-sifre-sifirlama-deneme.md`.

## Aşama 2: Kötüye kullanıma karşı sınır ve Türkçe hatalar

- [x] **Görev 4: Hız sınırları** (S)
  - Kabul: Aynı adresten 15 dakikada 6. bağlantı isteği ve 11. yeni şifre kaydı reddediliyor; kullanıcı "Çok fazla deneme yaptınız" mesajıyla `/Auth/ForgotPassword`'a dönüyor. Aynı hesaba saatte 4. istekte iş kuyruğa girmiyor, sayfa aynı cevabı veriyor. Sayılar `PasswordReset` ayarlarından geliyor. Mevcut paylaşım, takvim ve Zoom sınırları değişmedi.
  - Doğrula: odaklı test; mevcut sınır testleri.
  - Bağımlılık: 3.
  - Dosyalar: `MeetingWeb/Program.cs`, `MeetingWeb/Services/PasswordResetRateLimiter.cs`, `MeetingWeb/Services/PasswordResetService.cs`, `MeetingWeb/appsettings.json`, `MeetingWeb.Tests/Security/PasswordResetSecurityTests.cs`.

- [x] **Görev 5: Türkçe şifre hataları** (S)
  - Kabul: Kurala uymayan yeni şifrede hatalar Türkçe ve sade, şifre değişmiyor, bağlantı hâlâ geçerli. Kayıt sayfasında aynı hatalar Türkçe.
  - Doğrula: odaklı test; kayıt testleri.
  - Bağımlılık: 3.
  - Dosyalar: `MeetingWeb/Services/TurkishIdentityErrorDescriber.cs`, `MeetingWeb/Program.cs`, testler.

- [x] **Ek: yeni şifre eskisiyle aynı olamaz** (spec karar 11, Kontrol A'da bulundu): bağlantı doğrulandıktan sonra kontrol ediliyor; testte.

### Kontrol B
- [ ] `scripts/verify.sh` GEÇTİ.
- [ ] Yerel yığında sınır mesajı ve Türkçe hata ekranda görüldü.

## Aşama 3: Belgeler ve yayın

- [x] **Görev 6: Karar kaydı ve deneme notu** (S)
  - Kabul: ADR-0016 (bağlantı kökü ve Host gerekçesi, Identity kodu ve tek kullanım, ayrı kuyruk, bellek sayacının ve vekil sunucu arkasındaki adres sınırının kısıtı). Kontrol A'nın notu `docs/raporlar/`'da. Backlog 1'e `ForwardedHeaders` notu eklendi. `docs/decisions/README.md` dizini güncel.
  - Doğrula: belgeler koddaki adlarla tutarlı (`rg` ile).
  - Bağımlılık: 4, 5.
  - Dosyalar: `docs/decisions/0016-sifre-sifirlama.md`, `docs/decisions/README.md`, `docs/raporlar/2026-11-21-sifre-sifirlama-deneme.md`, `Backlog.md`.

### Kontrol C: bitti
- [ ] Spec başarı ölçütleri 1-9 tek tek işaretlendi.
- [ ] `scripts/verify.sh` GEÇTİ; commit'ler main'e push edildi; CI yeşil.
- [ ] Ulaş'a `/agent-skills:review` hatırlatıldı.
