# Görevler: Python servisini kaldırma, yapay zekâ hattı .NET içinde

Plan: `tasks/python-kaldirma/plan.md` · Karar: ADR-0018 · Kaynak kod: `ai_engine/` (silinene kadar tek doğru kaynak; birebir taşınır, davranış değişmez).
Odaklı test komutu: `dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~Ai"` (görevdeki test sınıfının adıyla daraltılır). Her görev commit'ten önce `scripts/verify.sh`'den (hook) geçer.
İşçi brifi: önce `incremental-implementation` ve `test-driven-development` skill'lerini oku; yalnız değiştirdiğin yerin testlerini koş, tam takımı commit hook'u koşar.

## Aşama 1: Temel

- [ ] **Görev 1: `IAiEngine` arayüzü, bugünkü HTTP kodu arkasında** (S-M)
  - Kabul: `MeetingWeb/Services/Ai/IAiEngine.cs` üç işi tanımlıyor (`ProcessMeetingAsync`, `SummarizeAsync`, `AnswerAsync`). `PythonAiEngine` bugünkü HTTP kodunu taşıyor (`MeetingProcessor.RunPythonPipelineAsync`, `RequestSummaryAsync`, `AskService`'teki istek). `MeetingProcessor` ve `AskService` artık `IHttpClientFactory`'yi yapay zekâ için kullanmıyor, yalnız `IAiEngine`'i. `no_speech` ve özetsiz döküm yolları aynı. `AiEngine:Mode` ayarı var, yalnız `python` tanınıyor; bilinmeyen değerde uygulama açılışta anlaşılır hatayla duruyor.
  - Doğrula: test dosyalarına dokunmadan bütün mevcut testler geçiyor (`StubPythonApiHandler` HTTP düzeyinde çalışmaya devam ediyor).
  - Bağımlılık: yok.
  - Dosyalar: `MeetingWeb/Services/Ai/IAiEngine.cs`, `MeetingWeb/Services/Ai/PythonAiEngine.cs`, `MeetingWeb/Services/MeetingProcessor.cs`, `MeetingWeb/Services/ArchiveSearch/AskService.cs`, `MeetingWeb/Program.cs`.

- [ ] **Görev 2: Groq istemcisi ve `ILlmClient`** (M)
  - Kabul: `GroqClient` (`HttpClient`, taban adres `https://api.groq.com/openai/v1`) `providers/groq_client.py`'nin üç işini yapıyor: `ChatJsonAsync` (katı `json_schema`, `temperature` 0.2, `qwen/` modellerinde `reasoning_format: hidden`; 400 gelirse `json_object` ve şema sistem metninin sonuna eklenerek bir kez daha), `TranscribeAsync` (`json`) ve `TranscribeSegmentsAsync` (`verbose_json`), ikisi de `temperature` 0. En fazla 5 deneme: 429'da `Retry-After` kadar (saniye ya da tarih; yoksa 10 sn, en fazla 60 sn), 5xx'te `2 × deneme` saniye (`groq_client.py` ve `http_retry.py` ile aynı). İstek zaman aşımı 300 sn. `ILlmClient.CompleteJsonAsync(system, messages, schema)` ve görev → model seçimi `llm.py`'deki tabloyla aynı: ortam değişkenleri `SUMMARIZER`/`AI_SUMMARY_MODEL`, `SPEAKER_SUGGESTER`/`AI_SPEAKERS_MODEL`, `ANSWERER`/`AI_ANSWER_MODEL`, boş değer varsayılan sayılıyor, varsayılan modeller koddaki gibi. Anahtar yoksa anlaşılır hata. Hata mesajlarında istek ya da cevap içeriği yok.
  - Doğrula: `test_llm.py` (7) ve `test_providers.py`'deki Groq istemcisi testleri sahte `HttpMessageHandler` ile C#'ta; istek gövdesi (model, mesajlar, `response_format`) Python'un gönderdiğiyle aynı.
  - Bağımlılık: yok (Görev 1'le paralel yapılabilir).
  - Dosyalar: `MeetingWeb/Services/Ai/GroqClient.cs`, `MeetingWeb/Services/Ai/LlmClient.cs`, `MeetingWeb/Services/Ai/AiOptions.cs`, `MeetingWeb/Program.cs`, `MeetingWeb.Tests/Ai/GroqClientTests.cs`.

### Kontrol A
- [ ] `scripts/verify.sh` GEÇTİ; push, CI yeşil.
- [ ] Yerel yığında bir kayıt yüklendi, özet geldi (Python yolu, davranış değişmedi).

## Aşama 2: Dilimler

- [ ] **Görev 3: Arşive soru sor .NET'te, altın set verisi taşınır** (M)
  - Kabul: `ArchiveAnswerer` `answerer.py`'nin istemini, şemasını, `_checked` doğrulamasını ve `NOT_FOUND` cevabını birebir taşıyor. İstek sınırları `api_server.py`'deki gibi (40 parça, 24.000 karakter, kimlik deseni, 500 karakter soru, boş soru reddi). Hata olursa günlüğe yalnız hata türü yazılıyor, sonuç "meşgul". `DotNetAiEngine.AnswerAsync` bunu kullanıyor. `DotNetAiEngine` Görev 7'ye kadar yalnız testlerde kuruluyor; uygulama `AiEngine:Mode=dotnet` ile açılmayı reddediyor (yarım yol kullanıcıya ulaşmaz). `ai_engine/tests/altin` `MeetingWeb.Tests/Altin/`'e taşındı, `GoldSet.cs` ve `test_altin_set.py` yeni yeri okuyor. `AskMeasurement` `BRIEFYN_OLCUM_URL` yerine doğrudan `DotNetAiEngine` ile ölçebiliyor (anahtar yoksa koşmuyor).
  - Doğrula: `test_answer.py` (13) ve `test_api_security.py`'deki cevap sınırı testleri C#'ta; `test_altin_set.py` (6) C#'a taşındı; günlükte soru, parça ya da cevap geçmediğini `CapturingLoggerProvider` ile sınayan test; istem ve şema metni Python dosyasıyla karakter karakter aynı (tek seferlik betik çıktısı görev notuna yazılır).
  - Bağımlılık: 1, 2.
  - Dosyalar: `MeetingWeb/Services/Ai/ArchiveAnswerer.cs`, `MeetingWeb/Services/Ai/DotNetAiEngine.cs`, `MeetingWeb.Tests/Ai/ArchiveAnswererTests.cs`, `MeetingWeb.Tests/Infrastructure/GoldSet.cs`, `MeetingWeb.Tests/Features/AskMeasurement.cs`, `MeetingWeb.Tests/Altin/*`.

- [ ] **Görev 4: Toplantı özeti .NET'te** (M)
  - Kabul: `MeetingSummarizer` `groq_summarizer.py`'yi birebir taşıyor: istem, şema, `split_text` (12.000 karakter), parçaları birleştirme istemi (`MERGE_INSTRUCTION`), `to_backend_format` (aksiyon satırı biçimi dahil). `DotNetAiEngine.SummarizeAsync` hazır; özet hatasında günlüğe döküm yazılmıyor.
  - Doğrula: `test_providers.py`'deki özet testleri C#'ta; uzun döküm parçalara bölünüp birleştiriliyor; istem metinleri Python'la karakter karakter aynı.
  - Bağımlılık: 2.
  - Dosyalar: `MeetingWeb/Services/Ai/MeetingSummarizer.cs`, `MeetingWeb/Services/Ai/DotNetAiEngine.cs`, `MeetingWeb.Tests/Ai/MeetingSummarizerTests.cs`.

- [ ] **Görev 5: Döküm temizleme ve konuşmacı önerisi .NET'te** (M)
  - Kabul: `TranscriptText` `speakers.py`'yi taşıyor: Türkçe normalleştirme, uydurma altyazı listesi ve `is_only_hallucinated_captions`, istem yankısı ayıklama, `clean_segment_text`, `clean_plain_chunk`, VTT okuma (en fazla 2 MB, bozuk UTF-8'de yok sayma), `assign_speakers`, `format_lines`. `SpeakerSuggester` `speaker_suggester.py`'yi taşıyor (24.000 karakter sınırı, `Konuşmacı N` etiketleri); hata verirse boş öneri döner, günlüğe içerik yazılmaz.
  - Doğrula: `test_speakers.py` (3) ve `test_speaker_suggester.py` (17) C#'ta.
  - Bağımlılık: 2.
  - Dosyalar: `MeetingWeb/Services/Ai/TranscriptText.cs`, `MeetingWeb/Services/Ai/SpeakerSuggester.cs`, `MeetingWeb.Tests/Ai/TranscriptTextTests.cs`, `MeetingWeb.Tests/Ai/SpeakerSuggesterTests.cs`.

- [ ] **Görev 6: Ses işleme (ffmpeg) ve Groq yazıya dökme .NET'te** (M)
  - Kabul: `IAudioTools`/`FfmpegAudioTools` `audio.py`'nin beş işini yapıyor (mono FLAC 16 kHz, `ffprobe` ile süre, `silencedetect=noise=-35dB:d=0.4` orta noktaları, `PlanCuts`, `Cut`). Argümanlar yalnız `ProcessStartInfo.ArgumentList` ile, kabuk yok. Her çağrının zaman aşımı var, aşılınca süreç öldürülüyor. `GroqTranscriber` `groq_transcriber.py`'yi taşıyor (60 sn parçalar, teknik terim istemi, VTT varsa parça zamanlarıyla konuşmacı atama, yoksa düz metin). Geçici klasör hata olsa da siliniyor. `MeetingWeb/Dockerfile`'a ffmpeg, `test.yml`'a `sudo apt-get install -y ffmpeg`, README'ye geliştirme makinesi için `brew install ffmpeg` eklendi.
  - Doğrula: `PlanCuts` ve yazıya dökme döngüsü sahte `IAudioTools` ile (`test_providers.py`'deki karşılıkları); tek bir gerçek ffmpeg testi birkaç saniyelik üretilmiş sesi böler ve süresini okur (ffmpeg yoksa açık mesajla başarısız olur); boşluk ve tırnak içeren dosya adıyla argüman testi.
  - Bağımlılık: 2, 5.
  - Dosyalar: `MeetingWeb/Services/Ai/AudioTools.cs`, `MeetingWeb/Services/Ai/GroqTranscriber.cs`, `MeetingWeb.Tests/Ai/AudioToolsTests.cs`, `MeetingWeb.Tests/Ai/GroqTranscriberTests.cs`, `MeetingWeb/Dockerfile`, `.github/workflows/test.yml`, `README.md`.

- [ ] **Görev 10: Azure konuşmacı ayırma .NET'te** (S-M) · Ulaş'ın kararı (2026-11-22): taşınır, varsayılan Groq kalır
  - Kabul: `AzureTranscriber` `azure_transcriber.py`'yi taşıyor (mono FLAC, 5 saat sınırı, `diarization` tanımı, 429/5xx yeniden deneme, `Konuşmacı N` etiketleri, tek konuşmacıda düz metin). `TRANSCRIBER=azure` yalnız yüklenen dosyalarda; Zoom kayıtları her durumda Groq.
  - Doğrula: `test_azure_transcriber.py` (12) C#'ta.
  - Bağımlılık: 2, 5, 6 (mono FLAC ve süre için `IAudioTools`; sıra: Görev 6'dan sonra, Görev 7'den önce; Görev 7 iki yolu birden kurar).
  - Dosyalar: `MeetingWeb/Services/Ai/AzureTranscriber.cs`, `MeetingWeb/Services/Ai/DotNetAiEngine.cs`, `MeetingWeb.Tests/Ai/AzureTranscriberTests.cs`.

- [ ] **Görev 7: Kaydın tamamı .NET'te: `ProcessMeetingAsync`** (S-M)
  - Kabul: `DotNetAiEngine.ProcessMeetingAsync` `api_server.py`'deki `process_meeting` sırasını izliyor: VTT varsa Groq (ve Zoom adları), yoksa `TRANSCRIBER` ayarı (`groq` varsayılan, `azure` Görev 10'daki sınıf); döküm boşsa ya da yalnız uydurma altyazıysa "konuşma yok"; konuşmacı önerisi yalnız VTT yokken; özet hata verirse döküm özetsiz döner. `AiEngine:Mode=dotnet` ile üç işin hepsi .NET'te çalışıyor.
  - Doğrula: `test_api_security.py`'deki `process-meeting` davranış testleri (no_speech, özetsiz döküm, VTT boyutu) C#'ta, sahte sağlayıcılarla; servisler arası anahtar ve yol testleri gereksiz, nedeni görev notunda.
  - Bağımlılık: 3, 4, 5, 6, 10.
  - Dosyalar: `MeetingWeb/Services/Ai/DotNetAiEngine.cs`, `MeetingWeb/Program.cs`, `MeetingWeb.Tests/Ai/DotNetAiEngineTests.cs`.

### Kontrol B: iki yolun karşılaştırması
- [ ] `scripts/verify.sh` GEÇTİ; push, CI yeşil.
- [ ] Deneme kaydı (`~/Desktop/ULAS/Toplantı_ses_kaydi.m4a`, Ulaş'ın izniyle denemede kullanılıyor) önce `python`, sonra `dotnet` yoluyla işlendi; döküm, özet ve konuşmacı önerisi yan yana yazıldı. Model rastgeleliğinden gelen fark dışında fark yok, varsa nedeni bulundu.
- [ ] Zoom konuşmacı dosyalı bir kayıt `dotnet` yoluyla işlendi, adlar doğru satırlarda.
- [ ] `TRANSCRIBER=azure` ile deneme kaydı iki yoldan geçti, konuşmacı etiketleri aynı (Azure anahtarı `.env`'de yoksa bu madde atlanır ve nedeni yazılır).
- [ ] Arşive soru sor: altın setten 5 soru `dotnet` yoluyla soruldu, sonuç Ekim ölçümüyle aynı sınıfta (bulamadım / kaynaklı cevap).
- [ ] Sonuç Ulaş'a raporlandı, onay alındı.

## Aşama 3: Python'u kaldırma

- [ ] **Görev 8: Varsayılan `dotnet`, testler sahte `IAiEngine` ile** (M)
  - Kabul: `AiEngine:Mode` ayarı ve `PythonAiEngine` silindi, kayıtlı tek uygulama `DotNetAiEngine`. `StubPythonApiHandler` yerine `FakeAiEngine` aynı ayar adlarıyla (`Status`, `Body`, `BeforeRespond`, `CallCount`, `LastBody`); test dosyalarındaki değişiklik bununla sınırlı. `AiEngineClientTests` silindi (servisler arası anahtar artık yok). `Program.cs`'teki `PythonApi` istemcisi ve `appsettings.json`'daki `PythonApiSettings` kalktı.
  - Doğrula: bütün testler geçiyor; `git grep -i python MeetingWeb MeetingWeb.Tests` boş (ya da kalanların nedeni yazılı).
  - Bağımlılık: Kontrol B.
  - Dosyalar: `MeetingWeb/Program.cs`, `MeetingWeb/appsettings.json`, `MeetingWeb/Services/Ai/PythonAiEngine.cs` (silinir), `MeetingWeb.Tests/Infrastructure/StubPythonApiHandler.cs` → `FakeAiEngine.cs`, `MeetingWeb.Tests/Infrastructure/BriefynFactory.cs`, `MeetingWeb.Tests/Security/AiEngineClientTests.cs` (silinir), stub'ı kullanan 10 test dosyası.

- [ ] **Görev 9: `ai_engine`, Python araçları ve servis silinir** (M)
  - Kabul: `ai_engine/`, `pyproject.toml` silindi. `docker-compose.yml`'da `ai_engine` servisi yok; `GROQ_KEY`, `TRANSCRIBER`, `AZURE_*`, `ANSWERER`, `AI_*_MODEL` `web` servisine geçti; `AI_ENGINE_API_KEY` her yerden kalktı (`.env.example` dahil). `scripts/verify.sh`'te `.venv` kontrolü, ruff, mypy, pytest yok; son satır yalnız .NET sayısını yazıyor. `test.yml`'da Python kurulumu ve `pip-audit` yok; `codeql.yml`'da `python` dili yok. README'deki mimari çizim ve kurulum adımları güncel. `.gitignore`'daki Python satırları duruyor (zararsız) ya da temizlendi, hangisi seçildiyse yazıldı.
  - Doğrula: `scripts/verify.sh` GEÇTİ; `docker compose down && docker compose up --build` ile yığın ayağa kalkıyor, `briefyn-ai` konteyneri yok.
  - Bağımlılık: 8.
  - Dosyalar: `ai_engine/` (silinir), `pyproject.toml`, `docker-compose.yml`, `.env.example`, `scripts/verify.sh`, `.github/workflows/test.yml`, `.github/workflows/codeql.yml`, `README.md`.

### Kontrol C: bitti
- [ ] `scripts/verify.sh` GEÇTİ; push, CI yeşil.
- [ ] Temiz yığında tarayıcıdan: kayıt yüklendi, özet ve döküm geldi; özeti yeniden oluşturma çalıştı; arşive bir soru soruldu.
- [ ] `Backlog.md`'den bu madde çıkarıldı; ADR-0018'in "Sonuçlar"ı gerçekleşenle karşılaştırıldı.
- [ ] Açılan sunucular kapatıldı.
- [ ] Ulaş'a hatırlatıldı: `/agent-skills:review python kaldırma işi`.
