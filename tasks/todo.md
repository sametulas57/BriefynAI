# Görevler: Arayüz yenileme 1, ana sayfa ve herkese açık çerçeve

Plan: `tasks/plan.md` · Spec: `SPEC.md` · **Önce oku:** Beyin `knowledge/concepts/briefyn-fireflies-arastirmasi.md` (gerekçeler ve Fireflies ayrıntıları orada).
Odaklı test komutu: `dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~PublicSite"`. Her görev commit'ten önce `scripts/verify.sh`'den (hook) geçer.

## Aşama 1: Temel, önizleme ve adresler

- [ ] **Görev 1: CSS derleme temeli** (M)
  - İlk adım, hiçbir şeye dokunmadan: yerel yığında `/History` ve `/Upload`'ın 1440 ve 390 genişlikte ekran görüntüleri alınır (Görev 11'deki karşılaştırma için), repo dışında saklanır.
  - Kabul: repo kökünde `package.json` + `package-lock.json` (yalnız `tailwindcss`, `@tailwindcss/cli`, `daisyui`); `npm run css:build` `MeetingWeb/Styles/app.css`'ten küçültülmüş `MeetingWeb/wwwroot/css/app.css` üretiyor, içinde geçici `briefyn` teması var. `verify.sh` CSS derlemesini koşuyor; `node_modules` yoksa "bir kez kur: `npm ci`" diyerek duruyor. CI'da Node kuruluyor ve `npm ci` koşuyor. Derlenmiş CSS ve `node_modules` git'e girmiyor. README'de npm adımları var. Derlemenin hook'a eklediği süre yazıldı.
  - Doğrula: `npm ci && npm run css:build`; `scripts/verify.sh`; `git status`'ta `app.css` yok.
  - Bağımlılık: yok.
  - Dosyalar: `package.json`, `package-lock.json`, `MeetingWeb/Styles/app.css`, `scripts/verify.sh`, `.github/workflows/test.yml`, `.gitignore`, `README.md`.

