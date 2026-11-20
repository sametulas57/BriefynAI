# Backlog

Yapılacak her iş burada, tek listede, önem sırasıyla. Sıradaki iş en üstten seçilir; o anki iş `SPEC.md` ve `tasks/`'ta yürür, bitince maddesi buradan çıkarılır. Yeni iş listenin sonuna eklenir; sırayı Ulaş değiştirir. Bitenler git geçmişinde, kalıcı kararlar `docs/decisions/`'da, kısıtlar `AGENTS.md`'de.

1. **Yayın hazırlığı:**
   - gizli anahtarların yenilenmesi;
   - alan adı (site ve mail). Alınınca yerel Zoom denemeleri ngrok'tan Cloudflare adlı tünele geçer; tarif `docs/raporlar/2026-10-22-sabit-tunel.md`;
   - prod compose (bugün yalnız `docker-compose.yml` var);
   - herkese açık adres belli olunca `App:PublicUrl` ayarı doldurulur; takvim davetindeki Briefyn bağlantısı ancak o zaman çıkar (takvime aktarma işi, `SPEC.md`; Ulaş, 2026-11-20);
   - KVKK aydınlatma metni ve kullanım sözleşmesi. `Privacy` sayfasındaki metin taslak, hukuken gözden geçirilmiş metinle değişmeli;
   - Zoom Aşama 3: Marketplace beta onayı, başka hesapların bağlanabilmesi. Herkese açık kalıcı adres ve gizlilik metni gerektirdiği için yayınla birlikte (Ulaş, 2026-10-22);
   - Microsoft for Startups Founders Hub başvurusu, yayından hemen önce (kredi 90 gün).
2. **Konuşmacı ayırma** (`/spec` ile başlar): yüklenen dosyada kim ne dedi ayrılmıyor; Groq Whisper desteklemiyor. Zoom'dan alınan kayıtta adlar zaten geliyor. Adaylar: Azure Speech (Azure for Students hesabı var, 2026-11-17; yalnız Speech için mi kullanılacağına Ulaş deneyip karar verecek) ya da Zoom bulut kaydının kendi dökümü.
3. **Taze göz ve kırıcı ajan denemesi:** ikisi de yazıldı, Briefyn'de hiç denenmedi. Önce netleşecek: taze göz kayıt mı olur, test hesabıyla mı girer; hangi ses dosyasını yükler (`.taze-goz/` içine konur); AI motorunun bir koşudaki maliyeti; kırıcı ajan için Docker yığınında ayrı test veritabanı (yoksa ajan durur).
4. **Arayüz yenileme** (`/spec` ile başlar): karar bekliyor (Ulaş): bileşen kütüphanesi ve görünüm yönü (sade, sıcak ya da koyu). Öneri Tailwind v4 + daisyUI 5: HTML sınıflarıyla çalıştığı için Razor Pages'e uyar, React gerektirmez; Tailwind eklemek gerekir.
5. **Dış araçlara aktarma** (`/spec` ile başlar): takvim adımı bitti (e-postayla takvim daveti, ADR-0015). Kalanlar: Slack/Teams'e özet; Trello/Jira'ya aksiyon kullanıcı isteyince; Microsoft Graph ile takvim hesabı bağlama yayından sonra (kurumsal hesapta yönetici onayı istiyor). Takvim için açık kalan: Gmail / Google Takvim'de davet ve takvim dosyası denenmedi (`docs/raporlar/2026-11-20-takvim-daveti-deneme.md`).
6. **Arşive soru sor doğruluğu:**
   - kararı sonradan değişen konuda arama eski kararı öne alabiliyor (altın soru Q06);
   - henüz olmamış bir olayın "sonucu" sorulunca cevap uydurmaması için istem kuralı (Q44);
   - uzun tek konuşmacı satırı bölününce konuşmacı adı sonraki parçaya taşınmıyor.
