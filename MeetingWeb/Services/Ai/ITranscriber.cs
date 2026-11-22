namespace MeetingWeb.Services.Ai;

public interface ITranscriber
{
    Task<string> TranscribeAsync(string audioPath, string? speakersVtt, CancellationToken ct);
}
