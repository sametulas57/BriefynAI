# Spec: Arayüz yenileme 1: ana sayfa ve herkese açık çerçeve (masaüstü ve mobil)

Durum: onaylandı (Ulaş, 2026-11-21) · plan `tasks/plan.md`, görevler `tasks/todo.md` (onaylı, 2026-11-21) · Backlog 4 · araştırma notu Beyin `knowledge/concepts/briefyn-fireflies-arastirmasi.md`

## Amaç

Bugünkü ana sayfa Bootstrap ile yapılmış, beyaz ve şablon gibi görünüyor; ziyaretçiyi içine çekmiyor. Amaç şimdilik para almadan kullanıcı toplamak. Bunun için ana sayfa:
- ürünü bir toplantının yolculuğu olarak anlatmalı;
- güven vermeli;
- tek bir işe çağırmalı: kayıt yüklemek.

Bu spec arayüz yenilemesinin ilk adımı. Kapsamı üç parça:
- tasarım temeli (Tailwind v4 + daisyUI 5, tema, logo);
- herkese açık sayfaların ortak çerçevesi (üst menü, alt bilgi, "yakında" sayfaları, `/tr/` adresleri);
- ana sayfa.

Her sayfa masaüstü ve mobil hâliyle birlikte biter; mobil sonraya bırakılmaz (Ulaş, 2026-11-21).

Bu üç parça ayrı spec'lere bölünmedi, çünkü ana sayfa olmadan temel ve çerçevenin görünen bir sonucu yok. Plan onları ayrı, sırayla doğrulanan dilimler olarak ele alır.

Hedef kullanıcı: toplantısı çok olan şirket ekipleri, sektörden bağımsız (yöneticiler, proje ve yazılım ekipleri, satış, insan kaynakları, danışmanlık ve ajanslar). Ziyaretçi çoğunlukla giriş yapmamış biri.

### Kullanıcı hikâyeleri

1. Elif bir yazılım şirketinde proje yöneticisi. `/tr` adresini açıyor. İlk ekranda Briefyn'in ne işe yaradığını tek cümleden anlıyor ve "Kayıt yükle" düğmesini görüyor.
2. Elif sayfayı kaydırıyor ve bir toplantının yolculuğunu adım adım görüyor:
   - kayıt yüklenir;
   - yazıya dökülür;
   - konuşmacılar adlarıyla ayrılır;
   - özet, kararlar ve yapılacaklar çıkar;
   - geçmiş toplantılara soru sorulur;
   - yapılacaklar takvime gider.
3. Elif "verilerim ne oluyor" diye düşünüyor. Ayrı bir bölüm şunu söylüyor: toplantılar yapay zekâ eğitiminde kullanılmaz, ses analizden sonra silinir, döküm şifreli saklanır.
4. Elif "Kayıt yükle"ye basıyor. Hesabı yok:
   - giriş sayfasına gidiyor, oradan kayıt oluyor;
   - giriş yaptıktan sonra doğrudan yükleme ekranına dönüyor.
5. Elif üst menüde "Ürün"ün üstüne geliyor. Gruplanmış bir menü açılıyor; "Arşive sor"a tıklıyor. Bir "yakında" sayfası açılıyor. Sayfada şunlar var: özelliğin adı, tek cümlelik açıklaması, "Kayıt yükle" düğmesi ve ana sayfaya dönüş.
6. Burak zaten üye. `/tr` adresini açıyor ve tanıtım sayfasında kalıyor. Menüde "Toplantılarım" bağlantısını görüp oradan uygulamaya geçiyor.
7. Biri yalnız `briefyn...` adresini yazıyor (`/`). `/tr` adresine yönlendiriliyor.
8. Mert ana sayfayı telefonundan açıyor. Sayfa ekrana sığıyor, yana kaymıyor. Menü bir düğmeyle açılan bir panelde. Yolculuk bölümü telefona göre yeniden dizilmiş, "Kayıt yükle" parmakla rahat basılıyor.
9. Ayşe'nin bilgisayarında "hareketi azalt" ayarı açık. Sayfa kaydırmaya bağlı canlandırma yapmıyor ama bütün içerik okunuyor.

### Bakılan durum (2026-11-21)

