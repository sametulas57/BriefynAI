using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingWeb.Services.Ai;

public sealed class AzureSpeechException(string message) : Exception(message);

public sealed class AzureTranscriber : ITranscriber
{
    private readonly HttpClient _http;
    private readonly IAudioTools _audio;
    private readonly string _url;
    private readonly string _apiKey;
    private readonly string _definition;
    private readonly int _maxAttempts;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _now;

    public AzureTranscriber(HttpClient http, IAudioTools audio, string endpoint, string apiKey,
        string locale = "tr-TR", int maxSpeakers = 10, int maxAttempts = 5,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTimeOffset>? now = null)
    {
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        _http = http;
        _audio = audio;
        _url = endpoint.TrimEnd('/') + "/speechtotext/transcriptions:transcribe?api-version=2025-10-15";
        _apiKey = apiKey;
        _maxAttempts = maxAttempts;
        _delay = delay ?? Task.Delay;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _definition = PythonJson.Dumps(new JsonObject
        {
            ["locales"] = new JsonArray(locale),
            ["diarization"] = new JsonObject { ["enabled"] = true, ["maxSpeakers"] = maxSpeakers }
        });
    }

    public async Task<string> TranscribeAsync(string audioPath, string? speakersVtt, CancellationToken ct)
    {
        // VTT is ignored: Zoom recordings use Groq with their supplied names.
        var work = Path.Combine(Path.GetTempPath(), "briefyn_azure_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var mono = await _audio.ToMonoFlacAsync(audioPath, Path.Combine(work, "mono.flac"), ct);
            if (await _audio.DurationSecondsAsync(mono, ct) >= 5 * 3600)
                throw new AzureSpeechException("recording is longer than Azure's 5 hour limit");
            using var response = await PostAsync(mono, ct);
            if ((int)response.StatusCode != 200)
                throw new AzureSpeechException($"transcription failed: HTTP {(int)response.StatusCode}");
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return FormatTranscript(result.RootElement);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<HttpResponseMessage> PostAsync(string mono, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            using (var request = new HttpRequestMessage(HttpMethod.Post, _url))
            {
                request.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);
                using var multipart = new MultipartFormDataContent();
                var file = new StreamContent(File.OpenRead(mono));
                file.Headers.ContentType = new MediaTypeHeaderValue("audio/flac");
                multipart.Add(file, "audio", "mono.flac");
                multipart.Add(new StringContent(_definition), "definition");
                request.Content = multipart;
                response = await _http.SendAsync(request, ct);
            }
            if ((int)response.StatusCode == 429 && attempt < _maxAttempts)
            {
                var header = response.Headers.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null;
                var wait = RetryAfter.Seconds(header, _now());
                response.Dispose();
                await _delay(wait, ct);
                continue;
            }
            if ((int)response.StatusCode >= 500 && attempt < _maxAttempts)
            {
                response.Dispose();
                await _delay(TimeSpan.FromSeconds(2 * attempt), ct);
                continue;
            }
            return response;
        }
    }

    private static string Text(JsonElement phrase) =>
        phrase.TryGetProperty("text", out var text) ? PyText.Strip(text.GetString()!) : "";

    private static string FormatTranscript(JsonElement result)
    {
        if (!result.TryGetProperty("phrases", out var phrases) || phrases.ValueKind == JsonValueKind.Null || phrases.GetArrayLength() == 0)
            return result.TryGetProperty("combinedPhrases", out var combined)
                ? string.Join(" ", combined.EnumerateArray().Select(Text).Where(text => text.Length > 0)) : "";

        Dictionary<string, string> labels = [];
        List<(string? Speaker, string Text)> pairs = [];
        foreach (var phrase in phrases.EnumerateArray().OrderBy(phrase =>
            phrase.TryGetProperty("offsetMilliseconds", out var offset) ? offset.GetDouble() : 0))
        {
            var text = Text(phrase);
            if (text.Length == 0) continue;
            // Raw JSON keeps numeric and string IDs distinct. Unlike Python, 2 and 2.0 are different keys.
            var speaker = phrase.TryGetProperty("speaker", out var id) && id.ValueKind != JsonValueKind.Null ? id.GetRawText() : null;
            if (speaker != null && !labels.ContainsKey(speaker)) labels[speaker] = $"Konuşmacı {labels.Count + 1}";
            pairs.Add((speaker == null ? null : labels.GetValueOrDefault(speaker), text));
        }
        return labels.Count < 2 ? string.Join(" ", pairs.Select(pair => pair.Text)) : TranscriptText.FormatLines(pairs);
    }
}
