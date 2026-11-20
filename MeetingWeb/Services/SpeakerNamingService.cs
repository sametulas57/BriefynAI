using System.Text.Json;
using System.Text.RegularExpressions;
using Hangfire;
using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Services;

public enum SpeakerNamingResult { Renamed, NotFound, NothingToRename, InvalidName, Busy }

public interface ISpeakerNamingService
{
    Task<SpeakerNamingResult> RenameAsync(string? userId, int meetingId, IReadOnlyList<string> labels, IReadOnlyList<string> names);
}

public class SpeakerNamingService : ISpeakerNamingService
{
    public sealed record SpeakerRow(string Label, string FirstLine, string? SuggestedName, string? Reason);

    public const int MaxNameLength = 60;
    private static readonly Regex LabelPattern = new(@"^Konuşmacı \d+(?=: )", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);
    private readonly ApplicationDbContext _db;
    private readonly IMeetingAccessService _access;
    private readonly IBackgroundJobClient _jobs;

    public SpeakerNamingService(ApplicationDbContext db, IMeetingAccessService access, IBackgroundJobClient jobs)
    {
        _db = db;
        _access = access;
        _jobs = jobs;
    }

    public static IReadOnlyList<string> Labels(string? transcript) =>
        LabelPattern.Matches(transcript ?? "").Select(m => m.Value).Distinct(StringComparer.Ordinal).ToArray();

    public static IReadOnlyList<SpeakerRow> Rows(string? transcript, string? suggestionsJson)
    {
        Dictionary<string, AiSpeakerSuggestion>? suggestions = null;
        if (!string.IsNullOrWhiteSpace(suggestionsJson))
        {
            try { suggestions = JsonSerializer.Deserialize<Dictionary<string, AiSpeakerSuggestion>>(suggestionsJson); }
            catch (JsonException) { /* Suggestions are optional; a malformed value must not prevent naming speakers. */ }
        }

        var rows = new List<SpeakerRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in LabelPattern.Matches(transcript ?? ""))
        {
            if (!seen.Add(match.Value)) continue;
            var start = match.Index + match.Length + 2;
            var end = transcript!.IndexOfAny(['\r', '\n'], start);
            var firstLine = transcript[start..(end < 0 ? transcript.Length : end)];
            var sentenceEnd = firstLine.IndexOfAny(['.', '?', '!', '…']);
            if (sentenceEnd >= 0 && sentenceEnd < 140) firstLine = firstLine[..(sentenceEnd + 1)];
            else if (firstLine.Length > 140) firstLine = firstLine[..140] + "…";

            string? name = null;
            string? reason = null;
            if (suggestions != null && suggestions.TryGetValue(match.Value, out var suggestion)
                && TryNormalizeName(suggestion?.Ad, out var normalized))
            {
                name = normalized;
                reason = suggestion!.Gerekce?.Trim();
                reason = string.IsNullOrEmpty(reason) ? null : reason[..Math.Min(reason.Length, 200)];
            }
            rows.Add(new SpeakerRow(match.Value, firstLine, name, reason));
        }
        return rows;
    }

    private static bool TryNormalizeName(string? input, out string name)
    {
        name = input?.Trim() ?? "";
        // Check before collapsing whitespace so embedded tabs/newlines cannot hide control characters.
        if (string.IsNullOrWhiteSpace(name) || name.Contains(':') || name.Any(char.IsControl)) return false;
        name = WhitespacePattern.Replace(name, " ");
        return name.Length <= MaxNameLength;
    }

    public async Task<SpeakerNamingResult> RenameAsync(string? userId, int meetingId, IReadOnlyList<string> labels, IReadOnlyList<string> names)
    {
        if (!await _access.CanAccessMeetingAsync(userId, meetingId)) return SpeakerNamingResult.NotFound;
        var meeting = await _db.Meetings.Include(m => m.Transcript).FirstOrDefaultAsync(m => m.Id == meetingId);
        if (meeting == null) return SpeakerNamingResult.NotFound;

        var available = Labels(meeting.Transcript?.Text).ToHashSet(StringComparer.Ordinal);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < Math.Min(labels.Count, names.Count); i++)
        {
            if (string.IsNullOrWhiteSpace(names[i]) || !available.Contains(labels[i])) continue;
            if (!TryNormalizeName(names[i], out var name)) return SpeakerNamingResult.InvalidName;
            replacements[labels[i]] = name;
        }
        if (replacements.Count == 0) return SpeakerNamingResult.NothingToRename;
        if (meeting.Status is not (MeetingStatus.Completed or MeetingStatus.Failed)) return SpeakerNamingResult.Busy;

        var previousStatus = meeting.Status;
        var previousFailureReason = meeting.FailureReason;
        // Claim the processing slot atomically, including when two callers loaded the same previous state.
        var started = await _db.Meetings
            .Where(m => m.Id == meetingId && m.Status != MeetingStatus.Processing)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MeetingStatus.Processing)
                .SetProperty(m => m.FailureReason, (MeetingFailureReason?)null));
        if (started == 0) return SpeakerNamingResult.Busy;

        try
        {
            meeting.Transcript!.Text = LabelPattern.Replace(meeting.Transcript.Text,
                match => replacements.TryGetValue(match.Value, out var name) ? name : match.Value);
            var appliedNames = string.Join("|", replacements.Values.Distinct(StringComparer.Ordinal).Select(Regex.Escape));
            meeting.Transcript.Text = Regex.Replace(meeting.Transcript.Text,
                @"^(?<name>" + appliedNames + @"): [^\r\n]*(?:\r?\n\k<name>: [^\r\n]*)+",
                match => match.Value.Replace("\r\n" + match.Groups["name"].Value + ": ", " ", StringComparison.Ordinal)
                    .Replace("\n" + match.Groups["name"].Value + ": ", " ", StringComparison.Ordinal),
                RegexOptions.Multiline);
            await _db.SaveChangesAsync();
        }
        catch
        {
            await _db.Meetings.Where(m => m.Id == meetingId && m.Status == MeetingStatus.Processing)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, previousStatus)
                    .SetProperty(m => m.FailureReason, previousFailureReason));
            throw;
        }

        _jobs.Enqueue<IMeetingProcessor>(p => p.ResummarizeMeetingJob(meetingId));
        return SpeakerNamingResult.Renamed;
    }
}
