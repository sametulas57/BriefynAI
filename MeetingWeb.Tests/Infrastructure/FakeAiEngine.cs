using System.Net;
using System.Text.Json;
using MeetingWeb.Models;
using MeetingWeb.Services.Ai;

namespace MeetingWeb.Tests.Infrastructure;

public sealed class FakeAiEngine : IAiEngine
{
    private static readonly JsonSerializerOptions ResponseOptions = new() { PropertyNameCaseInsensitive = true };

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string Body { get; set; } = """
        {"success": true, "transkript": "test",
         "summary": {"toplanti_konusu": "Konu", "gorusulen_konular": ["a"], "alinan_kararlar": ["b"], "aksiyon_maddeleri": ["c"]}}
        """;
    public Action? BeforeRespond { get; set; }
    public int CallCount { get; private set; }
    public string? LastBody { get; private set; }
    public string? LastPath { get; private set; }
    public string? LastResponseBody { get; private set; }
    public string? LastAudioPath { get; private set; }
    public string? LastSpeakersPath { get; private set; }
    public string? LastSpeakersText { get; private set; }

    public async Task<AiApiResponse?> ProcessMeetingAsync(string audioPath, string? speakersPath, CancellationToken ct = default)
    {
        RecordRequest("api/process-meeting", JsonSerializer.Serialize(new { audioPath, speakersPath }));
        LastAudioPath = audioPath;
        LastSpeakersPath = speakersPath;
        LastSpeakersText = speakersPath != null && File.Exists(speakersPath) ? await File.ReadAllTextAsync(speakersPath, ct) : null;
        Respond();
        if (IsSuccess) return JsonSerializer.Deserialize<AiApiResponse>(Body, ResponseOptions);
        if (Status == HttpStatusCode.UnprocessableEntity)
        {
            try
            {
                using var error = JsonDocument.Parse(Body);
                if (error.RootElement.ValueKind == JsonValueKind.Object &&
                    error.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String
                    && detail.GetString() == "no_speech")
                    throw new NoSpeechException();
            }
            catch (JsonException) { /* Malformed responses remain general processing errors. */ }
        }
        throw new InvalidOperationException("AI engine processing failed.");
    }

    public Task<AiSummaryData?> SummarizeAsync(string transcript, CancellationToken ct = default)
    {
        RecordRequest("api/summarize", JsonSerializer.Serialize(new { transkript = transcript }));
        Respond();
        if (!IsSuccess) throw new HttpRequestException($"AI engine summary failed: HTTP {(int)Status}", null, Status);
        var ai = JsonSerializer.Deserialize<AiApiResponse>(Body, ResponseOptions);
        return Task.FromResult(ai is { Success: true } ? ai.Summary : null);
    }

    public Task<JsonElement> AnswerAsync(string question, IReadOnlyList<AiAnswerPassage> passages, CancellationToken ct = default)
    {
        var payload = new
        {
            soru = question,
            parcalar = passages.Select(passage => new
            {
                id = passage.Id,
                toplanti = passage.Toplanti,
                tarih = passage.Tarih,
                tur = passage.Tur,
                metin = passage.Metin
            }).ToArray()
        };
        RecordRequest("api/answer", JsonSerializer.Serialize(payload));
        Respond();
        if (!IsSuccess) throw new HttpRequestException($"AI engine answer failed: HTTP {(int)Status}", null, Status);
        using var document = JsonDocument.Parse(Body);
        return Task.FromResult(document.RootElement.Clone());
    }

    private bool IsSuccess => (int)Status is >= 200 and <= 299;

    private void RecordRequest(string path, string body)
    {
        CallCount++;
        LastPath = path;
        LastBody = body;
    }

    private void Respond()
    {
        BeforeRespond?.Invoke();
        LastResponseBody = Body;
    }
}
