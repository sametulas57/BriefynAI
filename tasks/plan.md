# Uygulama planı: Şifremi unuttum

Spec: `SPEC.md` (onaylı, 2026-11-21) · Görev listesi: `tasks/todo.md` · Durum: onaylandı (Ulaş, 2026-11-21)

## Özet

Giriş sayfasından e-postayla süreli, tek kullanımlık şifre yenileme bağlantısı. İş altı göreve bölündü. En riskli kısım uçtan uca zincir: istek → ayrı kuyruktaki iş → gerçek e-posta → bağlantı → yeni şifre. Bu yüzden ilk iki dilim (görev 1-3) bu zinciri kurar ve hemen yerel Docker yığınında gerçek e-postayla denenir. Sınırlar, Türkçe hatalar ve belgeler o deneme geçerse yapılır.

Önceki iş (takvime aktarma) bu dosyalarda yürüyordu. Açık kalan dört maddesi elle deneme ve inceleme: Gmail, Outlook web ve yeni Outlook denemesi, gereksiz klasörü kontrolü, takvim için `/agent-skills:review`. İlk üçü `docs/raporlar/2026-11-20-takvim-daveti-deneme.md`'de ve Backlog 5'te duruyor; eski plan git geçmişinde (`617d900` ve sonrası).

## Kodda baktıklarım (planın dayanağı)

- Identity: `AddDefaultIdentity<ApplicationUser>`, varsayılan token sağlayıcıları kayıtlı, uygulamada `Generate*TokenAsync` çağrısı yok. Giriş `lockoutOnFailure: true`.
- Oturumlar her istekte güvenlik damgasıyla kontrol ediliyor (`ValidationInterval = TimeSpan.Zero`).
- `Microsoft.AspNetCore.Identity.dll` (10.0.8) içinde `TimeProvider` adı geçiyor. Sıfırlama kodunun süresini kontrol eden sağlayıcının onu kullanıp kullanmadığını bilmiyorum; görev 1'de bakılır.
- Hangfire: tek sunucu, `WorkerCount = 1`, kuyruk adı verilmemiş (yalnız `default`). Testlerde Hangfire kurulmuyor; `FakeBackgroundJobClient` işleri ve durumlarını (kuyruk dahil) kaydediyor.
- Hız sınırı: `Program.cs`'teki `GlobalLimiter` eşleşen sayfaya (`RouteValues["page"]`) ve handler'a bakıyor. `OnRejected` mesajı TempData'ya yazıp `Referer`'a ya da `/History`'ye yönlendiriyor; anonim sayfalar için dal yok.
- Bellek içi sayaç örneği: `AskRateLimiter` (singleton, `TimeProvider`, kilitli `Dictionary<string, Queue<DateTimeOffset>>`, içerik değil yalnız kimlik ve zaman tutuyor).
- `ForwardedHeaders` kurulu değil. Uygulama bir tünelin ya da vekil sunucunun arkasına geçerse istek adresi hep aynı görünür (Zoom ucundaki sınır da aynı durumda).
- Test altyapısı: `BriefynFactory` (`EmailServiceOverride`, `Jobs`, `ExtraSettings`), `CapturingLoggerProvider`, `FormHelpers`, `TestAuthHandler`.
- `WorkspaceBoundaryTests` her sayfa handler'ını listede arıyor; `Auth/Login:Get`, `Auth/Login:Post`, `Auth/Register:Post` `NotWorkspaceScoped`.

## Mimari kararlar

1. **`IPasswordResetService` tek giriş noktası.** Üç metot:
   - `RequestAsync(email, baseUrl)`: kullanıcıyı bulur; yoksa hiçbir şey yapmaz. Varsa ve hesap sınırı aşılmadıysa `SendResetEmailAsync(userId, baseUrl)` işini `eposta` kuyruğuna atar. Dönen değer sayfaya hesap hakkında bilgi vermez.
   - `SendResetEmailAsync(userId, baseUrl)`: Hangfire işi. Kodu o anda üretir, bağlantıyı kurar, `IEmailService.SendPasswordResetAsync`'i çağırır. Kullanıcı silinmişse çıkar.
   - `ResetAsync(userId, code, newPassword)`: kodu doğrular, şifreyi değiştirir, kilidi ve yanlış deneme sayacını sıfırlar, "şifreniz değişti" e-postasını kuyruğa atar. Sonuç: başarılı / bağlantı geçersiz / şifre kurala uymuyor (Türkçe hatalarla).
   - `IsLinkValidAsync(userId, code)`: sayfa açılınca bağlantı geçerli mi; geçersizse form hiç gösterilmez.
