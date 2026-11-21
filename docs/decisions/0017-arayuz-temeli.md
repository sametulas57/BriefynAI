# ADR-0017: Herkese açık sitenin temeli: Tailwind + daisyUI (npm), ayrı çerçeve, `/tr` adresleri, tek katalog

- Durum: Kabul edildi · Tarih: 2026-11-21

## Bağlam
Ana sayfa Bootstrap ile yapılmıştı ve şablon gibi görünüyordu. Amaç, para almadan kullanıcı toplamak için ziyaretçiyi içine çeken bir tanıtım sitesi (Backlog 4, `SPEC.md` arayüz yenileme 1). Kısıtlar:
- uygulama içi sayfalar Bootstrap'ta kalacak ve görünümleri değişmeyecek;
- dış sunucudan dosya (yazı tipi, simge, betik) yüklenmeyecek;
- İngilizce sonra gelecek;
- menüdeki çoğu sayfa henüz yok;
- `RedirectToPage("/Index")` kodda 10 yerde çağrılıyor.

## Karar
- **Stil:** Tailwind CSS v4 ve daisyUI 5, npm ile.
  - `package.json` ve kilit dosyası repo kökünde; sürümler sabit: tailwindcss 4.3.5, @tailwindcss/cli 4.3.5, daisyui 5.8.2.
  - Giriş `MeetingWeb/Styles/app.css`, çıktı `wwwroot/css/app.css`. Çıktı git'e girmez; `scripts/verify.sh`, commit öncesi hook ve CI derler (CI'da Node 24 + `npm ci`).
  - Gerekçe: iki aracın da birinci resmî yolu bu ve sürümler kilitte sabit kalıyor. Bedeli: CI'da Node, geliştirirken ayrıca `npm run css:watch`.
- **Tema:** daisyUI `briefyn` teması, onaylı "C, derin su" yönü (Ulaş, `docs/brand/onizleme-2.html`).
  - Renkler geçişli: degradeler tema dışı CSS değişkenleri (`--grad-*`) ve `btn-grad` / `bg-grad-*` yardımcıları.
  - Sayfa açık zeminli; açılış ve alt bilgi koyu degrade.
  - Degrade yazı yok.
- **Yazı tipleri ve marka:**
  - Bricolage Grotesque (başlık) ve Atkinson Hyperlegible Next (metin), resmî depoların sabit commit'lerinden bir kez indirildi; `wwwroot/fonts/` altında lisanslarıyla (OFL 1.1). npm paketi yok.
  - Logo, koyu zemin logosu ve sekme simgesi `wwwroot/img/brand/` altında SVG.
- **Ayrı çerçeve:** herkese açık sayfalar `Pages/Public/` altında, kendi `_ViewStart`'larıyla `_PublicLayout`'u kullanır.
  - `_PublicLayout` veritabanına gitmez; yalnız oturuma bakar ("Toplantılarım" ya da "Giriş yap").
  - Bootstrap ve `site.css` yüklenmez. Eski sayfalar `_Layout`'ta kalır ve `app.css` yüklemez; Bootstrap ile Tailwind aynı sayfada yoktur.
- **Adresler:**
  - Tanıtım adresleri `/tr/` önekli, sayfa adları İngilizce (`/tr/product/ask-archive`); İngilizce gelince aynı adlar `/en/` altında açılır.
  - `Pages/Index` silinmedi, yalnız `/tr`'ye 302 yönlendirir. Böylece eski `RedirectToPage("/Index")` çağrıları ve bağlantılar dokunulmadan `/tr`'ye varır.
- **Tek katalog:** menü, alt bilgi ve "yakında" sayfaları `Services/PublicSite/SiteCatalog`'dan üretilir (28 kayıt; bölüm, grup, adres, başlık, tek cümle, bugün var mı).
  - Katalogda olmayan adres 404 verir. "Yakında" sayfaları `noindex`.
  - Her ürün cümlesi bugün çalışan bir özelliğe dayanır.
  - Yükleme kuralları (`UploadRules`) ayardan okunur; sayfaya elle sayı yazılmaz.
- **Kayıttan sonra geri dönüş:** girişteki kayıt bağlantısı `returnUrl`'i taşır. Kayıttan sonra yalnız yerel adrese dönülür (`Url.IsLocalUrl`); yoksa eskisi gibi `/Index`.
- **Ana sayfa motoru:** scroll-craft. Motor dosyaları değiştirilmeden `wwwroot/lib/scrollcraft/` altında.
  - Motor CSS'i `@layer scrollcraft` ile en alt katmanda; düz yüklenince Tailwind katmanlarını eziyordu.
  - Sayfaya özel davranış `wwwroot/js/home.js`'te. "Hareketi azalt" açıkken motor bağlanmaz.
  - Ana sayfa brief'i, ham görseller ve ölçüm araçları (`playwright-core`, Lighthouse) repo dışında, scroll-craft çalışma klasöründe.
- **Menü:** `<details>/<summary>` üstüne küçük, kütüphanesiz bir betik (`public-nav.js`); betik yokken de bütün bağlantılara ulaşılır. Simgeler elle yazılmış SVG (`_Icons`).

## Sonuçlar
- Docker imajında Node yok. Yerel geliştirme bağlı klasörle çalışıyor; üretim imajı `app.css`'i derlemeli (Backlog 1).
- `wwwroot/css/app.css` git'te olmadığı için yeni klonda önce `npm ci` ve `npm run css:build` gerekir (README).
- `npm` 11'de `@parcel/watcher`'ın kurulum betiği çalışmıyor; `css:watch` hazır derlenmiş paketle çalışıyor (denendi).
- Dosya uzantıları iki yerde: `Pages/Upload.cshtml.cs` ve `UploadRules`. Eski sayfaya dokunmamak için birleştirilmedi.
- Zoom cümleleri ("tek tıkla aktarın") üretimde Zoom ayarı açıksa doğru. "Kendiliğinden gelir" denmedi, çünkü bildirim anahtarı yalnız sunucuda tanımlıysa doğru.
- `_PublicLayout`'ta tek satırlık satır içi betik var (`html.js` sınıfı); içerik güvenlik politikası (CSP) eklenirse izin gerekir ya da dosyaya taşınmalı.
- Doğrulama (yerel, geliştirme ortamı):
  - Lighthouse masaüstü 99/100/96/100, mobil 84/100/96/100;
  - iOS Simulator Safari'de iki hata bulunup düzeltildi;
  - `/History` ve `/Upload` önceki görüntüleriyle piksel piksel aynı.
  Ayrıntı `docs/raporlar/2026-11-21-arayuz-yenileme-1-dogrulama.md` (yerel).

## Kaynak
- `SPEC.md`, `tasks/plan.md`, `tasks/todo.md` (2026-11-21)
- `docs/brand/onizleme-2.html` (onaylı palet, Yön C)
- Tailwind CSS v4 ve daisyUI 5 belgeleri (kurulum: npm ve CLI; tema: `@plugin "daisyui/theme"`)
- [ADR-0002: Ses analizden sonra silinir, döküm şifreli saklanır](0002-ses-analizden-sonra-silinir-dokum-sifreli-saklanir.md) (güven bölümündeki "döküm şifreli saklanır" sözünün dayanağı)