**Bugünkü arayüz ve sayfalar**
- Arayüz Bootstrap 5 ile yapılmış. `bootstrap-icons` jsdelivr'den, yani dışarıdan yükleniyor. `wwwroot/css/site.css` 64 satır. Ortak çerçeve `Pages/Shared/_Layout.cshtml`.
- `Pages/Index` giriş istemiyor; bugünkü tanıtım sayfası bu.
- `Upload` ve `History` sayfaları `[Authorize]` ile korunuyor. Giriş sayfası `/Auth/Login`; `returnUrl` alıp girişten sonra `LocalRedirect` yapıyor.
- Yükleme yalnız `.mp3`, `.wav` ve `.m4a` kabul ediyor, sınır 300 MB (`Upload:MaxMegabytes`).
- Toplantı sayfasında e-postayla paylaşma var (`Details`, `OnPostShareAsync`).

**Docker, CI ve araçlar**
- Docker'da web servisi `dotnet watch` ile çalışıyor ve `./MeetingWeb` klasörü kapsayıcıya bağlı. Yani bilgisayarda üretilen bir CSS dosyası kapsayıcıda hemen görünür. İmajda Node yok.
- CI (`.github/workflows/test.yml`) yalnız .NET ve Python kuruyor, sonra `scripts/verify.sh` koşuyor.
- Bu bilgisayarda Node v26 kurulu.

**Testler**
- `MeetingWeb.Tests/Security/WorkspaceBoundaryTests.cs` her sayfa handler'ının listede olmasını istiyor. Yeni sayfalar oraya eklenmeli.
- Bazı testler sayfa HTML'inde metin arıyor.

**Tailwind ve daisyUI belgeleri**
- daisyUI'nin ASP.NET için kurulum rehberi yok (`/docs/install/aspnet/` 404 verdi).
- Resmî iki yol var: Tailwind CLI npm ile, ya da Tailwind'in tek dosyalık programı ile daisyUI'nin `.mjs` dosyaları.
- Belgelerde görünen sürümler: Tailwind v4.3, daisyUI 5.8.2.
- Özel tema `@plugin "daisyui/theme" { ... }` ile tanımlanıyor.

**scroll-craft skill'i** (`nateherk-design` 0.3.1, SKILL.md okundu)
- Ön kontrol `doctor.mjs` ile yapılıyor. Gerekenler: Node, tam bir ffmpeg kurulumu, Playwright ve Chrome.
- Çalışma klasörü varsayılan olarak `<repo>/scrollcraft`. `SCROLLCRAFT_HOME` ile başka yere alınabiliyor.
- Motor dosyaları (`scrollcraft.js` ve `.css`) olduğu gibi kopyalanıyor, değiştirilmiyor. Tema altı renk ve iki yazı tipi değişkeniyle veriliyor.
- Görseller ya kie.ai ile üretiliyor (ücretli) ya da kullanıcı kendisi veriyor. İkinci yol belgede tam destekleniyor.
- Skill mobil görünümü baştan hedef sayıyor.

