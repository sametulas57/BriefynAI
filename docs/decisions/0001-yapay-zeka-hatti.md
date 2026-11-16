# ADR-0001: Yapay zekâ hattı: Groq, sağlayıcıdan bağımsız arayüz

- Durum: Kabul edildi · Tarih: 2026-10-17

## Bağlam
Yerelde faster-whisper + Ollama (qwen2.5:14b) yavaştı (4 dk ses ≈ 124 sn) ve sunucuda GPU/disk ister. Bütçe sıfıra yakın; toplantı verisi şirket verisi, eğitimde kullanılmamalı ve saklanmamalı.

## Karar
Yazıya dökme Groq whisper-large-v3 (≈60 sn parçalar), özet Groq qwen3.8-27b + JSON şema. Groq'ta ZDR açık. Sağlayıcılar `TRANSCRIBER`/`SUMMARIZER` ile değiştirilebilir arayüz arkasında. Yerel model yok.

## Sonuçlar
Uçtan uca ≈8 sn. Konuşmacı ayırma yok (Groq desteklemiyor; yayında Azure ile). Ücretsiz katman dakika sınırına takılabilir (retry var).

## Kaynak
iç proje notu
