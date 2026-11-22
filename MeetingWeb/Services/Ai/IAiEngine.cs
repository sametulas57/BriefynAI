using System.Text.Json;
using MeetingWeb.Models;

namespace MeetingWeb.Services.Ai;

public interface IAiEngine
{
    // Throws NoSpeechException when the engine reports no speech (HTTP 422 detail "no_speech").
    Task<AiApiResponse?> ProcessMeetingAsync(string audioPath, string? speakersPath, CancellationToken ct = default);

    // Returns null when the engine answered but success is false.
    Task<AiSummaryData?> SummarizeAsync(string transcript, CancellationToken ct = default);

    // Returns the engine's JSON answer object (cloned root). Non-success HTTP -> HttpRequestException with StatusCode set.
    Task<JsonElement> AnswerAsync(string question, IReadOnlyList<AiAnswerPassage> passages, CancellationToken ct = default);
}

public sealed record AiAnswerPassage(string Id, string Toplanti, string Tarih, string Tur, string Metin);

public sealed class NoSpeechException : Exception;
