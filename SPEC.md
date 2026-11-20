# Spec: Şifremi unuttum

Durum: onaylandı (Ulaş, 2026-11-21) · plan `tasks/plan.md`, görevler `tasks/todo.md`

## Amaç

Şifresini unutan kullanıcı bugün hesabına dönemiyor: giriş sayfasındaki "Şifremi Unuttum?" bağlantısı yorum satırında (`Pages/Auth/Login.cshtml` 47-48), uygulamada şifre sıfırlama kodu yok. Yayın öncesi kapatılması gereken bir eksik; arayüz yenilemesinden (Backlog 4) önce yapılıyor ki yeni sayfa tasarıma baştan girsin.

### Kullanıcı hikâyeleri

1. Ayşe şifresini unutmuş. Giriş sayfasında "Şifremi unuttum"a tıklıyor, e-postasını yazıyor. Ekranda "Bu adrese kayıtlı bir hesap varsa şifre yenileme bağlantısı gönderdik" yazıyor.
2. Ayşe'ye bir e-posta geliyor; içindeki bağlantı 1 saat geçerli. Bağlantıyı açıyor, yeni şifresini iki kez yazıyor, kaydediyor. Giriş sayfasına dönüyor, "Şifreniz değişti, yeni şifrenizle giriş yapın" yazıyor; yeni şifreyle giriyor.
3. Ayşe aynı bağlantıyı ikinci kez açıyor. "Bu bağlantı artık geçerli değil" yazıyor, yeni bağlantı isteme sayfasına yönlendiriliyor.
4. Ayşe'nin telefonunda Briefyn açık kalmış. Şifre değişince o oturum da kapanıyor; telefonda yeni şifreyle girmesi gerekiyor.
5. Biri Briefyn'de kayıtlı olmayan bir adres yazıyor. Ayşe'ninkiyle aynı ekranı görüyor, e-posta gitmiyor. Adresin kayıtlı olup olmadığını cevaptan anlayamıyor.
6. Biri Ayşe'nin adresi için art arda bağlantı istiyor. Birkaç istekten sonra Ayşe'ye yeni e-posta gitmiyor; isteyen kişi yine aynı ekranı görüyor.

### Bakılan durum (2026-11-20)

- Kimlik doğrulama ASP.NET Core Identity: `AddDefaultIdentity<ApplicationUser>` (`Program.cs`), giriş `SignInManager.PasswordSignInAsync`, `lockoutOnFailure: true`. Varsayılan token sağlayıcıları kayıtlı geliyor; uygulamada hiçbir `Generate*TokenAsync` çağrısı yok.
- Oturum çerezleri her istekte güvenlik damgasıyla kontrol ediliyor (`SecurityStampValidatorOptions.ValidationInterval = TimeSpan.Zero`); hesap silme isteği bunu zaten kullanıyor.
- Data Protection anahtarları kalıcı (`App_Data/keys`, ADR-0007), uygulama adı `Briefyn`.
- E-posta: `EmailService` (SMTP, `EmailSettings`). E-postalar Hangfire işiyle gidiyor; kuyruğa adres düz yazılmıyor (ADR-0009).
- Hangfire'da tek işçi var (`WorkerCount = 1`); toplantı analizi de aynı işçide koşuyor ve dakikalar sürebiliyor.
- `AllowedHosts: "*"`: istekteki Host başlığı süzülmüyor.
- `App:PublicUrl` yerelde ve `appsettings.json`'da boş; yerel Docker yığını `Development` ortamında, `localhost:8080`'de çalışıyor.
- Hız sınırı: `Program.cs`'teki `GlobalLimiter`, eşleşen sayfaya ve handler'a bakıyor (paylaşım, takvim, Zoom). Reddedilince `OnRejected` kullanıcıyı mesajla geri gönderiyor.
- `MeetingWeb.Tests/Security/WorkspaceBoundaryTests.cs` her sayfa handler'ının listede olmasını istiyor; `Auth/Login` ve `Auth/Register` orada `NotWorkspaceScoped`.
- Uygulamada Türkçe Identity hata metni (`IdentityErrorDescriber`) yok; kayıt sayfası Identity'nin şifre kuralı hatalarını olduğu gibi (İngilizce) gösteriyor.

