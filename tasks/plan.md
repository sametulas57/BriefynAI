# Uygulama planı: Arayüz yenileme 1, ana sayfa ve herkese açık çerçeve

Spec: `SPEC.md` (onaylı, 2026-11-21) · Görev listesi: `tasks/todo.md` · Araştırma notu: Beyin `knowledge/concepts/briefyn-fireflies-arastirmasi.md` · Durum: onaylandı (Ulaş, 2026-11-21); uygulama yeni oturumda başlar

**Bu planla çalışan her oturum önce üç şeyi okur:** `SPEC.md`, bu dosya ve Beyin notu `knowledge/concepts/briefyn-fireflies-arastirmasi.md` (kasada: `~/Documents/Obsidian/Beyin/`). Kararların gerekçeleri ve Fireflies ayrıntıları (renk değerleri, yazı tipleri, menü yapısı, kaçınılacaklar) spec'te değil, o nottadır.

Önceki iş (Şifremi unuttum) bu dosyalarda yürüyordu, bütün maddeleri kapanmıştı (0 açık kutu). Eski plan git geçmişinde (`55b1503` ve öncesi).

## Özet

Üç parça sırayla doğrulanan dilimlere bölündü: CSS derleme temeli, herkese açık çerçeve (katalog, "yakında" sayfaları, menü, `/tr` adresleri), scroll-craft ana sayfası. En riskli iki şey öne alındı:
- **Ulaş'ın görsel onayı** (palet, yazı tipi, logo) kodlamayla paralel, en başta yürür; tema ancak onaydan sonra uygulanır.
- **Yönlendirme ve giriş akışı** (`/` → `/tr`, "Kayıt yükle" → giriş → yükleme) ana sayfa yapılmadan, boş bir `/tr` sayfasıyla test edilir.

Ana sayfa en sonda, çünkü çerçeve, tema ve brief olmadan scroll-craft'a başlanmaz.

## Kodda baktıklarım (planın dayanağı)

- `Pages/Index` giriş istemiyor, yalnız sayfa içi `<style>` ve Bootstrap. `_ViewStart` her sayfaya `_Layout` veriyor; `_Layout` veritabanından çalışma alanı okuyor.
- **`RedirectToPage("/Index")` 10 yerde çağrılıyor** (`Auth/Login` GET, `Auth/Register` kayıttan sonra, `Auth/ForgotPassword`, `Workspace/Members`, `Create`, `Switch`, `Join`). Ayrıca `_Layout`, `_LoginPartial` (çıkışta `returnUrl`), `Error` ve `Workspace/Create` görünümleri `/Index`'e bağlanıyor. `Pages/Index` silinirse bunlar çalışma anında hata verir. Bir test `/` ya da `/Index` yönlendirmesi bekliyor (`PasswordResetTests.cs:571`).
- **Kayıt sayfası `returnUrl` taşımıyor.** Giriş sayfası `returnUrl` alıp `LocalRedirect` yapıyor, ama girişteki "Hemen Kayıt Olun" bağlantısı onu geçirmiyor ve `Register` kayıttan sonra `/Index`'e gidiyor. Yani hesabı olmayan ziyaretçi kayıttan sonra yükleme ekranına değil tanıtım sayfasına düşer (kullanıcı hikâyesi 4).
- `Program.cs`: `UseStatusCodePagesWithReExecute("/Durum/{0}")`, `UseStaticFiles()`, `AddRazorPages()`; özel route kuralı yok.
- Testler: `BriefynFactory` her test sınıfına geçici `wwwroot` veriyor, yani testte derlenmiş `app.css` yok; testler yalnız HTML'e bakmalı. `WorkspaceBoundaryTests` `Index:Get`'i `NotWorkspaceScoped` sayıyor.
- `verify.sh` yalnız .NET ve Python koşuyor; `ai_engine/.venv` yoksa açıklamalı duruyor (CSS için aynı kalıp kullanılabilir). CI yalnız .NET ve Python kuruyor.
- `.gitignore` `wwwroot/`'u dışarıda bırakmıyor; derlenmiş `app.css` için ayrı satır gerekir.
- Docker imajı `sdk:10.0` + `dotnet watch`; Node yok. `./MeetingWeb` kapsayıcıya bağlı.
- scroll-craft (`SKILL.md` okundu): Adım 0'da 8 soruluk görüşme (`BRIEF.md`), Adım 1 yolculuk, Adım 2 dilbilgisi + parmak izi kapısı + his eğrisi, Adım 3 görseller, Adım 4 sayfa, Adım 5 kaydırarak doğrulama. `shoot.mjs` çalışma klasöründe `playwright-core` istiyor. Motor dosyaları değiştirilmiyor.

## Mimari kararlar

