# Görevler: Arayüz yenileme 1, ana sayfa ve herkese açık çerçeve

Plan: `tasks/plan.md` · Spec: `SPEC.md` · **Önce oku:** Beyin `knowledge/concepts/briefyn-fireflies-arastirmasi.md` (gerekçeler ve Fireflies ayrıntıları orada).
Odaklı test komutu: `dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~PublicSite"`. Her görev commit'ten önce `scripts/verify.sh`'den (hook) geçer.

## Aşama 1: Temel, önizleme ve adresler

- [x] **Görev 1: CSS derleme temeli** (M) · bitti 2026-11-21: önceki ekran görüntüleri `~/scrollcraft/builds/briefyn/once/`; `css:build` hook'a ~0,3 sn ekliyor (üç ölçüm ortalaması 0,33 sn); sürümler tailwindcss 4.3.5, @tailwindcss/cli 4.3.5, daisyui 5.8.2; `@parcel/watcher`'ın kurulum betiği npm 11'de çalışmıyor, watch hazır derlenmiş paketle çalışıyor (denendi).
  - İlk adım, hiçbir şeye dokunmadan: yerel yığında `/History` ve `/Upload`'ın 1440 ve 390 genişlikte ekran görüntüleri alınır (Görev 11'deki karşılaştırma için), repo dışında saklanır.
  - Kabul: repo kökünde `package.json` + `package-lock.json` (yalnız `tailwindcss`, `@tailwindcss/cli`, `daisyui`); `npm run css:build` `MeetingWeb/Styles/app.css`'ten küçültülmüş `MeetingWeb/wwwroot/css/app.css` üretiyor, içinde geçici `briefyn` teması var. `verify.sh` CSS derlemesini koşuyor; `node_modules` yoksa "bir kez kur: `npm ci`" diyerek duruyor. CI'da Node kuruluyor ve `npm ci` koşuyor. Derlenmiş CSS ve `node_modules` git'e girmiyor. README'de npm adımları var. Derlemenin hook'a eklediği süre yazıldı.
  - Doğrula: `npm ci && npm run css:build`; `scripts/verify.sh`; `git status`'ta `app.css` yok.
  - Bağımlılık: yok.
  - Dosyalar: `package.json`, `package-lock.json`, `MeetingWeb/Styles/app.css`, `scripts/verify.sh`, `.github/workflows/test.yml`, `.gitignore`, `README.md`.

