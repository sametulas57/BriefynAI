# Backlog

Yapılacak her iş burada, tek listede, önem sırasıyla. Sıradaki iş en üstten seçilir; o anki iş `SPEC.md` ve `tasks/`'ta yürür, bitince maddesi buradan çıkarılır. Yeni iş listenin sonuna eklenir; sırayı Ulaş değiştirir. Bitenler git geçmişinde, kalıcı kararlar `docs/decisions/`'da, kısıtlar `AGENTS.md`'de.

1. **Yayın hazırlığı:**
   - gizli anahtarların yenilenmesi;
   - alan adı (site ve mail). Alınınca yerel Zoom denemeleri ngrok'tan Cloudflare adlı tünele geçer; tarif `docs/raporlar/2026-10-22-sabit-tunel.md`;
   - prod compose (bugün yalnız `docker-compose.yml` var);
   - tünel ya da vekil sunucunun arkasında gerçek istek adresi için `ForwardedHeaders`; kurulmazsa bütün istekler aynı adresten gelir ve şifre sıfırlama ile Zoom bildirim ucundaki adres başına sınır herkesi birlikte kilitler (ADR-0016);
   - herkese açık adres belli olunca `App:PublicUrl` ayarı doldurulur; takvim davetindeki Briefyn bağlantısı ancak o zaman çıkar (takvime aktarma işi, `SPEC.md`; Ulaş, 2026-11-20);
   - KVKK aydınlatma metni ve kullanım sözleşmesi. `Privacy` sayfasındaki metin taslak, hukuken gözden geçirilmiş metinle değişmeli;
   - Zoom Aşama 3: Marketplace beta onayı, başka hesapların bağlanabilmesi. Herkese açık kalıcı adres ve gizlilik metni gerektirdiği için yayınla birlikte (Ulaş, 2026-10-22);
   - Microsoft for Startups Founders Hub başvurusu, yayından hemen önce (kredi 90 gün);
   - Groq'ta sıfır veri saklama (Zero Data Retention) açılır (Ulaş, console.groq.com > Data Controls). Groq varsayılanda istek içeriğini saklamıyor, ama hata ve kötüye kullanım incelemesi için 30 güne kadar kayıt tutabiliyor; bu ayar o kaydı da kapatıyor (`console.groq.com/docs/your-data`, 2026-11-21). Sözleşmede "uygun müşteriler" deniyor; ücretsiz hesapta açılıp açılmadığı ekranda görülür.
2. **Konuşmacı ayırmayı açma kararı** (Ulaş): kod hazır. `TRANSCRIBER=azure` ise yüklenen dosya Azure Speech'te konuşmacılar ayrılarak yazıya dökülür; toplantı sayfasında konuşmacılara ad verilir, yapay zekâ ad önerir, aynı kişinin satırları birleşir. Zoom kayıtları her durumda Groq + Zoom'un adlarıyla gider. Varsayılan hâlâ Groq (`docker-compose.yml`, `.env.example`'da boş). Kalan:
   - Ulaş gerçek bir kayıtla deneyip Azure'u varsayılan yapıp yapmamaya karar verir (Azure for Students hesabı, 2026-11-17);
   - açılmadan önce Azure Speech'in ses ve dökümü saklamadığı, eğitimde kullanmadığı belgesinden doğrulanır (AGENTS.md kuralı). 2026-11-21'de bakıldı:
     - saklama: hızlı yazıya dökmenin sesi ve dökümü cevaptan sonra saklamadığı, konuşmacı ayırmanın ses izini işten sonra attığı yazıyor;
     - kullanım: Microsoft'un veri koruma eki (DPA) müşteri verisini yalnız hizmeti vermek için kullanacağını söylüyor;
     - eksik: "eğitimde kullanılmaz" diye açık bir cümle bulunamadı;
     - kontrol edilmedi: DPA'nın Azure for Students hesabına uygulanıp uygulanmadığı;
   - karar ADR'ye yazılır (ADR-0014 madde 6 Azure'u "ayrı iş" diye bırakmıştı); varsayılan değişirse `docker-compose.yml` ve `.env.example` güncellenir;
   - açılırsa arayüz yenileme konuşmacı adlarını hesaba katar.
