# Uygulama planı: Python servisini kaldırma, yapay zekâ hattı .NET içinde

Karar: ADR-0018 · Görev listesi: `tasks/python-kaldirma/todo.md` · Durum: onaylandı, uygulanıyor (Ulaş, 2026-11-22)

Bu plan ayrı klasörde, çünkü `tasks/plan.md` ve `tasks/todo.md` arayüz yenileme 1'e ait ve son kontrolünde 4 açık madde var (`tasks/todo.md:104-107`). O iş kapanınca bu dosyalar `tasks/` köküne taşınabilir.

## Özet

`ai_engine`'in üç ucu (`/api/process-meeting`, `/api/summarize`, `/api/answer`) C# sınıflarına taşınır. Davranış değişmez: istemler, şemalar, döküm temizleme ve cevap doğrulaması birebir aynı kalır. Taşıma sırasında iki yol yan yana durur (`AiEngine:Mode = python | dotnet`, varsayılan `python`); her dilim kendi testleriyle gelir, en sonda aynı kayıtla iki yol karşılaştırılır, sonra Python ve ayar silinir.

Sıra: önce arada bir arayüz (bugünkü HTTP kodu arkasına girer, hiçbir şey değişmez), sonra Groq istemcisi, sonra küçükten büyüğe üç dilim (arşive soru sor → özet → kaydın tamamı), en son silme.

## Kodda baktıklarım (planın dayanağı)

