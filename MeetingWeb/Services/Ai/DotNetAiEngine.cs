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

    public async Task<AiSummaryData?> SummarizeAsync(string transcript, CancellationToken ct = default)
    {
        try
        {
            return await new MeetingSummarizer(llms.Create(LlmTask.Summary), logger).SummarizeAsync(transcript, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError("Summarization failed: {Error}", ex.GetType().Name);
            throw new HttpRequestException("AI engine summary failed", null, HttpStatusCode.InternalServerError);
        }
    }

    public async Task<JsonElement> AnswerAsync(string question, IReadOnlyList<AiAnswerPassage> passages, CancellationToken ct = default)
    {
        if (PyText.Length(question) > 500 || PyText.Strip(question).Length == 0 || passages.Count > 40 ||
            passages.Any(p => !PartId.IsMatch(p.Id) || PyText.Length(p.Toplanti) > 300 || !Date.IsMatch(p.Tarih) ||
                PyText.Length(p.Tur) > 20 || PyText.Length(p.Metin) is < 1 or > 4000) ||
            passages.Sum(p => PyText.Length(p.Metin)) > 24000)
            throw new HttpRequestException("AI engine rejected the request", null, HttpStatusCode.UnprocessableEntity);

        try
        {
            var answerer = new ArchiveAnswerer(llms.Create(LlmTask.Answer));
            return await answerer.AnswerAsync(PyText.Strip(question), passages, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError("Answer failed: {Error}", ex.GetType().Name);
            throw new HttpRequestException("AI engine answer failed", null, HttpStatusCode.ServiceUnavailable);
        }
    }

    public async Task<Dictionary<string, AiSpeakerSuggestion>> SuggestSpeakerNamesAsync(string transcript, CancellationToken ct = default)
    {
        try
        {
            return await new SpeakerSuggester(llms.Create(LlmTask.Speakers)).SuggestAsync(transcript, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogError("Speaker name suggestions failed; continuing without proposals.");
            return [];
        }
    }
}
