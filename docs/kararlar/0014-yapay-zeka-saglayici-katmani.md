# ADR-0014: Yapay zekâ sağlayıcı katmanı: göreve göre model, seçim ölçümle

- Durum: Kabul edildi · Tarih: 2026-10-24 · ADR-0001'i genişletir (onu geçersiz kılmaz)

## Bağlam
Arşive soru sor, özetten ağır bir iş: doğru alıntıyı seçmek, bilmediğinde "bulamadım" demek, dökümdeki talimatlara uymamak. Hangi modelin bunu Türkçede iyi yaptığına dair bağımsız ölçüm yok. Groq'un iyi modellerinden biri önizlemede ve Groq modelleri birkaç ayda bir kapatıyor. Sağlayıcı araştırması AWS Bedrock'u öneriyor ama Ulaş şimdilik ücretsiz yolu seçti: yapay zekâ Groq, yazıya dökme Azure Speech (öğrenci hesabı doğrulaması bekliyor, kesin değil). Bugün yazıya dökme ve özet zaten `TRANSCRIBER`/`SUMMARIZER` arayüzü arkasında (ADR-0001), ama özet kodu doğrudan Groq istemcisine bağlı.

## Karar
1. **Ortak model istemcisi:** `ai_engine/providers` içinde tek bir sohbet arayüzü (`LlmClient`: sistem metni + mesajlar + JSON şeması → doğrulanmış sözlük). Groq bunun ilk uygulaması. Özet ve soru-cevap bu arayüzü kullanır, sağlayıcıya özgü ayrıntı (Groq'un `json_schema` biçimi, Bedrock'un araç/şema biçimi, yeniden deneme) yalnız uygulamanın içinde kalır.
2. **Göreve göre seçim:** her görev kendi sağlayıcısını ve modelini ortam değişkeninden alır: `SUMMARIZER` + `AI_SUMMARY_MODEL`, yeni `ANSWERER` + `AI_ANSWER_MODEL`. Özet ve soru-cevap farklı modellerde çalışabilir. Varsayılanlar kodda görünür (bugünkü düzen).
3. **Model dışı her şey modelden bağımsız:** arama (C#'ta, ekip başına bellek indeksi), yetki filtresi, alıntı doğrulama, hız sınırı ve ekran sağlayıcıyı bilmez. Model değişince yalnız `providers` modülü ve ortam değişkeni değişir.
4. **Seçim ölçümle:** deneme soru seti (altın set) ve ölçüm betiği ilk gün kurulur. Model ancak bu setle karşılaştırılarak seçilir ya da değiştirilir; sonuç `docs/raporlar/` altına yazılır.
5. **Yeni sağlayıcı yalnız ölçümle girer:** Bedrock, Mistral ya da başkası, ancak altın sette karşılaştırılacağı zaman uygulanır. Ücretli hesap açmak Ulaş'ın onayı (ADR-0001 kuralı sürer).
6. **Azure Speech:** hesap açılınca `TRANSCRIBER=azure` olarak ayrı bir işte eklenir; bu ADR'nin kapsamı dışında, arayüz hazır.

## Sonuçlar
- Groq bir modeli kapatırsa ya da kalite yetmezse geçiş bir modül + bir ortam değişkeni; geri kalan kod ve testler aynı kalır.
- Sağlayıcıların ortak paydası kullanılır: akarak gelen cevap ilk sürümde yok (Groq'ta katı JSON ile birlikte çalışmıyor). Cevap tek parça gelir, ekranda bekleme göstergesi olur.
- Ölçüm Groq'un ücretsiz günlük sınırını (200K token) aşabilir: ya iki güne yayılır ya Developer planına geçilir (kullandıkça ödeme, bu hacimde sentler).

## Kaynak
iç proje notu · iç proje notu