### Önerilen kararlar (onayda düzeltilebilir)

1. **Token: Identity'nin kendi sıfırlama kodu, şema değişikliği yok.** `UserManager.GeneratePasswordResetTokenAsync` / `ResetPasswordAsync`. Kod veritabanına hiç yazılmaz; Data Protection ile imzalı ve şifreli, içinde kullanıcı kimliği, amaç, zaman ve güvenlik damgası var.
   - **Süreli:** yalnız sıfırlama için ayrı bir sağlayıcı kaydedilir, ömrü **1 saat**. Varsayılan sağlayıcının ömrüne (1 gün) dokunulmaz.
   - **Tek kullanımlık:** şifre değişince güvenlik damgası değişir; aynı kod (ve o ana kadar gönderilmiş öteki kodlar) geçersiz olur.
   - **Açık oturumlar:** damga değiştiği için öteki cihazlardaki çerezler bir sonraki istekte reddedilir (mevcut ayar).
   - Sonuç: tablo ya da migration gerekmiyor.
2. **Bağlantının adresi: `App:PublicUrl`; boşsa yalnız yerel adres.**
   - `App:PublicUrl` doluysa bağlantı her zaman ondan kurulur; istekteki Host başlığına bakılmaz.
   - Boşsa ve istek yerel bir adrese geldiyse (`localhost`, `127.0.0.1`, `::1`) bağlantı istekteki adresle kurulur. Yerel Docker denemesi bununla çalışır.
   - Boşsa ve istek başka bir adrese geldiyse e-posta gönderilmez, uyarı günlüğe yazılır (adres ve kod yazılmadan); kullanıcı yine aynı ekranı görür.
   - Gerekçe: `AllowedHosts` `*` olduğu için istekteki adrese güvenmek, saldırganın sahte Host başlığıyla kurbana kendi sitesine giden bir bağlantı göndermesine (bağlantı zehirlenmesi) yol açar. En kötü ihtimalle bağlantı kurbanın kendi bilgisayarını gösterir, kod dışarı sızmaz. Ortam adına (`Development`) bağlamıyorum, çünkü yerel yığın da `Development` ve prod compose henüz yok; yanlış ortamla yayına çıkmak koruma açığı olmamalı.
3. **E-posta arka planda ve ayrı kuyrukta gider.**
   - İstek yalnız kullanıcıyı arar ve bulursa iş kuyruğa atar; kayıtlı olan ve olmayan adres için cevap süresi yakın kalır (zamanlamadan hesap varlığı anlaşılmaz).
   - Kuyruğa yalnız kullanıcı kimliği ve bağlantının adres kökü gider; e-posta adresi ve sıfırlama kodu Hangfire'a yazılmaz. Kod, iş koşarken üretilir.
   - Tek işçi analizle meşgulken sıfırlama e-postası dakikalarca beklemesin diye e-postalar için ayrı bir kuyruk ve tek işçili ikinci bir Hangfire sunucusu açılır (yeni paket yok).
