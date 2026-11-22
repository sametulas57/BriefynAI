namespace MeetingWeb.Services.Ai;

public interface ITranscriptionClient
{
    Task<string> TranscribeAsync(string audioPath, string model, string language, string prompt, CancellationToken ct);
    Task<IReadOnlyList<TranscriptSegment>> TranscribeSegmentsAsync(string audioPath, string model, string language, string prompt, CancellationToken ct);
}

public sealed class GroqTranscriber(ITranscriptionClient client, IAudioTools audio, string model, double chunkSeconds = 60.0, string language = "tr")
{
    public const string TechPrompt = "backend, frontend, API, endpoint, database, SQL, server, client, microservice, Docker, Kubernetes, GitHub, pull request, merge, deployment, CI/CD, bug, ticket";

    public async Task<string> TranscribeAsync(string audioPath, string? speakersVtt, CancellationToken ct)
    {
        var cues = !string.IsNullOrEmpty(speakersVtt) ? TranscriptText.ParseVtt(speakersVtt) : [];
        var named = cues.Any(cue => !string.IsNullOrEmpty(cue.Speaker));
        var work = Path.Combine(Path.GetTempPath(), "briefyn_stt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        List<string> parts = [];
        List<TranscriptSegment> segments = [];
        try
        {
            var mono = await audio.ToMonoFlacAsync(audioPath, Path.Combine(work, "mono.flac"), ct);
            var cuts = FfmpegAudioTools.PlanCuts(await audio.DurationSecondsAsync(mono, ct), await audio.SilenceMidpointsAsync(mono, ct), chunkSeconds);
            for (var i = 0; i + 1 < cuts.Count; i++)
            {
                var piece = await audio.CutAsync(mono, cuts[i], cuts[i + 1], Path.Combine(work, $"part{i}.flac"), ct);
                if (named)
                {
                    foreach (var segment in await client.TranscribeSegmentsAsync(piece, model, language, TechPrompt, ct))
                    {
                        var cleaned = TranscriptText.CleanSegmentText(segment.Text, TechPrompt);
                        if (cleaned.Length > 0) segments.Add(new TranscriptSegment(cuts[i] + segment.Start, cuts[i] + segment.End, cleaned));
                    }
                }
                else
                {
                    parts.Add(TranscriptText.CleanPlainChunk(await client.TranscribeAsync(piece, model, language, TechPrompt, ct), TechPrompt));
                }
            }
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return named
            ? TranscriptText.FormatLines(TranscriptText.AssignSpeakers(segments, cues))
            : string.Join(" ", parts.Where(part => part.Length > 0));
    }
}
