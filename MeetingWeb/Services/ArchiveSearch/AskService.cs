using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeetingWeb.Services;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Services.ArchiveSearch;

public sealed class AskService(
    IArchiveSearchService search,
    AskRateLimiter limiter,
    IHttpClientFactory clients,
    IConfiguration configuration,
    IOptions<AskOptions> options,
    ILogger<AskService> logger) : IAskService
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private static readonly char[] EdgePunctuation = ['.', ',', ';', ':', '!', '?', '…', '"', '\''];

    public async Task<AskResult> AskAsync(string? userId, int workspaceId, AskRequest request, CancellationToken ct = default)
    {
        var question = request.Question?.Trim() ?? "";
        if (question.Length == 0) return Result(AskStatus.EmptyQuestion);
        if (question.Length > 500) return Result(AskStatus.QuestionTooLong);
        if (!limiter.TryAccept(userId)) return Result(AskStatus.RateLimited);

        var settings = options.Value;
        var found = await search.SearchAsync(userId, workspaceId,
            new ArchiveQuery(question, request.Scope, request.TargetId, request.From, request.To,
                Top: Math.Clamp(settings.MaxPassages, 1, 40)), ct);
        if (found.MeetingsSearched == 0) return Result(AskStatus.EmptyArchive);
        if (found.Passages.Count == 0) return Result(AskStatus.NotFound, found.MeetingsSearched);

        var sent = new List<(ArchivePassage Passage, string Text)>();
        var total = 0;
        var contextLimit = Math.Min(settings.ContextCharacters, 24000);
        foreach (var passage in found.Passages)
        {
            var text = passage.Text[..Math.Min(passage.Text.Length, 4000)];
            if (sent.Count > 0 && total + text.Length > contextLimit) break;
            sent.Add((passage, text));
            total += text.Length;
        }

        var payload = new
        {
            soru = question,
            parcalar = sent.Select(item => new
            {
                id = item.Passage.Id,
                toplanti = item.Passage.MeetingTitle[..Math.Min(item.Passage.MeetingTitle.Length, 300)],
                tarih = item.Passage.MeetingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                tur = item.Passage.Kind switch
                {
                    ArchivePassageKind.Topic => "konu",
                    ArchivePassageKind.Decision => "karar",
                    ArchivePassageKind.Pending => "bekleyen",
                    ArchivePassageKind.Task => "aksiyon",
                    _ => "döküm"
                },
                metin = item.Text
            }).ToArray()
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)));
            var client = clients.CreateClient(MeetingProcessor.PythonApiClient);
            var endpoint = configuration["PythonApiSettings:AnswerEndpoint"] ?? "api/answer";
            using var response = await client.PostAsJsonAsync(endpoint, payload, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Archive answer failed: HTTP {StatusCode}", (int)response.StatusCode);
                return Result(AskStatus.Busy, found.MeetingsSearched);
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("yeterli_bilgi", out var enough) ||
                !root.TryGetProperty("cevap", out var answerField) ||
                !root.TryGetProperty("kanitlar", out var evidenceField) ||
                enough.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                answerField.ValueKind != JsonValueKind.String || evidenceField.ValueKind != JsonValueKind.Array)
                throw new JsonException("Invalid answer shape");

            var answer = answerField.GetString()?.Trim();
            if (enough.ValueKind != JsonValueKind.True || string.IsNullOrEmpty(answer))
                return Result(AskStatus.NotFound, found.MeetingsSearched);

            var byId = sent.ToDictionary(item => item.Passage.Id);
            var seen = new HashSet<(string Id, string Quote)>();
            var sources = new List<AskSource>();
            foreach (var evidence in evidenceField.EnumerateArray())
            {
                if (evidence.ValueKind != JsonValueKind.Object ||
                    !evidence.TryGetProperty("id", out var idField) || idField.ValueKind != JsonValueKind.String ||
                    !evidence.TryGetProperty("alinti", out var quoteField) || quoteField.ValueKind != JsonValueKind.String)
                    continue;
                var id = idField.GetString()!;
                var quote = quoteField.GetString()!.Trim();
                if (!byId.TryGetValue(id, out var item)) continue;
                var normalized = Normalize(quote);
                if (normalized.Length < 5 || !Normalize(item.Text).Contains(normalized, StringComparison.Ordinal) ||
                    !seen.Add((id, normalized))) continue;
                var p = item.Passage;
                sources.Add(new AskSource(id, p.MeetingId, p.MeetingTitle, p.MeetingDate, p.Kind, quote));
            }
            if (sources.Count == 0) return Result(AskStatus.NotFound, found.MeetingsSearched);
            var uncertain = root.TryGetProperty("belirsiz_not", out var note) && note.ValueKind == JsonValueKind.String
                ? note.GetString()?.Trim() : null;
            return new AskResult(AskStatus.Answered, found.MeetingsSearched, answer,
                string.IsNullOrEmpty(uncertain) ? null : uncertain, sources);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException or NotSupportedException)
        {
            logger.LogWarning("Archive answer failed: {Error}", ex.GetType().Name);
            return Result(AskStatus.Busy, found.MeetingsSearched);
        }
    }

    private static AskResult Result(AskStatus status, int meetings = 0) => new(status, meetings, null, null, []);

    private static string Normalize(string value)
    {
        var text = value.Normalize(NormalizationForm.FormC)
            .Replace('’', '\'').Replace('‘', '\'').Replace('‛', '\'').Replace('`', '\'').Replace('´', '\'')
            .Replace('“', '"').Replace('”', '"').Replace('„', '"');
        return Spaces.Replace(text, " ").Trim().Trim(EdgePunctuation).Trim().ToLower(Turkish);
    }
}
