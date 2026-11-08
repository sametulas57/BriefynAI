# Yol haritası (backlog)

Açık işler ve planlanan özellikler. Sıradaki iş buradan seçilir; o anki iş `SPEC.md` ve `tasks/`'ta yürür, bitince maddesi buradan çıkarılır. Bitenler git geçmişinde, kalıcı kararlar `docs/kararlar/`'da, kısıtlar `AGENTS.md` "Kurallar"da.

## Açık işler (sırayla)
1. **Yayına kalanlar:** yayın öncesi gizli anahtarların yenilenmesi, mail alan adı, prod compose, KVKK metinleri ve standart sözleşme, Microsoft for Startups Founders Hub başvurusu (yayından hemen önce; kredi 90 gün), **Zoom Aşama 3** (Marketplace beta onayı, başka hesapların bağlanabilmesi; kalıcı herkese açık adres ve gizlilik metni gerektirdiği için yayınla birlikte, Ulaş 2026-10-22).
2. **Ertelenen özellik:** yüklenen dosyada konuşmacı ayırma (Groq Whisper desteklemiyor; yayında Azure Speech ile). Zoom'dan alınan kayıtta adlar 1b ile geliyor.
3. **Sonra (küçük işler incelemesi, 2026-10-22):** avatar yalnız JPEG imzasına bakıyor; imzalı bozuk dosya kabul edilir (yalnız kişinin kendi fotoğrafı bozuk görünür, resim olarak sunulduğu için kod çalışmaz). İstenirse görüntüyü açıp doğrulamak ya da `X-Content-Type-Options: nosniff`. İpucu yankısı süzgeci, ipucundaki 6+ terimi tam o sırayla söyleyen gerçek sözü de siler (çok düşük olasılık).
4. **Sonra (seriler işi, 2026-10-22):** sabit tünel seçildi (Ulaş, 2026-10-22): ngrok ücretsiz sabit adres; adres yalnız `.env`'de (`ZOOM_REDIRECT_URI`). Kalan (Ulaş): Marketplace'teki üç adres, ilk denemede Zoom'u yeniden bağlama. Tarif `docs/raporlar/2026-10-22-sabit-tunel.md`; alan adı alınınca Cloudflare adlı tünel. Seriler işinin kapsam dışı bıraktıkları (takvimli gündem e-postası, "bitti denenleri kapat" önerisi, seriler arası birleştirme vb.).

- **Sonra (doğrulama betikleri incelemesi, 2026-11-09):** `.githooks/pre-commit` önerisi `git stash -u --keep-index` olmalı (izlenmeyen dosya kalınca hook yine kalıyor); `scripts/dogrula.sh` pytest süzgeci `xfailed`'ı da hata sayıyor; `--hash` boş dönerse damga kontrolsüz basılıyor, boşsa KALDI demeli; yeni klonda `ai_engine/.venv` kurulumu AGENTS.md'de yazmıyor.
- **Sonra (arşive soru sor, 2026-10-24):** sonradan değişen kararda aramanın yeni kararı öne alması (Q06); henüz olmamış olayın "sonucu" sorusu için istem kuralı (Q44); uzun tek konuşmacı satırı bölününce konuşmacı adının sonraki parçaya taşınması; işlem onayı her commit'te indeks neslini artırıyor (performans); ad önceliği önek eşleşmesi ("Ali" ~ "alışveriş").

## Planlanan büyük özellikler (Ulaş, 2026-10-21; her biri `/spec` ile başlar)
1. **Dış araçlara aktarma:** öncelik takvim. En yaygın iki takvim Google Takvim ve Outlook / Microsoft 365. İlk adım hesap bağlamadan: tarihli aksiyon için `.ics` dosyası (ikisi de açar) + "Google Takvim'e ekle" bağlantısı. Sonra Slack/Teams'e özet, Trello/Jira'ya aksiyon.
2. **Konuşmacı ayırma:** yukarıdaki ertelenen iş. Ucuz yol adayı: Zoom bulut kaydının kendi dökümü konuşmacı adı taşıyabilir; Zoom araştırmasında bakılır.
