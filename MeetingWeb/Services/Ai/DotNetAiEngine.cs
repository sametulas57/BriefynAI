using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeetingWeb.Models;

namespace MeetingWeb.Services.Ai;

// AiEngine:Mode=dotnet selects the in-process pipeline (ADR-0018); python remains the default.
public sealed class DotNetAiEngine(ILlmClientFactory llms, ITranscriberFactory transcribers, ILogger<DotNetAiEngine> logger) : IAiEngine
{
    private static readonly Regex PartId = new(@"^T\d{1,9}-[KRBAD]\d{1,4}\z");
    private static readonly Regex Date = new(@"^\d{4}-\d{2}-\d{2}\z");

    public async Task<AiApiResponse?> ProcessMeetingAsync(string audioPath, string? speakersPath, CancellationToken ct = default)
    {
        var speakersTooLarge = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            logger.LogInformation("Received audio for processing.");
            logger.LogInformation("Initializing Speech-to-Text engine...");
            string? speakersVtt = null;
            if (speakersPath != null)
            {
                const int speakerLimit = 2 * 1024 * 1024;
                var data = new byte[speakerLimit + 1];
                await using var file = File.OpenRead(speakersPath);
                var count = await file.ReadAtLeastAsync(data, data.Length, throwOnEndOfStream: false, cancellationToken: ct);
                if (count > speakerLimit)
                {
                    speakersTooLarge = true;
                    throw new InvalidDataException("Speaker file is too large.");
                }
                try { speakersVtt = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data, 0, count); }
                catch (DecoderFallbackException) { /* Ignore invalid UTF-8, like Python; ParseVtt handles the BOM. */ }
            }

            var hasVtt = !string.IsNullOrEmpty(speakersVtt);
            var transcriber = transcribers.Create(hasVtt ? "groq" : null);
            var text = await transcriber.TranscribeAsync(audioPath, hasVtt ? speakersVtt : null, ct);
            HashSet<string> speakerNames = hasVtt
                ? TranscriptText.ParseVtt(speakersVtt!).Where(cue => !string.IsNullOrEmpty(cue.Speaker))
                    .Select(cue => cue.Speaker!).ToHashSet(StringComparer.Ordinal)
                : [];
            if (string.IsNullOrEmpty(text) || TranscriptText.IsOnlyHallucinatedCaptions(text, speakerNames))
                throw new NoSpeechException();

            logger.LogInformation("Transcription completed successfully.");
            Dictionary<string, AiSpeakerSuggestion> suggestions = hasVtt ? [] : await SuggestSpeakerNamesAsync(text, ct);
            logger.LogInformation("Executing LLM summarization pipeline...");
            AiSummaryData? summary;
            try
            {
                summary = await new MeetingSummarizer(llms.Create(LlmTask.Summary), logger).SummarizeAsync(text, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogError("Summarization failed; returning the transcript without a summary: {Error}", ex.GetType().Name);
                summary = null;
            }
            return new AiApiResponse { Success = summary != null, Transkript = text, Summary = summary, KonusmaciOnerileri = suggestions };
        }
        catch (NoSpeechException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (InvalidDataException) when (speakersTooLarge) { throw; }
        catch (Exception ex)
        {
            // Transcription/audio errors (GroqException, AzureSpeechException, AudioToolException) carry
            // status/exit codes or fixed diagnostics, never a transcript or provider response body.
            logger.LogError(ex, "Meeting processing failed.");
            // No inner exception: Hangfire persists this error to its database.
            throw new InvalidOperationException("AI engine processing failed.");
        }
    }

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