1. **`Pages/Index` silinmez, yalnız yönlendirme olur.** İçeriği ve görünümü kaldırılır; `OnGet` `/tr`'ye 302 döner. Böylece on `RedirectToPage("/Index")` çağrısı ve eski Bootstrap sayfalarındaki bağlantılar dokunulmadan `/tr`'ye varır (iki adım yönlendirme, zararsız). Spec'in "Pages/Index kaldırılır" maddesinin amacı (`/` → `/tr`, eski tanıtım sayfası yok) böyle karşılanıyor; eski sayfalara dokunma kuralı da korunuyor.
2. **Herkese açık sayfalar `Pages/Public/` altında, kendi `_ViewStart`'ıyla `_PublicLayout`'u kullanır.** `_PublicLayout` veritabanına gitmez; yalnız `User.Identity.IsAuthenticated`'e bakar ("Toplantılarım" bağlantısı için). Bootstrap ve `site.css` yüklemez; yalnız `app.css`.
3. **Adresler sayfa yönergesiyle:** `Home` → `@page "/tr"`; `ComingSoon` → `@page "/tr/{section}/{slug?}"`. `SiteCatalog` (`Services/PublicSite/`, singleton, salt okunur liste) adresi çözer; bulamazsa `NotFound()`. `/tr/enterprise` ve `/tr/pricing` gruptan bağımsız tek parçalı kayıtlar olarak katalogda. Menü de aynı katalogdan üretilir; sayfada liste yazılmaz.
4. **CSS derleme:** repo kökünde `package.json` (`tailwindcss`, `@tailwindcss/cli`, `daisyui`; sürümler kilitte), `css:build` ve `css:watch` betikleri. `verify.sh` .NET'ten önce `npm run css:build` koşar; `node_modules` yoksa `.venv` kalıbıyla "bir kez kur: `npm ci`" der. CI'a `actions/setup-node` + `npm ci` eklenir. Derlenmiş `MeetingWeb/wwwroot/css/app.css` `.gitignore`'da.
5. **Tema iki adımda:** Görev 1'de geçici, nötr bir `briefyn` teması (yalnız derlemenin çalıştığını göstermek için); Ulaş önizlemeyi onaylayınca (Kontrol A) gerçek değerler, yazı tipleri ve logo Görev 5'te girer.
6. **Simgeler kütüphanesiz:** az sayıda simge (menü, ok, kapat, kilit) elle yazılmış satır içi SVG partial'ı olur; yeni paket yok. `bootstrap-icons` herkese açık sayfalarda yüklenmez.
7. **scroll-craft çalışma klasörü repo dışında** (`SCROLLCRAFT_HOME`, ör. `~/scrollcraft`); `BRIEF.md`, ham görseller, ekran görüntüleri ve `playwright-core` orada. Repoya yalnız sayfanın kendisi, küçültülmüş görseller ve değiştirilmemiş motor dosyaları girer.
8. **Ana sayfa metinleri** Razor'da düz HTML; her ürün cümlesinin dayandığı özellik görev notunda yazılır (spec kuralı). Güven bölümünün cümleleri testte birebir aranır.
9. **Kayıttan sonra da geri dönülür** (Ulaş, 2026-11-21). Girişteki "Hemen Kayıt Olun" bağlantısı gelen `returnUrl`'i kayıt sayfasına geçirir; `Register` onu formda taşır ve kayıttan sonra `LocalRedirect` ile oraya döner (boşsa bugünkü gibi `/Index`). Eski sayfaların görünümü değişmez; dokunulan yalnız bağlantının parametresi ve kayıt sonrası yönlendirme. Görev 4'te.
10. **Yazı tipi dosyaları resmî depodan** (Ulaş, 2026-11-21). Seçilen yazı tipinin `.woff2` dosyaları ve lisans dosyası (`OFL.txt`) yazı tipinin resmî deposundan (ör. GitHub sürümü) bir kez indirilir, `wwwroot/fonts/`'a konur. npm paketi (`@fontsource/...`) eklenmez. İndirmeden önce Ulaş'a kaynak adres, dosya adları ve boyutları söylenir, onay beklenir. Görev 5'te.
11. **`playwright-core` repo dışında** (Ulaş, 2026-11-21). scroll-craft'ın `shoot.mjs`'i için repo dışındaki scroll-craft çalışma klasörüne (`SCROLLCRAFT_HOME`) kurulur; repodaki `package.json`'a girmez. Görev 7'de.

## Bağımlılık sırası