- **Python'un dışarıya verdiği:** `api_server.py`'de üç uç. `process-meeting` sesi ve isteğe bağlı Zoom konuşmacı dosyasını (VTT, en fazla 2 MB) alır; döküm boşsa ya da yalnız uydurma altyazıysa 422 `no_speech`; özet hata verirse dökümü özetsiz döner (`success: false`); konuşmacı önerisi yalnız VTT yokken. `answer` istek sınırları: en fazla 40 parça, toplam 24.000 karakter, parça kimliği `^T\d{1,9}-[KRBAD]\d{1,4}$`, soru en fazla 500 karakter; hata olursa günlüğe yalnız hata türü yazılır, 503 `busy`.
- **Python içi:** `providers/` 11 dosya, 1074 satır: `groq_client` (sohbet JSON'u: katı `json_schema`, olmazsa `json_object`; 429/5xx yeniden deneme, `Retry-After`), `llm` (görev → sağlayıcı/model, ortam değişkeninden), `groq_summarizer` (uzun dökümü 12.000 karakterlik parçalara bölüp birleştirme, `to_backend_format`), `speaker_suggester`, `answerer` (`_checked` doğrulaması), `speakers` (VTT okuma, uydurma altyazı ve istem yankısı ayıklama, konuşmacı atama, satır biçimi), `audio` (ffmpeg: mono FLAC 16 kHz, `ffprobe` süre, `silencedetect`, `plan_cuts`, `cut`), `groq_transcriber`, `azure_transcriber`, `http_retry`.
- **Python testleri:** 105 test fonksiyonu, parametreli hâlleriyle 163 test (`test_providers` 27, `test_api_security` 20, `test_speaker_suggester` 17, `test_answer` 13, `test_azure_transcriber` 12, `test_llm` 7, `test_altin_set` 6, `test_speakers` 3). ffmpeg'i çağırmıyorlar, ses yardımcılarını sahteyle değiştiriyorlar.
- **C# tarafı:** Python'u üç yer çağırıyor: `MeetingProcessor.RunPythonPipelineAsync` ve `RequestSummaryAsync` (`MeetingWeb/Services/MeetingProcessor.cs:245-330`), `AskService` (`MeetingWeb/Services/ArchiveSearch/AskService.cs:72`). İstemci `Program.cs:92-110`'da (`PythonApi`, `X-Api-Key`). İşleme Hangfire işinde (`Upload.cshtml.cs:125`).
- **C# testleri:** Python'un yerine `StubPythonApiHandler` geçiyor (`MeetingWeb.Tests/Infrastructure/StubPythonApiHandler.cs`), 10 test dosyası ve `BriefynFactory` bunu kullanıyor. `AiEngineClientTests` iki servis arasındaki anahtarı sınıyor. `AskMeasurement` gerçek model ölçümünü yalnız `BRIEFYN_OLCUM_URL` verilince yapıyor. `GoldSet.cs:141` altın seti `ai_engine/tests/altin`'den okuyor.
- **Doğrulama ve CI:** `scripts/verify.sh` `.venv` yoksa duruyor, ruff/mypy/pytest koşuyor. `.github/workflows/test.yml` Python kuruyor ve `pip-audit` koşuyor; `codeql.yml` `python` dilini tarıyor.
- **ffmpeg:** bu makinede kurulu (`/opt/homebrew/bin/ffmpeg`). Web imajında yok (`MeetingWeb/Dockerfile` yalnız .NET SDK). CI'da kurulu olup olmadığına bakılmadı; görevde açıkça kurulur.

## Mimari kararlar

1. **Arayüz:** `IAiEngine` üç iş: `ProcessMeetingAsync(sesYolu, vttYolu?)`, `SummarizeAsync(döküm)`, `AnswerAsync(soru, parçalar)`. Dönüş tipleri bugünkü JSON sözleşmesinin C# karşılığı (`AiApiResponse`, `AiSummaryData` zaten var). `MeetingProcessor` ve `AskService` yalnız bunu bilir.
2. **İki uygulama, bir ayar:** `PythonAiEngine` (bugünkü HTTP kodu, taşınmış hâli) ve `DotNetAiEngine`. `AiEngine:Mode` hangisinin kayıtlı olacağını seçer. Varsayılan `python`; Görev 7'de `dotnet` seçilebilir olur, Kontrol B'de iki yol aynı kayıtla karşılaştırılır, Görev 8'de ayar ve `PythonAiEngine` silinir, Görev 9'da Python servisi.
3. **Klasör:** `MeetingWeb/Services/Ai/`. Sağlayıcıya özgü ayrıntı (Groq'un şema biçimi, yeniden deneme) yalnız `GroqClient`'ta kalır (ADR-0014 madde 1).
4. **ffmpeg:** `IAudioTools` arayüzü arkasında, `Process` ile; argümanlar `ArgumentList`, kabuk yok; zaman aşımında süreç öldürülür; geçici klasör `finally`'de silinir. Birim testleri sahte `IAudioTools` kullanır (Python testleri gibi); gerçek ffmpeg'i tek bir küçük test sınar.
5. **Testler:** her Python testi ya C#'a taşınır ya da neden gereksiz kaldığı görevde yazılır (ör. servisler arası anahtar testleri). İstem ve şema metinleri Python dosyalarıyla karakter karakter karşılaştırılır (Python silinmeden önce, tek seferlik).
6. **Yeni paket yok** (ADR-0018 madde 3-4).

## Görev listesi

Ayrıntılı kabul ölçütleri `todo.md`'de.

### Aşama 1: Temel
- [x] Görev 1: `IAiEngine` arayüzü, bugünkü HTTP kodu arkasında (S-M)
- [x] Görev 2: Groq istemcisi ve `ILlmClient` (M)

### Kontrol A
- [ ] `verify.sh` GEÇTİ; davranış değişmedi (Python yolu varsayılan)

### Aşama 2: Dilimler
- [x] Görev 3: Arşive soru sor .NET'te, altın set veri taşıması (M)
- [x] Görev 4: Toplantı özeti .NET'te (M)
- [x] Görev 5: Döküm temizleme ve konuşmacı önerisi .NET'te (M)
- [x] Görev 6: Ses işleme (ffmpeg) ve Groq yazıya dökme .NET'te (M)
- [ ] Görev 10: Azure konuşmacı ayırma .NET'te (S-M); numarası eski, sırası burası
- [ ] Görev 7: Kaydın tamamı .NET'te: `ProcessMeetingAsync` (S-M)

### Kontrol B: iki yolun karşılaştırması
- [ ] Aynı kayıt iki yoldan geçti, fark yazıldı; Ulaş onayladı

### Aşama 3: Python'u kaldırma
- [ ] Görev 8: Varsayılan `dotnet`, testler sahte `IAiEngine` ile (M)
- [ ] Görev 9: `ai_engine`, Python araçları ve servis silinir (M)

### Kontrol C: bitti
- [ ] `verify.sh` GEÇTİ, CI yeşil, temiz `docker compose up` ile uçtan uca deneme

## Riskler ve önlemler

| Risk | Etki | Önlem |
|------|------|-------|
| İstem, şema ya da temizleme kuralı taşırken farklılaşır, özet kalitesi sessizce düşer | Yüksek | Metinler Python dosyalarıyla karakter karakter karşılaştırılır; Python testleri birebir taşınır; Kontrol B'de aynı kayıt iki yoldan geçer |
| ffmpeg web sürecinde takılır ya da CPU'yu doldurur | Orta | Zaman aşımı ve süreç öldürme; Hangfire işi tek tek koşar; geçici dosyalar `finally`'de silinir |
| Kabuk enjeksiyonu (dosya adı ffmpeg argümanına girer) | Yüksek | Yalnız `ArgumentList`; dosya adı zaten GUID'li depolama adı (`AudioStorage`); test eder |
| Toplantı verisi günlüğe ya da hata mesajına sızar | Yüksek | Python'daki kural korunur: cevap ve özet hatasında yalnız hata türü; testle (`CapturingLoggerProvider` var) |
| Groq ücretsiz katman dakika sınırı | Orta | Python'daki 429/`Retry-After` yeniden deneme birebir taşınır |
| CI'da ffmpeg yok | Düşük | `test.yml`'a kurulum adımı; gerçek ffmpeg testi ffmpeg yoksa açık mesajla başarısız olur, sessizce atlanmaz |
| 10 test dosyası `StubPythonApiHandler`'a bağlı | Orta | Görev 8'de sahte `IAiEngine` aynı ayar adlarını (`Status`, `Body`, `BeforeRespond`, `CallCount`, `LastBody`) taşır; test dosyalarında değişiklik en aza iner |

## Açık sorular (Ulaş)

1. ~~Azure konuşmacı ayırma taşınsın mı?~~ Karar (Ulaş, 2026-11-22): taşınır, varsayılan Groq kalır (Görev 10). Azure'u varsayılan yapmak Backlog madde 2'de ayrı karar.
2. Barındırma ve PostgreSQL bu planın dışında (Backlog'a ayrı madde olarak eklendi). Bu plan hangi barındırma seçilirse seçilsin gerekli.

## İş dağıtımı

Uygulama Codex'e (`gpt-6.1-sol`). Her brifte: önce `incremental-implementation` ve `test-driven-development` skill'lerini oku; yalnız değiştirdiğin yerin testlerini koş, tam takımı commit hook'u koşar; Python dosyaları kaynak, birebir taşı, davranış değiştirme. Kontrol B'deki karşılaştırma ve Ulaş'a rapor Claude'da.
