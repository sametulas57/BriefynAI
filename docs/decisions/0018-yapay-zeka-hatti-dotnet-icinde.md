# ADR-0018: Yapay zekâ hattı .NET içinde; Python servisi kaldırılır

- Durum: Kabul edildi · Tarih: 2026-11-22 · ADR-0001 ve ADR-0014'ün kararları sürer; yalnız uygulandıkları yer değişir

## Bağlam
`ai_engine` (Python, FastAPI) Haziran 2026'da yerel model için kuruldu: faster-whisper ve Ollama aynı konteynerde çalışıyordu (`fe8acf6`, `a97dd17`). ADR-0001 ile (2026-10-17) yerel model bırakıldı, Groq'a geçildi. O günden beri servis model çalıştırmıyor: bağımlılığı `fastapi`, `uvicorn`, `python-multipart`, `httpx`; işi Groq ve Azure'a HTTP isteği atmak, `ffmpeg` çalıştırmak ve JSON doğrulamak.

Bugün bunun bedeli: ikinci bir dil ve test düzeni (ruff, mypy, pytest, CI'da Python), ikinci bir konteyner, iki servis arasında ortak gizli anahtar (`AI_ENGINE_API_KEY`) ve fazladan bir HTTP adımı. Proje yayında değil ve ücretsiz ya da çok ucuz barındırma hedefleniyor (Ulaş, 2026-11-22); tek süreç bu hedefe daha uygun.

## Karar
1. `ai_engine`'in yaptığı her şey `MeetingWeb/Services/Ai/` altına C# olarak taşınır; Python servisi, konteyneri ve Python araçları kaldırılır.
2. **Yazıya dökme Groq'ta kalır** (Ulaş, 2026-11-22). Ses yine sessiz anlardan yaklaşık 60 saniyelik parçalara bölünür: 2026-10-16 denemesinde tek parça gönderimde Türkçe harfler bozulmuştu, ayrıca Groq'un ücretsiz katman sınırı 25 MB (dev katmanı 100 MB; `console.groq.com/docs/speech-to-text`, 2026-11-22). Deneme kaydı 4 dakikada 2,4 MB tuttu; 40 dakikayı aşan toplantı bu sınırı geçer.
3. **ffmpeg kalır**, C#'tan ayrı program olarak çağrılır (`Process`, argümanlar `ArgumentList` ile, kabuk kullanılmaz). Web imajına kurulur. Saf .NET kütüphaneleri Linux'ta m4a/mp4 çözmediği, sarmalayıcılar (FFMpegCore vb.) yine ffmpeg'i çağırdığı için yeni paket eklenmez.
4. **Model istemcisi düz `HttpClient`** ile yazılır, önünde ADR-0014'teki tek arayüz (`ILlmClient`: sistem metni + mesajlar + JSON şeması → doğrulanmış JSON). Yeni paket yok; Groq'un `json_schema` biçimi Groq'a özgü olduğu için ara kütüphane bir şey kazandırmaz.
5. Ortam değişkenlerinin adları ve kodda görünen varsayılan modeller aynen korunur (`GROQ_KEY`, `TRANSCRIBER`, `AI_SUMMARY_MODEL`, `AI_SPEAKERS_MODEL`, `AI_ANSWER_MODEL`, `SUMMARIZER`, `SPEAKER_SUGGESTER`, `ANSWERER`, `AI_TRANSCRIBE_MODEL`).
6. İstem metinleri, JSON şemaları, döküm temizleme kuralları ve cevap doğrulaması Python'dakiyle aynı kalır; taşıma davranış değiştirmez. Değişiklik ayrı iş olarak, ölçümle (ADR-0014 madde 4) yapılır.
7. Toplantı işleme Hangfire arka plan işinde kalır (bugün de orada: `Upload.cshtml.cs`, `ProcessMeetingJob`); web isteği beklemez.
8. Model ölçümü (`AskMeasurement`) C# testinde kalır, yalnız elle çalıştırılır; gerçek Groq çağırdığı için commit öncesi kontrolde koşmaz. Altın set verisi `MeetingWeb.Tests` altına taşınır.

## Sonuçlar
- Bir konteyner, bir dil, servisler arası gizli anahtar ve 8000 portu kalkar. `verify.sh` ve CI'dan Python adımları çıkar.
- Ses işleme web sürecinin içinde çalışır: ffmpeg'in CPU ve bellek kullanımı web konteynerine biner. ffmpeg takılırsa süreç zaman aşımıyla öldürülür, geçici dosyalar her durumda silinir.
- Geliştirme makinesinde ve CI'da ffmpeg kurulu olmalı (macOS'ta `brew install ffmpeg`).
- Azure ile konuşmacı ayırma (`TRANSCRIBER=azure`, Backlog madde 2) da taşınır (Ulaş, 2026-11-22): aynı arayüzün ikinci uygulaması. Varsayılan Groq kalır; Azure'u varsayılan yapma kararı Backlog madde 2'de ayrı.
- Taşıma sırasında `AiEngine:Mode` ayarı (`python` | `dotnet`) iki yolu yan yana tutar; aynı kayıtla karşılaştırma yapıldıktan sonra Python yolu ve bu ayar silinir.

## Kaynak
iç proje notu · `docs/raporlar/2026-10-16-ai-hatti-arastirmasi.md` · Groq speech-to-text belgesi (2026-11-22)