2. **Bağlantı kökü saf bir kuralla seçilir** (`PasswordResetLink.BaseUrl`): `App:PublicUrl` doluysa o; boşsa yalnız yerel adres (`localhost`, `127.0.0.1`, `[::1]`) için istekteki adres; yoksa `null` → e-posta gitmez, uyarı günlüğe yazılır (adres ve kod yazılmadan). Kök sayfada seçilir ve işe argüman olarak gider; iş istek görmez.
3. **Ayrı sağlayıcı, ayrı ömür.** `DataProtectorTokenProvider<ApplicationUser>`'dan türeyen `PasswordResetTokenProvider`, kendi seçenek sınıfıyla (`TokenLifespan` = `PasswordReset:LinkLifetimeMinutes`, varsayılan 60). `IdentityOptions.Tokens.PasswordResetTokenProvider` buna bağlanır. Varsayılan sağlayıcının ömrü değişmez.
4. **Kod URL'de base64url** (`WebEncoders.Base64UrlEncode`). Sayfa GET'te kodu okur, gizli alana koyar; sayfa `Referrer-Policy: no-referrer` ve `Cache-Control: no-store` gönderir.
5. **E-posta kuyruğu.** Sıfırlama ve bildirim işleri `eposta` kuyruğuna, `IBackgroundJobClient.Enqueue("eposta", ...)` ile. `Program.cs`'e yalnız bu kuyruğu dinleyen, tek işçili ikinci bir `AddHangfireServer` eklenir; mevcut sunucu `default` kuyruğunda kalır. Mevcut e-postalar (özet, gündem, takvim) bu işte taşınmaz.
6. **İki katman sınır.**
   - İstek adresine göre, `GlobalLimiter`'a iki dal: `Auth/ForgotPassword` POST (`PasswordReset:RequestsPerIpPer15Minutes`, 5) ve `Auth/ResetPassword` POST (`PasswordReset:ResetsPerIpPer15Minutes`, 10). `OnRejected` bu iki sayfa için "Çok fazla deneme yaptınız. Lütfen biraz sonra tekrar deneyin." mesajıyla `/Auth/ForgotPassword`'a döner (yeni şifre sayfasında `Referer` yok, kod da formda; dönülecek güvenli tek yer burası).
   - Hesaba göre: `AskRateLimiter` gibi bellek içi `PasswordResetRateLimiter`, `PasswordReset:EmailsPerAccountPerHour` (3). Aşılınca iş kuyruğa girmez, sayfa aynı cevabı verir.
7. **Türkçe şifre hataları.** `IdentityErrorDescriber`'dan türeyen sınıf, yalnız şifre kuralı ve geçersiz kod metinlerini Türkçeleştirir; `AddErrorDescriber` ile bağlanır. Kayıt sayfası da bunu kullanır (onaylandı).
8. **Sayfa mantığı servis içinde.** Sayfa modelleri yalnız formu doğrular, servisi çağırır, sonucu gösterir. Sayfaların görünümü mevcut `Login.cshtml` kartının kopyası (Bootstrap); tasarım yenilemesi ayrı iş.

## Bağımlılık sırası

```
1 bağlantı kökü + sağlayıcı ── 2 istek dilimi (sayfa, servis, kuyruk, e-posta) ── 3 yenileme dilimi (sayfa, servis, bildirim)
                                                                                         │
                                                                              [Kontrol A: Docker'da gerçek e-posta]
                                                                                         │
                                                       4 hız sınırları ── 5 Türkçe hatalar ── [Kontrol B]
                                                                                         │
                                                                              6 belgeler ── [Kontrol C: push, CI]
```

Görev 4 ile 5 birbirinden bağımsız ama ikisi de `Program.cs`'e dokunduğu için sırayla yapılır.

## İş dağılımı

Ulaş'ın kuralına göre uygulama Codex'e (`gpt-6.1-sol`), sayfaların görünüm kodu (iki `.cshtml`) Claude Opus alt ajanına gider. Brife o adımın Addy skill'leri yazılır: `test-driven-development`, `incremental-implementation`, `security-and-hardening`; işçiden önce onları okuması istenir. Her görevden sonra ben diff'i okur, odak testleri ve `scripts/verify.sh`'yi koşar, commit'lerim. Kontrol A'daki ekranda deneme bende.

## Riskler

| Risk | Etki | Önlem |
|---|---|---|
| Sıfırlama kodunun süresi `TimeProvider`'ı kullanmıyor, süre dolumu testte zaman ilerletilerek denenemiyor | Düşük | Görev 1'de bakılır. Kullanmıyorsa test ömrü ayardan çok kısa (negatif) vererek süre dolumunu dener. |
| İkinci Hangfire sunucusu `eposta` kuyruğunu dinlemezse e-postalar hiç gitmez; testler bunu yakalamaz (testte Hangfire yok) | Yüksek | Kontrol A'da gerçek e-postayla denenir; Hangfire panelinde iş ve kuyruk görülür. |
| Hangfire yeniden denemesi işi başka kuyruğa düşürür | Orta | Kontrol A'da SMTP bilerek bozulmaz; Hangfire 1.8'de kuyruk işle birlikte saklanıyor (belgeden, görev 2'de kaynağa bakılır). |
| Tünel ya da vekil sunucu arkasında istek adresi hep aynı → adres sınırı herkesi birlikte kilitler | Orta (yayında) | Bu işte değişmez; ADR-0016'ya ve Backlog 1'e (yayın hazırlığı) `ForwardedHeaders` notu yazılır. |
| Bellek içi hesap sayacı birden çok kopyada işlemez, yeniden başlatmada sıfırlanır | Düşük | Tek kopya çalışıyoruz; ADR-0016'da yazılır. Adres sınırı yine çalışır. |
| Gerçek gönderici adresinden giden sıfırlama e-postası gereksiz klasörüne düşer | Orta | Kontrol A'da hangi klasöre düştüğüne bakılır; düz metin + HTML iki parça gönderilir (mevcut e-postalar gibi). |
| Kayıt sayfasında Identity hatası metni değişiyor | Düşük | Kayıt testleri koşar; metni kontrol eden test varsa güncellenir. |

## Açık sorular

1. **Elle deneme için gelen kutusu** (spec açık soru 3, cevabı gelmedi). Önerim: Kontrol A'da yerel yığında senin seçeceğin bir test hesabı açarım. E-posta o adrese gider; e-postadaki bağlantıyı sen açarsın ya da gelen kutusunu yerleşik tarayıcıda açarsın, girişini sen yaparsın. Mailpit eklemem.