4. **Hız sınırı iki katman.**
   - İstek adresine göre: "Şifremi unuttum" gönderimi 15 dakikada 5, yeni şifre kaydı 15 dakikada 10. Aşılınca kullanıcı "Çok fazla deneme yaptınız, biraz sonra tekrar deneyin" mesajıyla aynı sayfaya döner.
   - Hesaba göre: bir hesaba saatte en çok 3 sıfırlama e-postası. Aşılırsa e-posta sessizce gitmez, cevap değişmez. Sayaç bellekte tutulur (uygulama tek kopya çalışıyor; birden çok kopyaya geçilirse ortak depoya taşınmalı, ADR'ye yazılır).
   - Sayılar `appsettings.json`'dan değiştirilebilir.
5. **Kod adres çubuğundan sayfaya, oradan dışarı sızmaz.**
   - Yeni şifre sayfası `Referrer-Policy: no-referrer` ve `Cache-Control: no-store` gönderir; kod formda gizli alanla taşınır.
   - Kod ve bağlantı hiçbir günlüğe yazılmaz; testte günlükler yakalanıp kodun geçmediği kontrol edilir.
6. **Sıfırlamadan sonra:** kullanıcı otomatik giriş yapmaz, giriş sayfasına yönlenir. Hesap çok fazla yanlış denemeden kilitliyse kilit kalkar ve yanlış deneme sayacı sıfırlanır (bağlantıya ulaşan kişi e-postanın sahibi).
7. **Şifre değişti bildirimi:** sıfırlamadan sonra kullanıcıya kısa bir "Şifreniz değiştirildi; bunu siz yapmadıysanız bize yazın" e-postası gider. Hesabı ele geçirilen kişi fark etsin diye.
8. **Türkçe şifre hataları:** Identity'nin şifre kuralı hataları (rakam, büyük harf, uzunluk...) için Türkçe metin eklenir. Kayıt sayfası da bundan yararlanır; bugün oradaki bu hatalar İngilizce.
9. **Oturum açıkken:** "Şifremi unuttum" sayfası giriş sayfası gibi ana sayfaya yönlendirir. Yeni şifre sayfası oturumdan bağımsız çalışır (bağlantıdaki koda bakar).
10. **Silme isteği bekleyen hesap:** özel işlem yok. Sıfırlama çalışır; kullanıcı sonra giriş yapınca mevcut kural silme isteğini iptal eder.

## Teknoloji

Mevcut yığın, yeni paket yok: ASP.NET Core Razor Pages (`net10.0`), ASP.NET Core Identity, EF Core 10 + SQL Server, Hangfire 1.8, `System.Net.Mail` (`EmailService`), `Microsoft.AspNetCore.RateLimiting`, `IMemoryCache`.

## Komutlar

```
Doğrulama (lint, biçim, tip, build, testler):  scripts/verify.sh
Yalnız bu işin testleri:                       dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~PasswordReset"
Yerelde çalıştırma:                            docker compose up -d --build
```

Commit öncesi hook ve CI `scripts/verify.sh`'yi koşar. Migration yok.

## Proje yapısı

```
MeetingWeb/Services/PasswordResetService.cs        → istek (kullanıcıyı bul, hesap sınırı, kuyruğa at), e-posta işi (kod üret, gönder), sıfırlama (kodu doğrula, şifreyi değiştir, kilidi kaldır, bildirim)
MeetingWeb/Services/PasswordResetLink.cs           → bağlantı kökünü seçen saf kural (PublicUrl / yerel adres / yok); birim test edilir
MeetingWeb/Services/PasswordResetTokenProvider.cs  → 1 saatlik ayrı sağlayıcı
MeetingWeb/Services/TurkishIdentityErrorDescriber.cs → şifre kuralı hataları Türkçe
MeetingWeb/Services/IEmailService.cs, EmailService.cs → SendPasswordResetAsync, SendPasswordChangedAsync (düz metin + HTML)
MeetingWeb/Pages/Auth/ForgotPassword.cshtml(.cs)   → e-posta formu ve "gönderdik" ekranı
MeetingWeb/Pages/Auth/ResetPassword.cshtml(.cs)    → yeni şifre formu; geçersiz bağlantı ekranı
MeetingWeb/Pages/Auth/Login.cshtml                 → "Şifremi unuttum" bağlantısı açılır
MeetingWeb/Program.cs                              → sağlayıcı, Türkçe hatalar, e-posta kuyruğu sunucusu, iki hız sınırı
MeetingWeb/appsettings.json                        → PasswordReset: sınır sayıları
MeetingWeb.Tests/Features/PasswordResetTests.cs    → akış testleri
MeetingWeb.Tests/Security/PasswordResetSecurityTests.cs → sızma, sınır, zehirlenme testleri
MeetingWeb.Tests/Security/WorkspaceBoundaryTests.cs → dört yeni handler NotWorkspaceScoped olarak eklenir
docs/decisions/0016-sifre-sifirlama.md             → karar kaydı (bağlantı adresi, token, kuyruk, bellek sayacının sınırı)
```

Dosya adları plana göre değişebilir; ilke değişmez: mantık `Services/`'te, sayfalar yalnız servisi çağırır.

### Kod biçimi

Mevcut düzen: sayfa modeli ince, kural servis içinde; saf karar ayrı ve statik, testler doğrudan çağırır.

```csharp
public static class PasswordResetLink
{
    // ADR-0016: the request's Host header is never trusted for a public link; it is only used when it is loopback.
    public static string? BaseUrl(string? publicUrl, HttpRequest request)
    {
        if (!string.IsNullOrWhiteSpace(publicUrl)) return publicUrl.TrimEnd('/');
        return IsLoopback(request.Host.Host) ? $"{request.Scheme}://{request.Host}" : null;
    }
}
```

Kod ve yorumlar İngilizce; kullanıcıya görünen her metin sade Türkçe, teknik terim yok ("bağlantı", "şifre"; "token", "link" değil).

## Test stratejisi

xUnit, `MeetingWeb.Tests`, mevcut `BriefynFactory` (`EmailServiceOverride` ile e-posta yakalanır, `FakeBackgroundJobClient` ile kuyruk görülür, `CapturingLoggerProvider` ile günlükler okunur). Önce kötüye kullanım testi, sonra kod.

**Birim:**
- `PasswordResetLink.BaseUrl`: PublicUrl dolu → o, sondaki `/` silinmiş; boş + `localhost` / `127.0.0.1` / `[::1]` → istek adresi; boş + `saldirgan.example` → yok.

**Akış:**
- Kayıtlı adres → aynı ekran, bir e-posta işi kuyrukta; iş koşunca e-posta gidiyor, bağlantı kurulabilir.
- Bağlantıyla yeni şifre → kaydediliyor; yeni şifreyle giriş oluyor, eski şifreyle olmuyor.
- Aynı bağlantı ikinci kez → reddediliyor. Aynı kullanıcıya önceden gönderilmiş ikinci bağlantı da reddediliyor.
- 1 saatten eski kod reddediliyor. Zamanın testte nasıl ilerletileceği planda seçilir (Identity'nin sağlayıcısı `TimeProvider` kullanıyor mu, bakmadım).
- Şifre değişmeden önce açılmış oturum, değiştikten sonra giriş sayfasına düşüyor.
- Kilitli hesap sıfırlamadan sonra giriş yapabiliyor.
- Sıfırlamadan sonra "şifreniz değişti" e-postası gidiyor.
- Kurala uymayan yeni şifre → Türkçe hata, şifre değişmiyor, kod hâlâ geçerli.

**Güvenlik:**
- Kayıtlı olmayan adres → kayıtlı adresle aynı sayfa metni ve durum kodu; kuyrukta iş yok.
- Hangfire'a giden iş argümanlarında e-posta adresi ve kod yok.
- Akış boyunca yakalanan hiçbir günlük satırında kod geçmiyor.
- Host başlığı `saldirgan.example` ve PublicUrl boş → e-posta gitmiyor. PublicUrl dolu → bağlantı PublicUrl'le başlıyor, Host başlığından bağımsız.
- Adres başına sınır aşılınca istek reddediliyor; hesap başına sınır aşılınca e-posta gitmiyor ama cevap aynı.
- Yeni şifre sayfası `Referrer-Policy: no-referrer` ve `Cache-Control: no-store` gönderiyor.
- Bozuk, başka kullanıcının kimliğiyle eşleştirilmiş ya da boş kod reddediliyor.
- Dört yeni handler `WorkspaceBoundaryTests` listesinde (yoksa test kırılır).

**Elle (yerel Docker yığını, ekranda):** giriş sayfasından bağlantı istenir; e-posta gelir; bağlantı açılır, yeni şifre kaydedilir, yeni şifreyle girilir; aynı bağlantı ikinci kez açılınca reddedilir. Kısa not `docs/raporlar/`'a.

## Sınırlar

- **Her zaman:** `scripts/verify.sh` geçmeden commit yok; yeni handler `WorkspaceBoundaryTests`'e eklenir; e-posta adresi ve kod Hangfire'a ve günlüğe yazılmaz; kayıtlı olmayan adres için cevap aynı; kullanıcıya görünen metin sade Türkçe.
- **Önce sor:** şema değişikliği (bu planda yok); yeni paket ya da ücretli servis (e-posta yakalayıcı dahil); e-posta sağlayıcısını ya da gönderici adresini değiştirmek; `AllowedHosts`'u değiştirmek; giriş ya da kayıt akışının sıfırlama dışındaki davranışını değiştirmek.
- **Asla:** sıfırlama kodunu veritabanına, Hangfire'a ya da günlüğe yazmak; istekteki Host başlığına yerel olmayan bir adreste güvenmek; cevapla ya da hata metniyle hesabın var olup olmadığını belli etmek; Identity'nin kapalı `/Identity/Account/ForgotPassword` sayfalarını açmak.

## Başarı ölçütleri

1. Giriş sayfasında "Şifremi unuttum" bağlantısı var; e-posta formu çalışıyor.
2. Kayıtlı adrese 1 saat geçerli bir bağlantı e-postayla gidiyor; kayıtlı olmayan adres için ekran ve durum kodu aynı, e-posta gitmiyor.
3. Bağlantıyla yeni şifre kaydediliyor; yeni şifreyle giriş oluyor, eskisiyle olmuyor.
4. Aynı bağlantı ikinci kez, süresi dolmuşsa ya da bozuksa reddediliyor; şifre değişince eski bağlantılar ve açık oturumlar geçersiz.
5. Adres başına ve hesap başına sınır çalışıyor.
6. Sıfırlama kodu veritabanında, Hangfire'da ve günlüklerde yok (testle).
7. Sahte Host başlığı bağlantıyı başka adrese çeviremiyor (testle).
8. Yerel Docker yığınında ekranda denendi: e-posta geldi, bağlantı çalıştı, ikinci kullanımda reddedildi; not `docs/raporlar/`'da.
9. `scripts/verify.sh` GEÇTİ; commit'ler main'de, CI yeşil; ADR-0016 yazıldı.

## Açık sorular

1. **Önerilen kararlar 1-10** (yukarıda) uygun mu? Özellikle: bağlantı ömrü 1 saat, sınır sayıları (adres başına 15 dakikada 5 / 10, hesap başına saatte 3), şifre değişti bildirimi (7) ve kayıt sayfasını da etkileyen Türkçe hata metinleri (8).
2. **E-postalar için ikinci Hangfire sunucusu (karar 3)** kabul mü? Alternatif: mevcut sunucunun işçi sayısını 2'ye çıkarmak; ama o zaman iki toplantı analizi aynı anda koşabilir, maliyet ve bellek artar.
3. **Elle deneme hangi gelen kutusuyla?** `.env`'deki gerçek gönderici adresinden, senin bir test adresine e-posta gidecek. Bağlantıyı açmak için ya o kutuyu yerleşik tarayıcıda açabileceğim bir adres söylersin (girişi sen yaparsın), ya da e-postadaki bağlantıyı sen açarsın. Yerel bir e-posta yakalayıcı (ör. Mailpit konteyneri) ücretsiz ama compose'a yeni bir servis demek; istemezsen eklemem.
4. **`SPEC.md` değişimi:** takvim spec'inin açık kalan maddeleri (Gmail/Google Takvim denemesi, takvim için `/agent-skills:review`) Backlog 5'te ve git geçmişinde duruyor; bu spec onu `SPEC.md`'de yerinden ediyor. `tasks/plan.md` ve `tasks/todo.md` plan aşamasında bu işe göre yeniden yazılır.
