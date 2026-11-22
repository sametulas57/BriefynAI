using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeetingWeb.Models;

namespace MeetingWeb.Services.Ai;

// Görev 7'ye kadar DI'a kayıtlı değil; yalnız testler ve AskMeasurement kurar (ADR-0018).
public sealed class DotNetAiEngine(ILlmClientFactory llms, ILogger<DotNetAiEngine> logger) : IAiEngine
{
    private static readonly Regex PartId = new(@"^T\d{1,9}-[KRBAD]\d{1,4}\z");
    private static readonly Regex Date = new(@"^\d{4}-\d{2}-\d{2}\z");

    public Task<AiApiResponse?> ProcessMeetingAsync(string audioPath, string? speakersPath, CancellationToken ct = default) =>
        throw new NotSupportedException("Görev 7'de taşınacak");

    public Task<AiSummaryData?> SummarizeAsync(string transcript, CancellationToken ct = default) =>
        throw new NotSupportedException("Görev 4'te taşınacak");

    public async Task<JsonElement> AnswerAsync(string question, IReadOnlyList<AiAnswerPassage> passages, CancellationToken ct = default)
    {
        if (Length(question) > 500 || question.Trim().Length == 0 || passages.Count > 40 ||
            passages.Any(p => !PartId.IsMatch(p.Id) || Length(p.Toplanti) > 300 || !Date.IsMatch(p.Tarih) ||
                Length(p.Tur) > 20 || Length(p.Metin) is < 1 or > 4000) ||
            passages.Sum(p => Length(p.Metin)) > 24000)
            throw new HttpRequestException("AI engine rejected the request", null, HttpStatusCode.UnprocessableEntity);

        try
        {
            var answerer = new ArchiveAnswerer(llms.Create(LlmTask.Answer));
            return await answerer.AnswerAsync(question.Trim(), passages, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError("Answer failed: {Error}", ex.GetType().Name);
            throw new HttpRequestException("AI engine answer failed", null, HttpStatusCode.ServiceUnavailable);
        }
    }

    private static int Length(string text) => text.EnumerateRunes().Count();
}