- [x] **Görev 2: Görsel önizleme: palet, yazı tipi, logo** (S, paralel) · bitti 2026-11-21: Ulaş'ın ara notuyla ("mat olmasın, parlak renk olsun") parlak palet: ana #00BFD3, zemin #A9EEF5, koyu kardeş #00616E, vurgu orkide #DA5CF0; 25/25 kontrast çifti eşiği geçiyor. Yazı tipleri Bricolage Grotesque (başlık) + Atkinson Hyperlegible Next (metin), ikisi OFL-1.1. Onay Kontrol A'da bekliyor.
  - Kabul: tek bir HTML önizleme dosyası (`docs/brand/`): "Petrol ve buz"un daha canlı, zemini renkli hâli (koyu tema yok, bembeyaz zemin yok, Fireflies'ın moru ve Inter/DM Sans yok); Türkçe harfli, OFL lisanslı başlık ve metin yazı tipi önerisi; logo A'nın tema renginde SVG'si ve 16 piksel için az öğeli işaret; örnek düğme, menü ve "yakında" kartı. Her metin/zemin çiftinin kontrast oranı yanında yazılı (≥ 4,5:1).
  - Doğrula: dosya tarayıcıda açılıyor; Ulaş'a gönderildi.
  - Bağımlılık: yok. İşçi: Opus alt ajanı (`frontend-design`), Beyin notunun palet turları brifte.
  - Dosyalar: `docs/brand/onizleme-1.html`, `docs/brand/logo-a.svg`, `docs/brand/logo-a-isaret-16.svg`.

- [x] **Görev 3: Katalog ve "yakında" sayfaları** (M) · bitti 2026-11-21: 28 kayıt, odaklı testler 98/98. Görünüm dosyaları şimdilik sınıfsız, anlamsal HTML (Codex); görsel kodu plana göre Görev 5-6'da Opus yazar. Katalog cümleleri koddaki özelliklere bakılarak yazıldı (takvim: e-postayla .ics daveti; Google takvim bağlama yok).
  - Kabul: `SiteCatalog` spec'teki sayfa haritasının hepsini tutuyor (bölüm, grup, adres adı, başlık, açıklama, var mı). `/tr/{section}/{slug}`, `/tr/enterprise`, `/tr/pricing` 200 dönüyor; sayfada başlık, açıklama, "Bu sayfa hazırlanıyor", "Kayıt yükle" (`/Upload`), ana sayfa bağlantısı ve `noindex` var. Katalogda olmayan adres 404. Sayfa `_PublicLayout` kullanıyor (en sade hâli: logo yazısı, boş menü yeri, alt bilgi), `bootstrap.min.css` ve dış adres yüklemiyor. Görünen metinde uzun tire yok. Yeni handler `WorkspaceBoundaryTests`'te.
  - Doğrula: odaklı test (katalogdaki her adres döngüyle); `WorkspaceBoundaryTests`.
  - Bağımlılık: 1.
  - Dosyalar: `MeetingWeb/Services/PublicSite/SiteCatalog.cs`, `MeetingWeb/Pages/Public/ComingSoon.cshtml(.cs)`, `MeetingWeb/Pages/Public/_ViewStart.cshtml`, `MeetingWeb/Pages/Shared/_PublicLayout.cshtml`, `MeetingWeb/Program.cs`, `MeetingWeb.Tests/Features/PublicSiteTests.cs`.

- [x] **Görev 4: `/tr`, `/` yönlendirmesi ve "Kayıt yükle" akışı** (S) · bitti 2026-11-21: odaklı testler (PublicSite, WorkspaceBoundary, PasswordReset, IdentityPages, PersonalWorkspace) 236/236; kayıt `Url.IsLocalUrl` ile dış adrese (`https://`, `//`, `/\`) gitmiyor.
  - Kabul: `/tr` giriş yapmamış ziyaretçiye 200, `<h1>` ve `/Upload` bağlantısı (içerik şimdilik yer tutucu). `/` ve `/Index` 302 ile `/tr`'ye gidiyor; `Pages/Index` yalnız yönlendirme. Giriş yapmamış ziyaretçi `/Upload` açınca `/Auth/Login?ReturnUrl=%2FUpload`'a, girişten sonra `/Upload`'a gidiyor. Giriş yapmış kullanıcı `/tr`'de 200 alıyor ve "Toplantılarım" (`/History`) bağlantısını görüyor. `/History` `app.css` yüklemiyor. Var olan `RedirectToPage("/Index")` akışları (giriş sayfası GET, şifremi unuttum) testte `/tr`'ye varıyor. Kayıttan sonra da geri dönülüyor (plan karar 9): `/Auth/Login?ReturnUrl=%2FUpload`'daki kayıt bağlantısı `/Auth/Register?returnUrl=%2FUpload`'a gidiyor, kayıt bitince `/Upload`'a dönülüyor; `returnUrl` boşsa bugünkü gibi `/Index`; dış adres (`https://saldirgan.example`, `//saldirgan.example`) verilirse dışarı gidilmiyor. Giriş ve kayıt sayfalarının görünümü değişmiyor.
  - Doğrula: odaklı test; `PasswordResetTests`; `IdentityPagesTests`; `WorkspaceBoundaryTests`.
  - Bağımlılık: 3.
  - Dosyalar: `MeetingWeb/Pages/Public/Home.cshtml(.cs)`, `MeetingWeb/Pages/Index.cshtml(.cs)`, `MeetingWeb/Pages/Shared/_PublicLayout.cshtml`, `MeetingWeb/Pages/Auth/Login.cshtml`, `MeetingWeb/Pages/Auth/Register.cshtml(.cs)`, `MeetingWeb.Tests/Features/PublicSiteTests.cs`, `MeetingWeb.Tests/Security/WorkspaceBoundaryTests.cs`.

### Kontrol A: temel ve onay
- [x] `scripts/verify.sh` GEÇTİ; commit'ler main'e push edildi, CI yeşil (Node adımıyla). · 2026-11-21: `224b51e`, Tests koşusu başarılı.
- [x] Yerel yığında `/`, `/tr`, iki "yakında" sayfası ve 404 tarayıcıda açıldı. · `/` ve `/Index` 302 → `/tr`; `/tr`, `/tr/product/ask-archive`, `/tr/pricing` 200; `/tr/olmayan`, `/tr/product` 404.
- [x] **Ulaş önizlemeyi onayladı** (palet, yazı tipi, logo). Onaylanmadan Görev 5'e geçilmez. · 2026-11-21: önizleme 1 (düz parlak camgöbeği) onaylanmadı, Ulaş geçişli renk istedi; önizleme 2'den **C, derin su** seçildi (düğme #00D3E6→#1E9BFF, açılış #04121F→#06224A→#0B3A8C, açık zemin #E8FBFD/#BDF0F6). Yazı tipleri Bricolage Grotesque + Atkinson Hyperlegible Next. Koyu zemin için açık renkli logo Görev 5'te.

## Aşama 2: Çerçevenin görünümü

- [x] **Görev 5: Tema, yazı tipleri ve logo** (M) · bitti 2026-11-21: C (derin su) teması, `--grad-*` degradeleri ve `btn-grad`/`bg-grad-*` yardımcıları; yazı tipleri (Ulaş'ın onayıyla tam değişken set, resmî depoların sabit commit'lerinden) `wwwroot/fonts/`'ta; logo, koyu zemin logosu, favicon `wwwroot/img/brand/`'da. Tarayıcıda: yalnız 127.0.0.1:8080 istekleri, iki yazı tipi yüklü, 12 Türkçe harf çiziliyor, 390'da taşma yok, kontrast hepsi eşik üstü (en düşük 5,38). Odaklı testler 88/88.
  - İlk adım (plan karar 10): yazı tipinin `.woff2` dosyaları ve `OFL.txt` resmî depodan bir kez indirilir; indirmeden önce Ulaş'a kaynak adres, dosya adları ve boyutları söylenir, onay beklenir. npm paketi eklenmez.
  - Kabul: onaylı değerler `briefyn` temasında; yazı tipi dosyaları ve lisansı `wwwroot/fonts/`'ta, `app.css`'ten yükleniyor; logo SVG'leri ve favicon `wwwroot/img/brand/`'da, `_PublicLayout`'ta kullanılıyor; kaynak PNG'ler `docs/brand/`'da duruyor. Herkese açık sayfalarda dış adrese istek yok (test + tarayıcı ağ listesi).
  - Doğrula: odaklı test (dış adres yok); "yakında" sayfasının 1440 ve 390 genişlikte ekran görüntüsü.
  - Bağımlılık: Kontrol A. İşçi: Opus alt ajanı.
  - Dosyalar: `MeetingWeb/Styles/app.css`, `MeetingWeb/wwwroot/fonts/*`, `MeetingWeb/wwwroot/img/brand/*`, `MeetingWeb/Pages/Shared/_PublicLayout.cshtml`.

- [x] **Görev 6: Üst menü ve alt bilgi** (M) · bitti 2026-11-21: altı başlık katalogdan (`SiteCatalog.Sections`), `<details>` tabanlı açılır menüler + `public-nav.js` (Esc, dışarı tıklama, Tab ile çıkış, telefonda kilitli panel); betik yokken de bağlantılara ulaşılıyor. 1440/1024/768/390'da taşma yok, telefon dokunma alanları ≥ 44, ağ yalnız 127.0.0.1:8080, kontrast en düşük 5,66. Menü şeridi yapışkan (Görev 9'da açılışla birlikte kontrol edilecek). Odaklı testler 94/94.
  - Kabul: masaüstünde altı başlık (Ürün, Çözümler, Entegrasyonlar, Kaynaklar, Kurumsal, Fiyatlandırma); ilk dördü açılır menü, Ürün dört gruplu; bugün olmayan özellikler "yakında" işaretli; hepsi katalogdan. Klavyeyle açılıp kapanıyor (Esc, Tab sırası). Telefonda menü düğmeyle açılan panel, bütün bağlantılara ulaşılıyor, dokunma alanları ≥ 44 × 44. Alt bilgide menü bağlantıları, Gizlilik (`/Privacy`), İletişim. Giriş yapmışsa "Toplantılarım", yapmamışsa "Giriş yap". Menüdeki her bağlantı 200.
  - Doğrula: odaklı test (menüdeki her `href` 200, katalogla aynı sayı); tarayıcıda 1440 ve 390 genişlikte açma-kapama.
  - Bağımlılık: 5. İşçi: Opus alt ajanı.
  - Dosyalar: `MeetingWeb/Pages/Shared/_PublicNav.cshtml`, `MeetingWeb/Pages/Shared/_PublicFooter.cshtml`, `MeetingWeb/Pages/Shared/_Icons.cshtml`, `MeetingWeb/Pages/Shared/_PublicLayout.cshtml`, `MeetingWeb.Tests/Features/PublicSiteTests.cs`.

### Kontrol B: çerçeve bitti
- [x] `scripts/verify.sh` GEÇTİ; push, CI yeşil. · 2026-11-21, `d4dd9ac`.
- [x] "Yakında" sayfası ve menü 1440 ve 390 genişlikte ekran görüntüsüyle Ulaş'a gösterildi.
- [x] `/History` ve `/Upload` Görev 1'deki görüntülerle aynı. · 1440 ve 390'da dört tam sayfa görüntüsü piksel piksel karşılaştırıldı: farklı piksel 0.

## Aşama 3: Ana sayfa (scroll-craft)

- [x] **Görev 7: scroll-craft brief'i Ulaş'la** (S, insan adımı) · bitti 2026-11-21: Ulaş sekiz soruyu yanıtlamak yerine kararı devretti ("Senin kararın olsun"); brief `~/scrollcraft/builds/briefyn/BRIEF.md` yetkiyle yazıldı ve Ulaş onayladı. İmza hareketi "dalga satıra yatar", ayrı sahneler (video yok). Zoom cümlesi "tek tıkla aktarın" oldu (bildirim anahtarı yalnız sunucuda tanımlıysa kendiliğinden gelir).
  - Kabul: `doctor.mjs` koştu, eksikler yazıldı; çalışma klasörü repo dışında (`SCROLLCRAFT_HOME`); `playwright-core` o klasöre kuruldu, repodaki `package.json` değişmedi (plan karar 11). Adım 0'ın 8 sorusu Ulaş'a tek seferde soruldu, cevaplar kendi sözleriyle `BRIEF.md`'de (his eğrisi, zirve = yolculuk bölümü, "Bu, ___ olan site" cümlesi). Adım 1 yolculuğu ve Adım 2 dilbilgisi, parmak izi kapısı, bölüm başına cihaz tablosu yazıldı; spec'in kesin kuralları (uzun tire yok, sayaç yok, uydurma sayı yok, tek zirve, tek imza hareketi, video yok) brifte. Masaüstü ve telefon düzeni ayrı ayrı tarif edildi.
  - Doğrula: `BRIEF.md` Ulaş'a gösterildi, onay aldı.
  - Bağımlılık: Kontrol A (palet). Görev 5-6 ile aynı anda yapılabilir.
  - Dosyalar: repo dışı (`$SCROLLCRAFT_HOME/builds/briefyn/BRIEF.md`).

- [x] **Görev 8: Görseller** (S) · bitti 2026-11-21: Astra (codex-fleet) ile üç görsel, BRIEF.md'deki tek stil önsözüyle; üçüne de bakıldı, yeniden üretim gerekmedi (yazı/logo/yüz yok, eller doğal). Astra istenenden küçük verdi (1672×941, 941×1672, 1024×1536); teslimler ölçülen boyuttan, büyütme yok: `salon-gece-genis-1600/1440.webp`, `salon-gece-dikey-780.webp` (780×1386), `masa-ustten-960.webp` (4:5), `masa-ustten-780.webp` (3:2). WebP q82, toplam 202 KB (bütçe 600). Ham PNG'ler ve istemler repo dışında `~/scrollcraft/builds/briefyn/out|istemler/`; kontak sayfası `lab/kontak.png`. Ücretli servis kullanılmadı.
  - Kabul: brief'teki her bölüm için codex-fleet (Astra) ile durağan görsel; tek stil önsözü her istemde aynı; görselde yazı yok; hepsine bakıldı, kötüler yeniden üretildi; masaüstü ve telefon için ayrı kırpım ve boyut, modern biçimde küçültülmüş. Ücretli servis (kie.ai) kullanılmadı.
  - Doğrula: görsellerin kontak sayfası Ulaş'a gösterildi; toplam boyut yazıldı.
  - Bağımlılık: 7.
  - Dosyalar: `MeetingWeb/wwwroot/img/home/*`.

- [x] **Görev 9: Açılış, kayan şerit ve yolculuk** (M, en riskli) · bitti 2026-11-21: motor değiştirilmeden `wwwroot/lib/scrollcraft/`'ta (hash skill'le aynı); motor CSS'i `@layer scrollcraft` ile en alt katmanda (düz yüklenince Tailwind'i eziyordu). İmza hareketi "dalga satıra yatar" `home.js`'te. shoot.mjs masaüstü, 390×844, 360×640 ve hareket azaltılmış: ölü kaydırma yok, taşma yok, ağ yalnız 127.0.0.1:8080; hareket azaltılınca motor bağlanmıyor, yolculuk durağan adım listesi. Kontrast en düşük 4,71. Brief'ten sapmalar (parallax yönü, ipucu pencereleri, telefonda %62/%38) görev raporunda. Odaklı testler 105/105. iOS Safari ve Lighthouse Görev 11'de.
  - İlk adım: motor (`scrollcraft.js` / `.css`, değiştirilmeden `wwwroot/lib/scrollcraft/`) `/tr`'de tek boş bölümle çalıştırılıp `shoot.mjs` ile görülür.
  - Kabul: açılışta tek cümlelik vaat, alt cümle, "Kayıt yükle"; sağa sabit hızla kayan şerit (uydurma logo yok); yolculuk bölümü zirve: yükleme → yazıya dökme → konuşmacılar → özet, kararlar, yapılacaklar → arşive sor → takvim ve paylaşma, her adım çalışan bir özelliğe dayanıyor (görev notunda eşleşme). Telefonda yolculuk kendi düzeninde. "Hareketi azalt" açıkken canlandırma yok, metin okunuyor. Dış adres yok, uzun tire yok.
  - Doğrula: odaklı test (`<h1>`, `/Upload` bağlantısı, uzun tire yok, dış adres yok); `shoot.mjs` masaüstü, 390 × 844 ve `--reduced-motion`; `sheet.png` okundu.
  - Bağımlılık: 6, 8. İşçi: Opus alt ajanı (`scroll-craft`).
  - Dosyalar: `MeetingWeb/Pages/Public/Home.cshtml`, `MeetingWeb/wwwroot/js/home.js`, `MeetingWeb/wwwroot/lib/scrollcraft/*`, `MeetingWeb/Styles/app.css`, `MeetingWeb.Tests/Features/PublicSiteTests.cs`.