**Yapay zekâ sağlayıcılarının veri kullanımı** (ayrıntı Backlog 1 ve 2'de)
- Groq'un sözleşmesi girdi ve çıktıyla model eğitmeyi yasaklıyor (madde 4.2).
- Döküm bizde şifreli saklanıyor (ADR-0002).

### Varsayımlar

1. **Dil seçici:** İngilizce sayfa olmadığı için şimdi gösterilmiyor. Adres yapısı ona hazır. `hreflang` İngilizce gelince eklenir.
2. **Eski ana sayfa:** `Pages/Index` kaldırılır, `/` adresi `/tr` adresine geçici yönlendirme (302) yapar. Tek dil varken bu bir dile göre yönlendirme değil, Google'ın kaçınılmasını istediği şey de bu değil.
3. **Giriş yapmış kullanıcının bağlantısı:** "Toplantılarım" `History` sayfasına gider.
4. **Alt bilgideki gizlilik bağlantısı:** "Gizlilik" bugünkü `Privacy` sayfasına gider. O sayfanın metni hâlâ taslak (Backlog 1).

## Kapsam

**İçinde**
- **Tasarım temeli:**
  - Tailwind v4 + daisyUI 5 kurulumu ve CSS derleme adımı (`verify.sh`, CI, README);
  - `briefyn` adlı daisyUI teması;
  - kendi sunucumuzdan verilen yazı tipleri ve simgeler;
  - logo A'nın tema renginde SVG hâli ve sekme simgesi (favicon) için sade sürümü.
- **Herkese açık çerçeve:**
  - `_PublicLayout` (Tailwind);
  - açılır menülü üst menü ve alt bilgi; telefonda menü bir düğmeyle açılan panel olur;
  - aşağıdaki sayfa haritasının hepsi, ortak "yakında" şablonuyla;
  - `/tr` adresleri.
- **Ana sayfa:** `/tr`, scroll-craft ile, masaüstü ve mobil birlikte. Mobilde yalnız sıkıştırılmış masaüstü olmaz; scroll-craft'ın kendi mobil düzeni yapılır.

**Dışında**
- **Diğer sayfalar ve içerikler:**
  - uygulama içi sayfaların yenilenmesi (sıradaki spec'ler: arşive sor sohbet ekranı, sonra konuşmacı adlı toplantı sayfası);
  - "yakında" sayfalarının içeriği;
  - İngilizce içerik ve dil seçici;
  - ücretli plan, plan tablosu ve Fiyatlandırma içeriği (Backlog 8).
- **Ayrı işler:** Groq'ta sıfır veri saklama ayarı (Backlog 1). Eski Bootstrap sayfalarının görünümüne dokunulmaz.

## Sayfa haritası

Bütün tanıtım adresleri `/tr/` ile başlar, sayfa adları İngilizcedir. İngilizce eklenince aynı adlar `/en/` altında açılır.

Menüdeki her bağlantı ve her "yakında" sayfası tek bir katalogdan üretilir (`Services/` altında, sayfadan ayrı). Katalogdaki her kayıtta şunlar bulunur:
- bölüm;
- grup;
- adres adı;
- Türkçe başlık;
- tek cümlelik açıklama.

Katalogda olmayan bir adres 404 verir.

| Menü | Grup | Sayfa (Türkçe başlık → adres) |
|---|---|---|
| Ürün | Kaydet | Dosya yükleme → `/tr/product/upload` · Zoom'dan aktarma → `/tr/product/zoom` |
| | Anla | Yazıya dökme → `/tr/product/transcription` · Konuşmacılar → `/tr/product/speakers` · Özet ve kararlar → `/tr/product/summary` · Yapılacaklar → `/tr/product/action-items` |
| | Bul | Arşive sor → `/tr/product/ask-archive` · Toplantı serileri → `/tr/product/series` |
| | Paylaş | Takvim daveti → `/tr/product/calendar` · E-postayla paylaşma → `/tr/product/share` · Ekip çalışma alanı → `/tr/product/workspaces` |
| Çözümler | | Yönetim ve liderler → `/tr/solutions/leadership` · Proje ve yazılım ekipleri → `/tr/solutions/project-teams` · Satış ve müşteri görüşmeleri → `/tr/solutions/sales` · İnsan kaynakları ve işe alım → `/tr/solutions/hr` · Danışmanlık ve ajanslar → `/tr/solutions/agencies` |
| Entegrasyonlar | | Zoom → `/tr/integrations/zoom` · Takvim (Outlook, Google) → `/tr/integrations/calendar` · Microsoft Teams → `/tr/integrations/teams` · Google Meet → `/tr/integrations/google-meet` · Slack → `/tr/integrations/slack` · Trello ve Jira → `/tr/integrations/trello-jira` |
| Kaynaklar | | Yardım merkezi → `/tr/resources/help` · Güvenlik ve gizlilik → `/tr/resources/security` · Yenilikler → `/tr/resources/changelog` · İletişim → `/tr/resources/contact` |
| Kurumsal | | `/tr/enterprise` |
| Fiyatlandırma | | `/tr/pricing` (yakında; "şimdilik her şey ücretsiz" cümlesi) |

**"Yakında" sayfasında ne var:**
- başlık ve açıklama;
- "Bu sayfa hazırlanıyor" cümlesi;
- "Kayıt yükle" düğmesi ve ana sayfaya dönüş bağlantısı;
- `<meta name="robots" content="noindex">` (içi boş sayfa arama sonuçlarına girmesin diye).

**Bugün var olmayan özellikler:** Teams, Meet, Slack, Trello/Jira ve Google takvim bağlama henüz yok. Menüde "yakında" diye işaretlenir. Google Meet Backlog'da yok; Ulaş'ın kararıyla "yakında" olarak menüde durur, gerekmezse sonra kaldırılır.

## Görünüm

**Renk:**
- açık, renkli ve canlı: zemin bembeyaz değil, açık tonlu bir renk;
- ana renk canlı petrol / camgöbeği mavisi;
- koyu tema yok, cıyak (neon) renk yok, şablon görünümü yok.
- **Geçişli renk** (Ulaş, 2026-11-21, önizleme 1'den sonra): renkler mat ve düz değil, geçişli (degrade) olur; örnek "turuncudan mora ya da mordan siyaha". Degrade düğme, açılış, şerit gibi yüzeylerde kullanılır; degrade yazı yine yok, Fireflies'ın kendi mor degradesi (`#6C31D9`–`#4013F2`) yine yok.
- **Zemin** (Ulaş, aynı gün): sayfa açık zeminli kalır; yalnız açılış bölümü ve alt bilgi koyu degrade olur, ana renk orada parlar. Sayfanın tamamı koyu olmaz.

Başlangıç noktası `docs/brand/palet-onerileri.html` dosyasındaki "Petrol ve buz" paleti. Ulaş bu tonları beğendi, ama daha canlı ve zemini renkli bir hâlini istiyor. Aynı dosyadaki diğer iki palet ve beyaz ağırlıklı yön reddedildi. Kesin renk değerlerini tasarım adımında Opus alt ajanı seçer. Kodlamaya geçmeden önce Ulaş önizlemeyi görüp onaylar.

**Fireflies'tan uzak dur:**
- koyu zemin;
- `#6C31D9`–`#4013F2` arası mor degrade;
- Inter ve DM Sans yazı tipleri;
- metinleri ve sayfa dizilişi birebir.

**Yazı tipleri:**
- Türkçe harfleri (ğ, ş, ı, İ) içermeli;
- serbest lisanslı (SIL Open Font License) olmalı;
- `wwwroot` içinden verilir;
- dış sunucudan hiçbir dosya (yazı tipi, simge, betik) yüklenmez.

**Logo:** Logo A (ses dalgası not satırına dönüşüyor):
- tema renginde SVG olarak yeniden çizilir;
- 16 piksel için az öğeli ayrı bir işaret yapılır (bugünkü işaret o boyutta leke oluyor);
- kaynak PNG'ler `docs/brand/` altında saklanır.

**scroll-craft'ın kesin kuralları uygulanır:**
- görünen metinde uzun tire (—) yok;
- "aşağı kaydır" işareti ve `01 / 06` gibi sayaç yok;
- uydurma sayı, müşteri, yorum ya da sertifika yok;
- degrade yazı ve neon parlama yok;
- tek bir zirve anı (yolculuk bölümü) ve bir imza hareketi var.

**Kayan şerit:** Sabit hızla sağa kayan bir şerit var. İçinde uydurma müşteri logosu olmaz. İçeriği tasarım adımında seçilir: toplantı türleri ya da bugün çalışan entegrasyonlar. Bir marka logosu kullanılacaksa o markanın kullanım kurallarına uyulur, yoksa yalnız adı yazılır.

**Görseller:**
- codex-fleet (Astra) ile üretilen durağan görseller kullanılır;
- video yok; bütçe olursa Ulaş sonra değiştirir;
- görselin içinde yazı olmaz, yazılar sayfanın kendisinde durur.

## Ana sayfa içeriği

Sıra ve bölümlerin nasıl hareket edeceği scroll-craft'ın tasarım adımında kesinleşir. İçerik şu:

1. **Açılış:**
   - bir cümlelik vaat;
   - bir alt cümle;
   - "Kayıt yükle" düğmesi.
2. **Kayan şerit.**
3. **Toplantının yolculuğu** (sayfanın zirvesi): yükleme → yazıya dökme → konuşmacılar → özet, kararlar, yapılacaklar → arşive sor → takvim ve paylaşma.
4. **Kimler için:** Çözümler'deki beş rol, birer cümle.
5. **Güven:**
   - "Toplantılarınız yapay zekâ eğitiminde kullanılmaz.";
   - "Ses analizden sonra silinir, döküm şifreli saklanır.";
   - "Hiç saklanmaz" denmez;
   - sertifika adı geçmez.
6. **Sık sorulanlar:** kod ve ayarlarla doğrulanmış cevaplar.
   - Ücretli mi? Şimdilik ücretsiz.
   - Hangi dosyalar? `.mp3`, `.wav`, `.m4a`, en fazla 300 MB.
   - Zoom kayıtları? Hesap bağlanınca bulut kayıtları listeden seçilip tek tıkla aktarılır. ("Kendiliğinden gelir" denmez: yalnız sunucuda Zoom bildirim anahtarı tanımlıysa doğru; Ulaş, 2026-11-21.)
   - Verilerim? Güven bölümüyle aynı cevap.
7. **Kapanış:** "Kayıt yükle".
8. **Alt bilgi:** menü bağlantıları, Gizlilik, İletişim.

**Kural:** Sayfadaki her ürün cümlesi bugün çalışan bir özelliğe dayanır. Olmayan özellik ana sayfada anlatılmaz.

## Tech Stack

- **Sunucu:** ASP.NET Core 10 Razor Pages (`net10.0`), var olan proje.
- **Stil:** Tailwind CSS v4 (`tailwindcss`, `@tailwindcss/cli`) ve daisyUI 5 (`daisyui`), npm ile. Bu üç paket onaylı: Ulaş derleme yolu kararını Claude'a bıraktı (2026-11-21). npm seçildi, çünkü iki aracın da birinci resmî yolu bu ve sürümler kilit dosyasında sabit kalıyor. Bedeli: CI'a Node kurulumu eklenir, geliştirirken `npm run css:watch` ayrıca açık tutulur.
  - `package.json` ve `package-lock.json` repo kökünde durur, `node_modules` `./MeetingWeb` bağlantısının dışında kalır.
  - Sürümler kilit dosyasında sabittir. Kurulum anındaki en son kararlı sürümler alınır; belgede görünenler v4.3 ve 5.8.2.
- **Ana sayfa motoru:** scroll-craft. `engine/scrollcraft.js` ve `engine/scrollcraft.css` değiştirilmeden `wwwroot/lib/scrollcraft/` altına konur. Sayfaya özel davranış sayfanın kendi betiğinde yazılır.
- **Eski sayfalar** Bootstrap ile `_Layout`'ta kalır. Bootstrap ile Tailwind aynı sayfada yüklenmez.

## Komutlar

```bash
npm ci                                   # repo kökünde, bir kez ve package-lock değişince
npm run css:build                        # MeetingWeb/Styles/app.css → MeetingWeb/wwwroot/css/app.css (küçültülmüş)
npm run css:watch                        # geliştirirken; Docker'daki dotnet watch dosyayı bağlı klasörden görür
docker compose up -d --build             # uygulama: http://127.0.0.1:8080/tr
scripts/verify.sh                        # lint, biçim, tip, CSS derleme, build, testler (commit öncesi hook ve CI aynısı)
dotnet test MeetingWeb.Tests --filter "FullyQualifiedName~PublicSite"   # yalnız bu işin testleri
```

**scroll-craft** (`<skill>` = `~/.claude/plugins/cache/nateherk/nateherk-design/0.3.1/skills/scroll-craft`). Çalışma klasörü repo dışında tutulur (`SCROLLCRAFT_HOME`):

```bash
node <skill>/scripts/doctor.mjs
node <skill>/scripts/shoot.mjs --url http://127.0.0.1:8080/tr --out <workspace>/builds/briefyn/lab/shots
node <skill>/scripts/shoot.mjs --url http://127.0.0.1:8080/tr --out <workspace>/builds/briefyn/lab/mobile --width 390 --height 844
node <skill>/scripts/shoot.mjs --url http://127.0.0.1:8080/tr --out <workspace>/builds/briefyn/lab/reduced --reduced-motion
```

**Ölçüm:** Lighthouse, masaüstü ve mobil ayarıyla ayrı ayrı, `http://127.0.0.1:8080/tr`.

## Proje yapısı

```
package.json, package-lock.json        → Tailwind ve daisyUI (repo kökü)
MeetingWeb/Styles/app.css              → Tailwind girişi: @import, @plugin "daisyui", briefyn teması
MeetingWeb/wwwroot/css/app.css         → derlenmiş çıktı (git'e girmez)
MeetingWeb/wwwroot/fonts/              → yazı tipi dosyaları
MeetingWeb/wwwroot/img/brand/          → logo SVG'leri, favicon
MeetingWeb/wwwroot/img/home/           → ana sayfa görselleri
MeetingWeb/wwwroot/lib/scrollcraft/    → scroll-craft motoru (değiştirilmez)
MeetingWeb/Pages/Shared/_PublicLayout.cshtml   → yeni çerçeve: üst menü, alt bilgi
MeetingWeb/Pages/Shared/_PublicNav.cshtml      → açılır menüler (katalogdan)
MeetingWeb/Pages/Public/Home.cshtml            → /tr
MeetingWeb/Pages/Public/ComingSoon.cshtml      → /tr/{section}/{slug} ve /tr/enterprise, /tr/pricing
MeetingWeb/Services/PublicSite/SiteCatalog.cs  → menü ve "yakında" sayfalarının tek kaynağı
MeetingWeb.Tests/Features/PublicSiteTests.cs   → bu işin testleri
docs/brand/                            → logo kaynak PNG'leri
```

Klasör ve dosya adları plan adımında kesinleşebilir. Kural şu: sayfa mantığı `Services/`'te olur, sayfada veritabanı sorgusu olmaz.

## Kod stili

Var olan C# düzeni geçerli (`dotnet format`). Kullanıcıya görünen her metin sade Türkçe. Razor'da Tailwind ve daisyUI sınıfları HTML'de yazılır. Tekrar eden parçalar partial view olur; `@apply` ile sınıf yığını yapılmaz.

```csharp
public sealed record SiteEntry(string Section, string? Group, string Slug, string Title, string Summary, bool Available);
```

```cshtml
<a asp-page="/Upload" class="btn btn-primary btn-lg">Kayıt yükle</a>
```

## Test stratejisi

**Otomatik testler** (xUnit + `WebApplicationFactory`, `MeetingWeb.Tests/Features/PublicSiteTests.cs`). Testler sınıf adına değil, metne, adrese ve davranışa bakar:
- `/tr` giriş yapmamış ziyaretçiye 200 döner: `<h1>` var, `/Upload` bağlantısı var.
- `/` adresi `/tr` adresine 302 ile yönlenir.
- Katalogdaki her adres 200 döner: başlık ve `noindex` var. Katalogda olmayan adres 404 döner.
- Giriş yapmamış ziyaretçi `/Upload` açınca `/Auth/Login?ReturnUrl=%2FUpload` adresine gider. Giriş yapmış kullanıcı `/tr` adresinde 200 alır, sayfada "Toplantılarım" bağlantısı görünür.
- `/tr` sayfası `bootstrap.min.css` yüklemez. Eski sayfalardan biri (ör. `/History`) `app.css` yüklemez.
- Hiçbir herkese açık sayfa başka bir sunucudan (`https://` ile başlayan dış adres) yazı tipi, simge, betik ya da stil yüklemez.
- Görünen metinde uzun tire (—) yok.
- `WorkspaceBoundaryTests` listesine yeni handler'lar `NotWorkspaceScoped` olarak eklenir.

**Görsel kontrol:**
- scroll-craft'ın 5. adımı: masaüstü (1440 genişlik), telefon (390 × 844) ve hareket azaltılmış hâlde ekran görüntüleri ile kontak sayfası (`sheet.png`), her bölümde kontrast ölçümü.
- Telefon kontrolü tarayıcı taklidiyle yetmez. scroll-craft belgesi başsız Chrome'un iPhone'un video, otomatik oynatma ve dokunarak kaydırma davranışını taklit edemediğini söylüyor. Bu yüzden sayfa iOS Simulator'daki Safari'de de açılıp kaydırılır. Ulaş isterse kendi telefonunda da bakar; bunun için yerel ağdan açma iş bitince kapatılır.
- Eski sayfalardan ikisinin (`/History` ve `/Upload`) önceki ve sonraki ekran görüntüleri aynı olmalı.

**Ölçüm** (Codex): Lighthouse masaüstü ve mobil.

Bütün test takımını commit öncesi hook koşar. İşçiler yalnız değiştirdikleri yerin testlerini koşar.

## Sınırlar

**Her zaman:**
- commit öncesi `scripts/verify.sh` geçer;
- görünen metin sade Türkçedir;
- her ürün cümlesi çalışan bir özelliğe dayanır;
- metin ile zemin arasında okunabilirlik oranı (kontrast) en az 4,5:1 olur;
- dış sunucudan dosya yüklenmez;
- scroll-craft motoru ve skill dosyaları olduğu gibi kalır;
- eski sayfaların görünümü değişmez.

**Önce sor:**
- `tailwindcss`, `@tailwindcss/cli` ve `daisyui` dışında npm ya da NuGet paketi eklemek;
- ücretli görsel servisi (kie.ai) ya da video kullanmak;
- eski Bootstrap sayfalarına dokunmak;
- uygulama içi sayfaların adresini değiştirmek;
- çerez, analiz ya da izleme eklemek;
- menüye Backlog'da olmayan bir özellik eklemek.

**Asla:**
- Fireflies'ın renklerini, yazı tiplerini ya da metinlerini kullanmak;
- uydurma müşteri, sayı, yorum ya da sertifika koymak;
- "verileriniz hiç saklanmaz" demek;
- dışarıdan (CDN'den) dosya yüklemek;
- derlenmiş CSS'i ya da `node_modules`'ü commit etmek;
- commit öncesi kontrolü atlamak.

## Başarı ölçütleri

1. Giriş yapmamış ziyaretçi `/tr` adresini açıyor, sayfa 200 dönüyor. `/` adresi `/tr` adresine yönleniyor.
2. Üst menüde altı başlık var: Ürün, Çözümler, Entegrasyonlar, Kaynaklar, Kurumsal, Fiyatlandırma.
   - Ürün, Çözümler, Entegrasyonlar ve Kaynaklar açılır menüdür.
   - Ürün menüsü dört gruba ayrılır.
   - Bütün bağlantılar 200 dönüyor.
3. Her "yakında" sayfasında şunlar var: başlık, tek cümle açıklama, "Kayıt yükle", ana sayfa bağlantısı ve `noindex`.
4. "Kayıt yükle" düğmesi:
   - giriş yapmamış ziyaretçiyi giriş sayfasına, girişten sonra yükleme ekranına götürüyor;
   - giriş yapmış kullanıcıyı doğrudan yükleme ekranına götürüyor.
5. Giriş yapmış kullanıcı `/tr` adresinde kalıyor ve menüde "Toplantılarım" bağlantısını görüyor.
6. Ana sayfada yolculuk bölümü ve güven bölümü var; güven bölümü yalnız izin verilen cümleleri kullanıyor.
7. Herkese açık sayfalar başka bir sunucuya istek atmıyor. Tarayıcının ağ listesinde yalnız `127.0.0.1:8080` görünüyor.
8. Bütün metinlerde kontrast oranı en az 4,5:1, büyük başlıklarda en az 3:1. Ölçüm scroll-craft'ın aracıyla, en parlak karede yapılır.
9. "Hareketi azalt" ayarı açıkken sayfa kaydırmaya bağlı canlandırma yapmıyor ve bütün metin okunuyor.
10. Lighthouse sonuçları:
    - masaüstü: Performans en az 90, Erişilebilirlik en az 95, En iyi uygulamalar en az 95, Arama motoru en az 90;
    - mobil: Performans en az 80, diğer üçü masaüstüyle aynı.
11. Telefonda (390 genişlik ve iOS Simulator Safari):
    - sayfa yana kaymıyor;
    - menü panel olarak açılıp kapanıyor ve bütün bağlantılara ulaşılıyor;
    - düğme ve bağlantıların dokunma alanı en az 44 × 44 piksel;
    - yolculuk bölümü telefona göre dizilmiş, metinler okunuyor.
12. Görünen metinde uzun tire yok; "aşağı kaydır" işareti ve sayaç yok; uydurma sayı yok.
13. Eski sayfalar (`/History`, `/Upload`) eskisi gibi görünüyor.
14. `scripts/verify.sh` (CSS derlemesi dahil) ve CI geçiyor. README'de npm adımları yazılı.
15. Ulaş iki noktada onay veriyor:
    - palet, yazı tipi ve logo önizlemesi, kodlamadan önce;
    - bitmiş sayfanın masaüstü ve telefon ekran görüntüleri, iş kapanmadan önce.

## Kararlar (Ulaş, 2026-11-21)

1. **CSS derleme yolu:** karar Claude'a bırakıldı; npm (bkz. Tech Stack).
2. **Mobil:** her sayfa masaüstü ve mobiliyle birlikte biter. Ana sayfa scroll-craft'ın kendi mobil düzeniyle yapılır, skill belgesinden ayrılınmıyor.
3. **Video:** yok, yalnız durağan görsel. Bütçe olursa Ulaş sonra değiştirir.
4. **Google Meet:** Entegrasyonlar menüsünde "yakında" olarak durur; gerekmezse Ulaş kaldırtır.

## Açık sorular

Yok.