- [ ] **Görev 2: Görsel önizleme: palet, yazı tipi, logo** (S, paralel)
  - Kabul: tek bir HTML önizleme dosyası (`docs/brand/`): "Petrol ve buz"un daha canlı, zemini renkli hâli (koyu tema yok, bembeyaz zemin yok, Fireflies'ın moru ve Inter/DM Sans yok); Türkçe harfli, OFL lisanslı başlık ve metin yazı tipi önerisi; logo A'nın tema renginde SVG'si ve 16 piksel için az öğeli işaret; örnek düğme, menü ve "yakında" kartı. Her metin/zemin çiftinin kontrast oranı yanında yazılı (≥ 4,5:1).
  - Doğrula: dosya tarayıcıda açılıyor; Ulaş'a gönderildi.
  - Bağımlılık: yok. İşçi: Opus alt ajanı (`frontend-design`), Beyin notunun palet turları brifte.
  - Dosyalar: `docs/brand/onizleme-1.html`, `docs/brand/logo-a.svg`, `docs/brand/logo-a-isaret-16.svg`.

- [ ] **Görev 3: Katalog ve "yakında" sayfaları** (M)
  - Kabul: `SiteCatalog` spec'teki sayfa haritasının hepsini tutuyor (bölüm, grup, adres adı, başlık, açıklama, var mı). `/tr/{section}/{slug}`, `/tr/enterprise`, `/tr/pricing` 200 dönüyor; sayfada başlık, açıklama, "Bu sayfa hazırlanıyor", "Kayıt yükle" (`/Upload`), ana sayfa bağlantısı ve `noindex` var. Katalogda olmayan adres 404. Sayfa `_PublicLayout` kullanıyor (en sade hâli: logo yazısı, boş menü yeri, alt bilgi), `bootstrap.min.css` ve dış adres yüklemiyor. Görünen metinde uzun tire yok. Yeni handler `WorkspaceBoundaryTests`'te.
  - Doğrula: odaklı test (katalogdaki her adres döngüyle); `WorkspaceBoundaryTests`.
  - Bağımlılık: 1.
  - Dosyalar: `MeetingWeb/Services/PublicSite/SiteCatalog.cs`, `MeetingWeb/Pages/Public/ComingSoon.cshtml(.cs)`, `MeetingWeb/Pages/Public/_ViewStart.cshtml`, `MeetingWeb/Pages/Shared/_PublicLayout.cshtml`, `MeetingWeb/Program.cs`, `MeetingWeb.Tests/Features/PublicSiteTests.cs`.

- [ ] **Görev 4: `/tr`, `/` yönlendirmesi ve "Kayıt yükle" akışı** (S)
  - Kabul: `/tr` giriş yapmamış ziyaretçiye 200, `<h1>` ve `/Upload` bağlantısı (içerik şimdilik yer tutucu). `/` ve `/Index` 302 ile `/tr`'ye gidiyor; `Pages/Index` yalnız yönlendirme. Giriş yapmamış ziyaretçi `/Upload` açınca `/Auth/Login?ReturnUrl=%2FUpload`'a, girişten sonra `/Upload`'a gidiyor. Giriş yapmış kullanıcı `/tr`'de 200 alıyor ve "Toplantılarım" (`/History`) bağlantısını görüyor. `/History` `app.css` yüklemiyor. Var olan `RedirectToPage("/Index")` akışları (giriş sayfası GET, şifremi unuttum) testte `/tr`'ye varıyor. Kayıttan sonra da geri dönülüyor (plan karar 9): `/Auth/Login?ReturnUrl=%2FUpload`'daki kayıt bağlantısı `/Auth/Register?returnUrl=%2FUpload`'a gidiyor, kayıt bitince `/Upload`'a dönülüyor; `returnUrl` boşsa bugünkü gibi `/Index`; dış adres (`https://saldirgan.example`, `//saldirgan.example`) verilirse dışarı gidilmiyor. Giriş ve kayıt sayfalarının görünümü değişmiyor.
  - Doğrula: odaklı test; `PasswordResetTests`; `IdentityPagesTests`; `WorkspaceBoundaryTests`.
  - Bağımlılık: 3.
  - Dosyalar: `MeetingWeb/Pages/Public/Home.cshtml(.cs)`, `MeetingWeb/Pages/Index.cshtml(.cs)`, `MeetingWeb/Pages/Shared/_PublicLayout.cshtml`, `MeetingWeb/Pages/Auth/Login.cshtml`, `MeetingWeb/Pages/Auth/Register.cshtml(.cs)`, `MeetingWeb.Tests/Features/PublicSiteTests.cs`, `MeetingWeb.Tests/Security/WorkspaceBoundaryTests.cs`.

### Kontrol A: temel ve onay
- [ ] `scripts/verify.sh` GEÇTİ; commit'ler main'e push edildi, CI yeşil (Node adımıyla).
- [ ] Yerel yığında `/`, `/tr`, iki "yakında" sayfası ve 404 tarayıcıda açıldı.
- [ ] **Ulaş önizlemeyi onayladı** (palet, yazı tipi, logo). Onaylanmadan Görev 5'e geçilmez.

## Aşama 2: Çerçevenin görünümü

- [ ] **Görev 5: Tema, yazı tipleri ve logo** (M)
  - İlk adım (plan karar 10): yazı tipinin `.woff2` dosyaları ve `OFL.txt` resmî depodan bir kez indirilir; indirmeden önce Ulaş'a kaynak adres, dosya adları ve boyutları söylenir, onay beklenir. npm paketi eklenmez.
  - Kabul: onaylı değerler `briefyn` temasında; yazı tipi dosyaları ve lisansı `wwwroot/fonts/`'ta, `app.css`'ten yükleniyor; logo SVG'leri ve favicon `wwwroot/img/brand/`'da, `_PublicLayout`'ta kullanılıyor; kaynak PNG'ler `docs/brand/`'da duruyor. Herkese açık sayfalarda dış adrese istek yok (test + tarayıcı ağ listesi).
  - Doğrula: odaklı test (dış adres yok); "yakında" sayfasının 1440 ve 390 genişlikte ekran görüntüsü.
  - Bağımlılık: Kontrol A. İşçi: Opus alt ajanı.
  - Dosyalar: `MeetingWeb/Styles/app.css`, `MeetingWeb/wwwroot/fonts/*`, `MeetingWeb/wwwroot/img/brand/*`, `MeetingWeb/Pages/Shared/_PublicLayout.cshtml`.

- [ ] **Görev 6: Üst menü ve alt bilgi** (M)
  - Kabul: masaüstünde altı başlık (Ürün, Çözümler, Entegrasyonlar, Kaynaklar, Kurumsal, Fiyatlandırma); ilk dördü açılır menü, Ürün dört gruplu; bugün olmayan özellikler "yakında" işaretli; hepsi katalogdan. Klavyeyle açılıp kapanıyor (Esc, Tab sırası). Telefonda menü düğmeyle açılan panel, bütün bağlantılara ulaşılıyor, dokunma alanları ≥ 44 × 44. Alt bilgide menü bağlantıları, Gizlilik (`/Privacy`), İletişim. Giriş yapmışsa "Toplantılarım", yapmamışsa "Giriş yap". Menüdeki her bağlantı 200.
  - Doğrula: odaklı test (menüdeki her `href` 200, katalogla aynı sayı); tarayıcıda 1440 ve 390 genişlikte açma-kapama.
  - Bağımlılık: 5. İşçi: Opus alt ajanı.
  - Dosyalar: `MeetingWeb/Pages/Shared/_PublicNav.cshtml`, `MeetingWeb/Pages/Shared/_PublicFooter.cshtml`, `MeetingWeb/Pages/Shared/_Icons.cshtml`, `MeetingWeb/Pages/Shared/_PublicLayout.cshtml`, `MeetingWeb.Tests/Features/PublicSiteTests.cs`.

### Kontrol B: çerçeve bitti
- [ ] `scripts/verify.sh` GEÇTİ; push, CI yeşil.
- [ ] "Yakında" sayfası ve menü 1440 ve 390 genişlikte ekran görüntüsüyle Ulaş'a gösterildi.
- [ ] `/History` ve `/Upload` Görev 1'deki görüntülerle aynı.

## Aşama 3: Ana sayfa (scroll-craft)

- [ ] **Görev 7: scroll-craft brief'i Ulaş'la** (S, insan adımı)
  - Kabul: `doctor.mjs` koştu, eksikler yazıldı; çalışma klasörü repo dışında (`SCROLLCRAFT_HOME`); `playwright-core` o klasöre kuruldu, repodaki `package.json` değişmedi (plan karar 11). Adım 0'ın 8 sorusu Ulaş'a tek seferde soruldu, cevaplar kendi sözleriyle `BRIEF.md`'de (his eğrisi, zirve = yolculuk bölümü, "Bu, ___ olan site" cümlesi). Adım 1 yolculuğu ve Adım 2 dilbilgisi, parmak izi kapısı, bölüm başına cihaz tablosu yazıldı; spec'in kesin kuralları (uzun tire yok, sayaç yok, uydurma sayı yok, tek zirve, tek imza hareketi, video yok) brifte. Masaüstü ve telefon düzeni ayrı ayrı tarif edildi.
  - Doğrula: `BRIEF.md` Ulaş'a gösterildi, onay aldı.
  - Bağımlılık: Kontrol A (palet). Görev 5-6 ile aynı anda yapılabilir.
  - Dosyalar: repo dışı (`$SCROLLCRAFT_HOME/builds/briefyn/BRIEF.md`).

- [ ] **Görev 8: Görseller** (S)
  - Kabul: brief'teki her bölüm için codex-fleet (Astra) ile durağan görsel; tek stil önsözü her istemde aynı; görselde yazı yok; hepsine bakıldı, kötüler yeniden üretildi; masaüstü ve telefon için ayrı kırpım ve boyut, modern biçimde küçültülmüş. Ücretli servis (kie.ai) kullanılmadı.
  - Doğrula: görsellerin kontak sayfası Ulaş'a gösterildi; toplam boyut yazıldı.
  - Bağımlılık: 7.
  - Dosyalar: `MeetingWeb/wwwroot/img/home/*`.

- [ ] **Görev 9: Açılış, kayan şerit ve yolculuk** (M, en riskli)
  - İlk adım: motor (`scrollcraft.js` / `.css`, değiştirilmeden `wwwroot/lib/scrollcraft/`) `/tr`'de tek boş bölümle çalıştırılıp `shoot.mjs` ile görülür.
  - Kabul: açılışta tek cümlelik vaat, alt cümle, "Kayıt yükle"; sağa sabit hızla kayan şerit (uydurma logo yok); yolculuk bölümü zirve: yükleme → yazıya dökme → konuşmacılar → özet, kararlar, yapılacaklar → arşive sor → takvim ve paylaşma, her adım çalışan bir özelliğe dayanıyor (görev notunda eşleşme). Telefonda yolculuk kendi düzeninde. "Hareketi azalt" açıkken canlandırma yok, metin okunuyor. Dış adres yok, uzun tire yok.
  - Doğrula: odaklı test (`<h1>`, `/Upload` bağlantısı, uzun tire yok, dış adres yok); `shoot.mjs` masaüstü, 390 × 844 ve `--reduced-motion`; `sheet.png` okundu.
  - Bağımlılık: 6, 8. İşçi: Opus alt ajanı (`scroll-craft`).
  - Dosyalar: `MeetingWeb/Pages/Public/Home.cshtml`, `MeetingWeb/wwwroot/js/home.js`, `MeetingWeb/wwwroot/lib/scrollcraft/*`, `MeetingWeb/Styles/app.css`, `MeetingWeb.Tests/Features/PublicSiteTests.cs`.

- [ ] **Görev 10: Kimler için, güven, sık sorulanlar, kapanış** (S)
  - Kabul: beş rol birer cümleyle (katalogdaki Çözümler'den); güven bölümünde yalnız "Toplantılarınız yapay zekâ eğitiminde kullanılmaz." ve "Ses analizden sonra silinir, döküm şifreli saklanır.", "hiç saklanmaz" ve sertifika adı yok; SSS cevapları koddan doğrulanmış (ücretsiz; `.mp3`, `.wav`, `.m4a`, 300 MB `Upload:MaxMegabytes`'tan; Zoom bağlanınca otomatik); kapanışta "Kayıt yükle". Telefonda dizilim kendi düzeninde.
  - Doğrula: odaklı test (iki güven cümlesi birebir var, "hiç saklanmaz" yok, SSS'teki dosya türleri ve sınır ayarla aynı); `shoot.mjs` üç hâl.
  - Bağımlılık: 9. İşçi: Opus alt ajanı.
  - Dosyalar: `MeetingWeb/Pages/Public/Home.cshtml`, `MeetingWeb/Pages/Public/Home.cshtml.cs`, `MeetingWeb.Tests/Features/PublicSiteTests.cs`.

### Kontrol C: ana sayfa çalışıyor
- [ ] `scripts/verify.sh` GEÇTİ; push, CI yeşil.
- [ ] `sheet.png` (masaüstü, telefon, hareket azaltılmış) okundu; his kontrolü `BRIEF.md`'deki eğriyle karşılaştırıldı.

## Aşama 4: Doğrulama ve kapanış

- [ ] **Görev 11: Doğrulama turu ve düzeltmeler** (M)
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