3. **Taze göz ve kırıcı ajan denemesi:** ikisi de yazıldı, Briefyn'de hiç denenmedi. Önce netleşecek: taze göz kayıt mı olur, test hesabıyla mı girer; hangi ses dosyasını yükler (`.taze-goz/` içine konur); AI motorunun bir koşudaki maliyeti; kırıcı ajan için Docker yığınında ayrı test veritabanı (yoksa ajan durur).
4. **Arayüz yenileme** (`/spec` ile başlar). Kararlar (Ulaş, 2026-11-21):
   - Tailwind v4 + daisyUI 5;
   - renkli ve canlı görünüm: koyu tema yok, zemin bembeyaz değil, petrol/camgöbeği mavisi tonları;
   - logo A (ses dalgası not satırına dönüşüyor);
   - tanıtım adresleri `/tr/...` önekli, sayfa adları İngilizce;
   - sırayla gidilir, önce ana sayfa (scroll-craft); menüdeki alt sayfalar "yakında" sayfası olarak açılır;
   - her sayfa masaüstü ve mobil hâliyle birlikte biter;
   - ana sayfadaki güven sözü belgelere dayanır:
     - "toplantılarınız yapay zekâ eğitiminde kullanılmaz" denebilir. Groq'un sözleşmesi girdi ve çıktıyla eğitimi yasaklıyor (madde 4.2);
     - "hiç saklanmaz" denmez, çünkü döküm bizde şifreli saklanıyor (ADR-0002) ve Groq 30 güne kadar kayıt tutabiliyor;
     - doğru söz: ses analizden sonra silinir, döküm şifreli saklanır.
   
   İlk adım `SPEC.md`. Ana sayfadan sonra sıradakiler: arşive sor sohbet ekranı, ardından konuşmacı adlarıyla toplantı sayfası. Araştırma notu: Beyin `knowledge/concepts/briefyn-fireflies-arastirmasi.md`.
5. **Dış araçlara aktarma** (`/spec` ile başlar): takvim adımı bitti (e-postayla takvim daveti, ADR-0015). Kalanlar: Slack/Teams'e özet; Trello/Jira'ya aksiyon kullanıcı isteyince; Microsoft Graph ile takvim hesabı bağlama yayından sonra (kurumsal hesapta yönetici onayı istiyor). Takvim için açık kalan: Gmail / Google Takvim'de davet ve takvim dosyası denenmedi (`docs/raporlar/2026-11-20-takvim-daveti-deneme.md`). Takvim spec'inin 6. ölçütü (indirilen dosyanın Outlook web ve Google Takvim'de açılması) de denenmedi.
6. **Arşive soru sor doğruluğu:**
   - kararı sonradan değişen konuda arama eski kararı öne alabiliyor (altın soru Q06);
   - henüz olmamış bir olayın "sonucu" sorulunca cevap uydurmaması için istem kuralı (Q44);
   - uzun tek konuşmacı satırı bölününce konuşmacı adı sonraki parçaya taşınmıyor.
7. **Kayıt sayfası hesabın var olduğunu söylüyor** (Ulaş): var olan adresle kayıt denemesi "bu adresle hesap var" diyor; şifremi unuttum sayfasındaki gizleme orada boşa çıkıyor. Kapatmak için kaydın e-posta doğrulamalı olması gerekir (kayıt e-postayla tamamlanır, cevap her adreste aynı). Karar Ulaş'ın (2026-11-21 incelemesi).
8. **Ücretli plan** (Ulaş, 2026-11-21): şimdilik herkes ücretsiz yükler, amaç kullanıcı toplamak. Ödeme için şirket kurulunca yapılacak:
   - ücretsiz ve ücretli planın ayrımı;
   - "Kayıt yükle"ye basınca dosya seçilmeden önce bir plan tablosu çıkar. Tabloda bütün özellikler satır satır yazar: Ücretsiz sütununda ✓/✗, Ücretli sütununda hepsi ✓. Ana seçenek "Ücretsiz devam et", yanında ücretli plana geçiş;
   - Fiyatlandırma sayfası. O zamana kadar menüdeki Fiyatlandırma "yakında" sayfasıdır.