```
1 CSS derleme temeli ─────────────┬── 3 katalog + "yakında" sayfaları ── 4 /tr, / yönlendirmesi, Kayıt yükle akışı
2 görsel önizleme (paralel) ──┐   │                                                     │
                              └───┴──────────── [Kontrol A: Ulaş önizlemeyi onaylar, verify geçer]
                                                                  │
                                    5 tema, yazı tipi, logo ── 6 üst menü (masaüstü + telefon paneli)
                                                                  │
                                                    [Kontrol B: çerçeve bitti, iki genişlikte ekran]
                                                                  │
                     7 scroll-craft brief (Ulaş'la) ── 8 görseller ── 9 açılış, şerit, yolculuk ── 10 kalan bölümler
                                                                  │
                                                    [Kontrol C: ana sayfa iki genişlikte çalışıyor]
                                                                  │
                                              11 doğrulama turu ve düzeltmeler ── 12 belgeler, Ulaş'ın son onayı
                                                                  │
                                                         [Kontrol D: bitti, push, CI]
```

- Görev 2, Görev 1, 3 ve 4 ile paralel yürür (dosya paylaşmazlar; görev 2 yalnız önizleme dosyası üretir).
- Görev 7 (görüşme) Kontrol B'yi beklemek zorunda değil; Ulaş uygunsa Görev 5-6 sürerken yapılabilir. Görev 8 Kontrol A'yı (onaylı palet) bekler.
- Görev 3 ve 4 aynı katalog ve testlere dokunduğu için sırayla.

## İş dağılımı

Ulaş'ın kuralına göre:
- **Codex (`gpt-6.1-sol`):** Görev 1 (derleme, `verify.sh`, CI), Görev 3 ve 4'ün sayfa mantığı ve testleri, Görev 11'deki Lighthouse ölçümü.
- **Claude Opus alt ajanı** (`frontend-design`, `scroll-craft`): Görev 2 önizlemesi, görünen bileşenlerin kodu (Görev 3-4'ün `.cshtml` dosyaları, 5, 6, 9, 10).
- **codex-fleet (Astra):** Görev 8 görselleri.
- **Ben (ana döngü):** Görev 7 görüşmesi Ulaş'la, brifler, her görevden sonra diff okuma, odak testleri, `verify.sh`, commit; Görev 11'deki ekran kontrolü ve iOS Simulator; Görev 12.

Her brife o adımın Addy skill'leri yazılır ve önce okunması istenir: `incremental-implementation`, `test-driven-development`, `frontend-ui-engineering`; derlemede `ci-cd-and-automation`; doğrulamada `browser-testing-with-devtools`. Brife şu da yazılır: "yalnız değiştirdiğin yerin testlerini koş, tam takımı commit hook'u koşar". Görev 2, 5, 9, 10 briflerine Beyin notunun ilgili bölümleri (Fireflies'tan kaçınılacaklar, palet turları) eklenir; alt ajan kasayı kendisi açmaz.

## Riskler

| Risk | Etki | Önlem |
|---|---|---|
| Palet onayı birkaç tur sürer | Orta | Görev 2 en başta ve paralel; tema dışındaki her şey geçici temayla ilerler. |
| `Pages/Index` kaldırılınca eski yönlendirmeler kırılır | Yüksek | Karar 1: `Index` yönlendirme sayfası olarak kalır; Görev 4'te `/Index` ve `/` için test. |
| Kayıt sonrası yükleme ekranına dönülmüyor (hikâye 4) | Orta | Karar 9: Görev 4'te `returnUrl` kayıttan da geçiyor; dış adrese yönlendirme olmasın diye `LocalRedirect` ve testte dış adres denemesi. |
| `npm run css:build` commit hook'unu yavaşlatır | Düşük | Tailwind CLI tek dosya derliyor; süre Görev 1'de ölçülüp yazılır. |
| Docker imajında Node yok; ileride üretim imajında `app.css` olmaz | Orta | Bu işte yerel geliştirme bağlı klasörle çalışıyor. Backlog 1'deki "prod compose" maddesine "imaj CSS'i derlemeli" notu Görev 12'de eklenir. |
| scroll-craft motoru Razor sayfasında beklenmedik davranır (betik sırası, `data-sc-*`) | Orta | Görev 9 en riskli bölümle (yolculuk) başlar; ilk iş motoru `/tr`'de boş bir bölümle çalıştırıp `shoot.mjs` ile görmek. |
| Telefonda başsız Chrome'da görünmeyen hata | Orta | Görev 11'de iOS Simulator Safari zorunlu; Ulaş isterse kendi telefonu (yerel ağ iş bitince kapanır). |
| Lighthouse mobil performans 80'in altında kalır (görseller) | Orta | Görev 8'de görseller mobil ve masaüstü için ayrı boyutta, modern biçimde; Görev 11'de ölçüm ve düzeltme. |
| Eski sayfaların görünümü farkında olmadan değişir | Orta | Görev 1'in ilk adımı `/History` ve `/Upload`'ın önceki ekran görüntüleri; Görev 11'de karşılaştırma. `_Layout` hiç değiştirilmez. |

## Açık sorular

Yok. Üç soru Ulaş'ın onayıyla kapandı (2026-11-21), cevaplar mimari kararlar 9, 10 ve 11'de.
