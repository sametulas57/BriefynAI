# Görevler: Python servisini kaldırma, yapay zekâ hattı .NET içinde

Plan: `tasks/python-kaldirma/plan.md` · Karar: ADR-0018 · Kaynak kod: `ai_engine/` (silinene kadar tek doğru kaynak; birebir taşınır, davranış değişmez).
Odaklı test komutu: `dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~Ai"` (görevdeki test sınıfının adıyla daraltılır). Her görev commit'ten önce `scripts/verify.sh`'den (hook) geçer.
İşçi brifi: önce `incremental-implementation` ve `test-driven-development` skill'lerini oku; yalnız değiştirdiğin yerin testlerini koş, tam takımı commit hook'u koşar.

## Aşama 1: Temel

- [x] **Görev 1: `IAiEngine` arayüzü, bugünkü HTTP kodu arkasında** (S-M)
  - Kabul: `MeetingWeb/Services/Ai/IAiEngine.cs` üç işi tanımlıyor (`ProcessMeetingAsync`, `SummarizeAsync`, `AnswerAsync`). `PythonAiEngine` bugünkü HTTP kodunu taşıyor (`MeetingProcessor.RunPythonPipelineAsync`, `RequestSummaryAsync`, `AskService`'teki istek). `MeetingProcessor` ve `AskService` artık `IHttpClientFactory`'yi yapay zekâ için kullanmıyor, yalnız `IAiEngine`'i. `no_speech` ve özetsiz döküm yolları aynı. `AiEngine:Mode` ayarı var, yalnız `python` tanınıyor; bilinmeyen değerde uygulama açılışta anlaşılır hatayla duruyor.
  - Doğrula: test dosyalarına dokunmadan bütün mevcut testler geçiyor (`StubPythonApiHandler` HTTP düzeyinde çalışmaya devam ediyor).
  - Bağımlılık: yok.
  - Dosyalar: `MeetingWeb/Services/Ai/IAiEngine.cs`, `MeetingWeb/Services/Ai/PythonAiEngine.cs`, `MeetingWeb/Services/MeetingProcessor.cs`, `MeetingWeb/Services/ArchiveSearch/AskService.cs`, `MeetingWeb/Program.cs`.

- [x] **Görev 2: Groq istemcisi ve `ILlmClient`** (M)
  - Kabul: `GroqClient` (`HttpClient`, taban adres `https://api.groq.com/openai/v1`) `providers/groq_client.py`'nin üç işini yapıyor: `ChatJsonAsync` (katı `json_schema`, `temperature` 0.2, `qwen/` modellerinde `reasoning_format: hidden`; 400 gelirse `json_object` ve şema sistem metninin sonuna eklenerek bir kez daha), `TranscribeAsync` (`json`) ve `TranscribeSegmentsAsync` (`verbose_json`), ikisi de `temperature` 0. En fazla 5 deneme: 429'da `Retry-After` kadar (saniye ya da tarih; yoksa 10 sn, en fazla 60 sn), 5xx'te `2 × deneme` saniye (`groq_client.py` ve `http_retry.py` ile aynı). İstek zaman aşımı 300 sn. `ILlmClient.CompleteJsonAsync(system, messages, schema)` ve görev → model seçimi `llm.py`'deki tabloyla aynı: ortam değişkenleri `SUMMARIZER`/`AI_SUMMARY_MODEL`, `SPEAKER_SUGGESTER`/`AI_SPEAKERS_MODEL`, `ANSWERER`/`AI_ANSWER_MODEL`, boş değer varsayılan sayılıyor, varsayılan modeller koddaki gibi. Anahtar yoksa anlaşılır hata. Hata mesajlarında istek ya da cevap içeriği yok.
  - Doğrula: `test_llm.py` (7) ve `test_providers.py`'deki Groq istemcisi testleri sahte `HttpMessageHandler` ile C#'ta; istek gövdesi (model, mesajlar, `response_format`) Python'un gönderdiğiyle aynı.
  - Bağımlılık: yok (Görev 1'le paralel yapılabilir).
  - Dosyalar: `MeetingWeb/Services/Ai/GroqClient.cs`, `MeetingWeb/Services/Ai/LlmClient.cs`, `MeetingWeb/Services/Ai/AiOptions.cs`, `MeetingWeb/Program.cs`, `MeetingWeb.Tests/Ai/GroqClientTests.cs`.

### Kontrol A
- [ ] `scripts/verify.sh` GEÇTİ; push, CI yeşil.
- [ ] Yerel yığında bir kayıt yüklendi, özet geldi (Python yolu, davranış değişmedi).

## Aşama 2: Dilimler

- [x] **Görev 3: Arşive soru sor .NET'te, altın set verisi taşınır** (M)
  - Kabul: `ArchiveAnswerer` `answerer.py`'nin istemini, şemasını, `_checked` doğrulamasını ve `NOT_FOUND` cevabını birebir taşıyor. İstek sınırları `api_server.py`'deki gibi (40 parça, 24.000 karakter, kimlik deseni, 500 karakter soru, boş soru reddi). Hata olursa günlüğe yalnız hata türü yazılıyor, sonuç "meşgul". `DotNetAiEngine.AnswerAsync` bunu kullanıyor. `DotNetAiEngine` Görev 7'ye kadar yalnız testlerde kuruluyor; uygulama `AiEngine:Mode=dotnet` ile açılmayı reddediyor (yarım yol kullanıcıya ulaşmaz). `ai_engine/tests/altin` `MeetingWeb.Tests/Altin/`'e taşındı, `GoldSet.cs` ve `test_altin_set.py` yeni yeri okuyor. `AskMeasurement` `BRIEFYN_OLCUM_URL` yerine doğrudan `DotNetAiEngine` ile ölçebiliyor (anahtar yoksa koşmuyor).
  - Doğrula: `test_answer.py` (13) ve `test_api_security.py`'deki cevap sınırı testleri C#'ta; `test_altin_set.py` (6) C#'a taşındı; günlükte soru, parça ya da cevap geçmediğini `CapturingLoggerProvider` ile sınayan test; istem ve şema metni Python dosyasıyla karakter karakter aynı (tek seferlik betik çıktısı görev notuna yazılır).
  - Not (2026-11-22): `SYSTEM` ve `json.dumps(SCHEMA, ensure_ascii=False)` Python'dan üretilip `MeetingWeb.Tests/Ai/Golden/`'a yazıldı; C# sabitleri bunlarla bayt bayt aynı (test + elle karşılaştırma). `test_answer_endpoint_needs_the_key` taşınmadı: .NET içinde servisler arası anahtar yok. Ölçüm: `BRIEFYN_OLCUM_MOTOR=dotnet` + `GROQ_API_KEY`.
  - Bağımlılık: 1, 2.
  - Dosyalar: `MeetingWeb/Services/Ai/ArchiveAnswerer.cs`, `MeetingWeb/Services/Ai/DotNetAiEngine.cs`, `MeetingWeb.Tests/Ai/ArchiveAnswererTests.cs`, `MeetingWeb.Tests/Infrastructure/GoldSet.cs`, `MeetingWeb.Tests/Features/AskMeasurement.cs`, `MeetingWeb.Tests/Altin/*`.

- [x] **Görev 4: Toplantı özeti .NET'te** (M)
  - Kabul: `MeetingSummarizer` `groq_summarizer.py`'yi birebir taşıyor: istem, şema, `split_text` (12.000 karakter), parçaları birleştirme istemi (`MERGE_INSTRUCTION`), `to_backend_format` (aksiyon satırı biçimi dahil). `DotNetAiEngine.SummarizeAsync` hazır; özet hatasında günlüğe döküm yazılmıyor.
  - Doğrula: `test_providers.py`'deki özet testleri C#'ta; uzun döküm parçalara bölünüp birleştiriliyor; istem metinleri Python'la karakter karakter aynı.
  - Not (2026-11-22): `SYSTEM`, `MERGE_INSTRUCTION`, şema, `split_text` çıktısı, birleştirme içeriği ve `to_backend_format` çıktısı Python'dan üretilip `MeetingWeb.Tests/Ai/Golden/summarizer_*`'a yazıldı; C# bunlarla bayt bayt aynı. Tek fark: model listelerden birini `null` döndürürse Python özeti `null` alanla geçiriyordu, C# özet hatası sayıyor (katı şema bunu zaten engelliyor).
  - Bağımlılık: 2.
  - Dosyalar: `MeetingWeb/Services/Ai/MeetingSummarizer.cs`, `MeetingWeb/Services/Ai/DotNetAiEngine.cs`, `MeetingWeb.Tests/Ai/MeetingSummarizerTests.cs`.

- [x] **Görev 5: Döküm temizleme ve konuşmacı önerisi .NET'te** (M)
  - Kabul: `TranscriptText` `speakers.py`'yi taşıyor: Türkçe normalleştirme, uydurma altyazı listesi ve `is_only_hallucinated_captions`, istem yankısı ayıklama, `clean_segment_text`, `clean_plain_chunk`, VTT okuma (en fazla 2 MB, bozuk UTF-8'de yok sayma), `assign_speakers`, `format_lines`. `SpeakerSuggester` `speaker_suggester.py`'yi taşıyor (24.000 karakter sınırı, `Konuşmacı N` etiketleri); hata verirse boş öneri döner, günlüğe içerik yazılmaz.
  - Doğrula: `test_speakers.py` (3) ve `test_speaker_suggester.py` (17) C#'ta.
  - Not (2026-11-23): Python'un metin anlamları (`strip`, `splitlines`, `split`, `isalnum`, `casefold`, `len`) `PyText`'te; Görev 3-4'teki `Trim` ve uzunluk hesapları da buna geçti. 502 vakalık diferansiyel altın set (`MeetingWeb.Tests/Ai/Golden/transcript_*`, `speaker_*`): girdiler sabit, beklenenler Python'dan üretildi, elle yeniden üretilip doğrulandı; bilinen fark yok. `test_chunk_offsets_are_added_before_matching` Görev 6'da.
  - Bağımlılık: 2.
  - Dosyalar: `MeetingWeb/Services/Ai/TranscriptText.cs`, `MeetingWeb/Services/Ai/SpeakerSuggester.cs`, `MeetingWeb.Tests/Ai/TranscriptTextTests.cs`, `MeetingWeb.Tests/Ai/SpeakerSuggesterTests.cs`.

- [x] **Görev 6: Ses işleme (ffmpeg) ve Groq yazıya dökme .NET'te** (M)
  - Kabul: `IAudioTools`/`FfmpegAudioTools` `audio.py`'nin beş işini yapıyor (mono FLAC 16 kHz, `ffprobe` ile süre, `silencedetect=noise=-35dB:d=0.4` orta noktaları, `PlanCuts`, `Cut`). Argümanlar yalnız `ProcessStartInfo.ArgumentList` ile, kabuk yok. Her çağrının zaman aşımı var, aşılınca süreç öldürülüyor. `GroqTranscriber` `groq_transcriber.py`'yi taşıyor (60 sn parçalar, teknik terim istemi, VTT varsa parça zamanlarıyla konuşmacı atama, yoksa düz metin). Geçici klasör hata olsa da siliniyor. `MeetingWeb/Dockerfile`'a ffmpeg, `test.yml`'a `sudo apt-get install -y ffmpeg`, README'ye geliştirme makinesi için `brew install ffmpeg` eklendi.
  - Doğrula: `PlanCuts` ve yazıya dökme döngüsü sahte `IAudioTools` ile (`test_providers.py`'deki karşılıkları); tek bir gerçek ffmpeg testi birkaç saniyelik üretilmiş sesi böler ve süresini okur (ffmpeg yoksa açık mesajla başarısız olur); boşluk ve tırnak içeren dosya adıyla argüman testi.
  - Not (2026-11-23): `PlanCuts` Python'dan üretilen 15 vakalık altın setle (`Golden/audio_plan_cuts.json`) ve `TechPrompt` bayt bayt (`Golden/transcriber_tech_prompt.txt`) aynı. Gerçek ffmpeg testi boşluk, tırnak ve `;` içeren yolla sesi çeviriyor, süresini okuyor, sessizliği buluyor ve kesiyor. Bilinen farklar: ffmpeg'e giden sayılarda Python'un `0.0`'ı yerine `0` (ffmpeg aynı okur); `AI_TRANSCRIBE_MODEL` tanımlı ama boşsa C# varsayılan modeli kullanıyor (Python boş model adı gönderirdi). Python'daki `plan_cuts` hedef 7,5 sn ve altında ilerlemeyen döngüye girebiliyor; kullanılan değer 60 sn olduğu için birebir bırakıldı. Groq cevabındaki `Trim` de `PyText.Strip`'e geçti.
  - Bağımlılık: 2, 5.
  - Dosyalar: `MeetingWeb/Services/Ai/AudioTools.cs`, `MeetingWeb/Services/Ai/GroqTranscriber.cs`, `MeetingWeb.Tests/Ai/AudioToolsTests.cs`, `MeetingWeb.Tests/Ai/GroqTranscriberTests.cs`, `MeetingWeb/Dockerfile`, `.github/workflows/test.yml`, `README.md`.

- [x] **Görev 10: Azure konuşmacı ayırma .NET'te** (S-M) · Ulaş'ın kararı (2026-11-22): taşınır, varsayılan Groq kalır
  - Kabul: `AzureTranscriber` `azure_transcriber.py`'yi taşıyor (mono FLAC, 5 saat sınırı, `diarization` tanımı, 429/5xx yeniden deneme, `Konuşmacı N` etiketleri, tek konuşmacıda düz metin). `TRANSCRIBER=azure` yalnız yüklenen dosyalarda; Zoom kayıtları her durumda Groq.
  - Doğrula: `test_azure_transcriber.py` (12) C#'ta.
  - Not (2026-11-23): `test_azure_transcriber.py`'nin ilk dokuz testi `AzureTranscriberTests`'te (33 vaka); fabrika ve `process_meeting` testleri Görev 7'de. Ortak `ITranscriber` arayüzü eklendi (`GroqTranscriber` ve `AzureTranscriber`). Bilinen farklar: konuşmacı kimliği ham JSON metniyle tutuluyor (Python `2` ile `2.0`'ı aynı sayardı; Azure tam sayı döndürüyor); `AZURE_SPEECH_MAX_SPEAKERS` sayıya fabrikada çevriliyor.
  - Bağımlılık: 2, 5, 6 (mono FLAC ve süre için `IAudioTools`; sıra: Görev 6'dan sonra, Görev 7'den önce; Görev 7 iki yolu birden kurar).
  - Dosyalar: `MeetingWeb/Services/Ai/AzureTranscriber.cs`, `MeetingWeb/Services/Ai/DotNetAiEngine.cs`, `MeetingWeb.Tests/Ai/AzureTranscriberTests.cs`.

- [x] **Görev 7: Kaydın tamamı .NET'te: `ProcessMeetingAsync`** (S-M)
  - Kabul: `DotNetAiEngine.ProcessMeetingAsync` `api_server.py`'deki `process_meeting` sırasını izliyor: VTT varsa Groq (ve Zoom adları), yoksa `TRANSCRIBER` ayarı (`groq` varsayılan, `azure` Görev 10'daki sınıf); döküm boşsa ya da yalnız uydurma altyazıysa "konuşma yok"; konuşmacı önerisi yalnız VTT yokken; özet hata verirse döküm özetsiz döner. `AiEngine:Mode=dotnet` ile üç işin hepsi .NET'te çalışıyor.
  - Doğrula: `test_api_security.py`'deki `process-meeting` davranış testleri (no_speech, özetsiz döküm, VTT boyutu) C#'ta, sahte sağlayıcılarla; servisler arası anahtar ve yol testleri gereksiz, nedeni görev notunda.
  - Not (2026-11-23): `TranscriberFactory` `build_transcriber`'ın karşılığı; `AiEngine:Mode` artık `python` ve `dotnet` kabul ediyor, varsayılan `python`. `test_api_security.py`'deki `process-meeting` davranış testleri ve `test_azure_transcriber.py`'deki fabrika testleri `DotNetAiEngineProcessTests` ve `TranscriberFactoryTests`'te. Taşınmayanlar: servisler arası anahtar testleri (.NET içinde anahtar yok), yükleme boyutu ve istemci dosya adı testleri (yükleme web tarafında; motor `AudioStorage`'daki GUID adlı dosyayı okuyor). Farklar: Python'un HTTP 413/500'ü yerine `InvalidDataException` (konuşmacı dosyası 2 MB'tan büyük) ve iç istisnasız `InvalidOperationException` (Hangfire'ın kaydına yalnız genel mesaj gider); boş `AZURE_SPEECH_MAX_SPEAKERS` 10 sayılıyor.
  - Bağımlılık: 3, 4, 5, 6, 10.
  - Dosyalar: `MeetingWeb/Services/Ai/DotNetAiEngine.cs`, `MeetingWeb/Program.cs`, `MeetingWeb.Tests/Ai/DotNetAiEngineTests.cs`.

### Kontrol B: iki yolun karşılaştırması
- [x] `scripts/verify.sh` GEÇTİ; push, CI yeşil.
- [x] Deneme kaydı önce `python`, sonra `dotnet` yoluyla işlendi; döküm üç durumda da birebir aynı, özetler model rastgeleliği kadar farklı. İstek baytlarındaki tek fark (UTF-8 yerine `\u` kaçışı) kapatıldı. Rapor: `docs/raporlar/2026-11-23-python-kaldirma-kontrol-b.md`.
- [x] Zoom konuşmacı dosyalı kayıt `dotnet` yoluyla işlendi, adlar doğru satırlarda. Gerçek Zoom VTT'si bulunmadığı için kayda göre zamanlanmış yapay VTT kullanıldı.
- [x] `TRANSCRIBER=azure` ile deneme kaydı iki yoldan geçti; döküm aynı, Azure tek konuşmacı buldu (iki yolda da düz metin).
- [x] Arşive soru sor: altın setten 5 soru `dotnet` yoluyla soruldu, sınıflar beklenenle aynı (3 kaynaklı cevap, 2 "bulamadım").
- [x] Sonuç raporlandı; onay: Ulaş kalan görevlerin onayını bu oturuma bıraktı (2026-11-23).

## Aşama 3: Python'u kaldırma

- [x] **Görev 8: Varsayılan `dotnet`, testler sahte `IAiEngine` ile** (M)
  - Kabul: `AiEngine:Mode` ayarı ve `PythonAiEngine` silindi, kayıtlı tek uygulama `DotNetAiEngine`. `StubPythonApiHandler` yerine `FakeAiEngine` aynı ayar adlarıyla (`Status`, `Body`, `BeforeRespond`, `CallCount`, `LastBody`); test dosyalarındaki değişiklik bununla sınırlı. `AiEngineClientTests` silindi (servisler arası anahtar artık yok). `Program.cs`'teki `PythonApi` istemcisi ve `appsettings.json`'daki `PythonApiSettings` kalktı.
  - Doğrula: bütün testler geçiyor; `git grep -i python MeetingWeb MeetingWeb.Tests` boş (ya da kalanların nedeni yazılı).
  - Not (2026-11-23): Kayıtlı tek motor `DotNetAiEngine`; `AiEngine:Mode`, `AiEngineOptions`, `PythonAiEngine`, `PythonApiSettings` kalktı. `FakeAiEngine` eski stub'ın ayar adlarını ve HTTP durum anlamlarını taşıyor; test dosyalarında değişiklik ad değişikliğiyle sınırlı. `AiEngineClientTests`'in konuşmacı dosyası testleri `MeetingProcessorAiTests`'e taşındı, anahtar testi kalktı; stub'ın yönlendirme testi stub'la birlikte silindi. `AskMeasurement` yalnız .NET motoruyla (`BRIEFYN_OLCUM=1` + `GROQ_API_KEY`), `scripts/olcum.sh` uvicorn'suz. `MeetingWeb` ve testlerde kalan "python" geçişleri `PyText`/`PythonJson` adları ve altın verinin kaynağını anlatan notlar.
  - Bağımlılık: Kontrol B.
  - Dosyalar: `MeetingWeb/Program.cs`, `MeetingWeb/appsettings.json`, `MeetingWeb/Services/Ai/PythonAiEngine.cs` (silinir), `MeetingWeb.Tests/Infrastructure/StubPythonApiHandler.cs` → `FakeAiEngine.cs`, `MeetingWeb.Tests/Infrastructure/BriefynFactory.cs`, `MeetingWeb.Tests/Security/AiEngineClientTests.cs` (silinir), stub'ı kullanan 10 test dosyası.

- [x] **Görev 9: `ai_engine`, Python araçları ve servis silinir** (M)
  - Kabul: `ai_engine/`, `pyproject.toml` silindi. `docker-compose.yml`'da `ai_engine` servisi yok; `GROQ_KEY`, `TRANSCRIBER`, `AZURE_*`, `ANSWERER`, `AI_*_MODEL` `web` servisine geçti; `AI_ENGINE_API_KEY` her yerden kalktı (`.env.example` dahil). `scripts/verify.sh`'te `.venv` kontrolü, ruff, mypy, pytest yok; son satır yalnız .NET sayısını yazıyor. `test.yml`'da Python kurulumu ve `pip-audit` yok; `codeql.yml`'da `python` dili yok. README'deki mimari çizim ve kurulum adımları güncel. `.gitignore`'daki Python satırları duruyor (zararsız) ya da temizlendi, hangisi seçildiyse yazıldı.
  - Doğrula: `scripts/verify.sh` GEÇTİ; `docker compose down && docker compose up --build` ile yığın ayağa kalkıyor, `briefyn-ai` konteyneri yok.
  - Not (2026-11-23): `ai_engine/` ve `pyproject.toml` silindi; yapay zekâ ayarları `web` servisinde; `AI_ENGINE_API_KEY` kalktı; `verify.sh`, CI, CodeQL ve dependabot'ta Python yok. `.gitignore`'daki Python satırları temizlendi (Python kodu kalmadı). `MeetingWeb.Tests/Ai/Golden` Python'dan üretilmiş sabit veri olarak kalıyor. Docker yığını Kontrol C'de denendi.
  - Bağımlılık: 8.
  - Dosyalar: `ai_engine/` (silinir), `pyproject.toml`, `docker-compose.yml`, `.env.example`, `scripts/verify.sh`, `.github/workflows/test.yml`, `.github/workflows/codeql.yml`, `README.md`.

### Kontrol C: bitti
- [ ] `scripts/verify.sh` GEÇTİ; push, CI yeşil.
- [ ] Temiz yığında tarayıcıdan: kayıt yüklendi, özet ve döküm geldi; özeti yeniden oluşturma çalıştı; arşive bir soru soruldu.
- [ ] `Backlog.md`'den bu madde çıkarıldı; ADR-0018'in "Sonuçlar"ı gerçekleşenle karşılaştırıldı.
- [ ] Açılan sunucular kapatıldı.
- [ ] Ulaş'a hatırlatıldı: `/agent-skills:review python kaldırma işi`.