- [x] **Görev 10: Kimler için, güven, sık sorulanlar, kapanış** (S) · bitti 2026-11-21: roller katalogdan; güven yalnız iki izinli cümle; SSS beş soru (dosya türleri ve MB sınırı `UploadRules` → `Upload:MaxMegabytes`'tan, Zoom "tek tıkla aktarın"); kapanışta işaret çizilerek "Kayıt yükle". shoot.mjs üç hâl: ölü kaydırma yok, taşma yok, ağ yalnız yerel; kontrast en düşük 5,75. FINGERPRINTS.md satırı eklendi. Odaklı testler 112/112. Görev 11'de bakılacak: telefonda yolculuktan çıkarken yalnız başlığın kaldığı kısa an ve kapanış girişindeki boş koyu levha; Zoom cümlesi üretimde Zoom açıksa doğru (`Zoom/Recordings` kapalıyken 404); uzantı listesi `Upload.cshtml.cs` ile `UploadRules`'ta iki yerde (eski sayfaya dokunmak Ulaş'ın onayıyla).
  - Kabul: beş rol birer cümleyle (katalogdaki Çözümler'den); güven bölümünde yalnız "Toplantılarınız yapay zekâ eğitiminde kullanılmaz." ve "Ses analizden sonra silinir, döküm şifreli saklanır.", "hiç saklanmaz" ve sertifika adı yok; SSS cevapları koddan doğrulanmış (ücretsiz; `.mp3`, `.wav`, `.m4a`, 300 MB `Upload:MaxMegabytes`'tan; Zoom bağlanınca otomatik); kapanışta "Kayıt yükle". Telefonda dizilim kendi düzeninde.
  - Doğrula: odaklı test (iki güven cümlesi birebir var, "hiç saklanmaz" yok, SSS'teki dosya türleri ve sınır ayarla aynı); `shoot.mjs` üç hâl.
  - Bağımlılık: 9. İşçi: Opus alt ajanı.
  - Dosyalar: `MeetingWeb/Pages/Public/Home.cshtml`, `MeetingWeb/Pages/Public/Home.cshtml.cs`, `MeetingWeb.Tests/Features/PublicSiteTests.cs`.

### Kontrol C: ana sayfa çalışıyor
- [x] `scripts/verify.sh` GEÇTİ; push, CI yeşil. · 2026-11-21, `6307783`.
- [x] `sheet.png` (masaüstü, telefon, hareket azaltılmış) okundu; his kontrolü `BRIEF.md`'deki eğriyle karşılaştırıldı. · Görev 9 ve 10 raporlarında; iki geçiş anı Görev 11'e not edildi.

## Aşama 4: Doğrulama ve kapanış

- [x] **Görev 11: Doğrulama turu ve düzeltmeler** (M) · bitti 2026-11-21: rapor `docs/raporlar/2026-11-21-arayuz-yenileme-1-dogrulama.md` (yerel; `docs/raporlar/` git dışında). Lighthouse masaüstü 99/100/96/100, mobil 84/100/96/100 (96'nın nedeni dotnet watch betiği). iOS Simulator Safari'de iki hata bulunup düzeltildi (yolculuk levhası kısa kalıp üst üste biniyordu; çıkışta başlık yalnız kalıyordu). Eski sayfalar piksel piksel aynı. Lighthouse plana göre Codex'te değil ana döngüde koştu: Codex'in ağ izni yok, Lighthouse CLI repo dışına kuruldu.
  - Kabul: scroll-craft Adım 5 tam: ölü kaydırma yok, her ipucu tam görünüyor, her bölümde kontrast ≥ 4,5:1 (büyük başlık ≥ 3:1), Tab sırası doğru. iOS Simulator Safari'de `/tr` açılıp kaydırıldı: yana kayma yok, menü paneli çalışıyor, yolculuk okunuyor. Lighthouse (Codex) masaüstü P ≥ 90, E ≥ 95, EU ≥ 95, SEO ≥ 90; mobil P ≥ 80, diğerleri aynı. Tarayıcı ağ listesinde yalnız `127.0.0.1:8080`. `/History` ve `/Upload` Görev 1'deki görüntülerle aynı. Bulunan her sorun düzeltildi ve yeniden çekildi.
  - Doğrula: raporlar ve ekran görüntüleri; doğrulanamayan her şey yazılı.
  - Bağımlılık: Kontrol C.
  - Dosyalar: bulunan sorunlara göre; rapor `docs/raporlar/2026-11-xx-arayuz-yenileme-1-dogrulama.md`.

- [ ] **Görev 12: Belgeler ve Ulaş'ın son onayı** (S)
  - Kabul: ADR-0017 (Tailwind + daisyUI npm ile, `_PublicLayout` ayrımı, `/tr` adresleri ve `Index` yönlendirmesi, katalog). `docs/decisions/README.md` güncel. Backlog 4'ten bu iş çıkarıldı, sıradakiler (arşive sor ekranı, konuşmacılı toplantı sayfası) duruyor; Backlog 1'e "üretim imajı CSS'i derlemeli" notu. Ulaş bitmiş sayfanın masaüstü ve telefon ekran görüntülerini onayladı.
  - Doğrula: belgeler koddaki adlarla tutarlı (`rg`).
  - Bağımlılık: 11.
  - Dosyalar: `docs/decisions/0017-arayuz-temeli.md`, `docs/decisions/README.md`, `Backlog.md`.

### Kontrol D: bitti
- [ ] Spec'in 15 başarı ölçütü tek tek işaretlendi, her birinin dayanağı yazıldı.
- [ ] `scripts/verify.sh` GEÇTİ; push, CI yeşil.
- [ ] Açılan sunucular ve yerel ağ paylaşımı kapatıldı.
- [ ] Ulaş'a hatırlatıldı: `/agent-skills:review arayüz yenileme 1`.
