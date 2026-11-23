using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hangfire;
using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Services;

public enum SpeakerNamingResult { Renamed, RenamedAndResummarizing, NotFound, NothingToRename, InvalidName, Busy }

public interface ISpeakerNamingService
{
    Task<SpeakerNamingResult> RenameAsync(string? userId, int meetingId, IReadOnlyList<string> labels, IReadOnlyList<string> names);
}

public class SpeakerNamingService : ISpeakerNamingService
{
    public sealed record SpeakerRow(string Label, string FirstLine, string? SavedName, string? SuggestedName, string? Reason);

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

    public static IReadOnlyList<SpeakerRow> Rows(MeetingTranscript? transcript)
    {
        var source = transcript?.OriginalText ?? transcript?.Text ?? "";
        var saved = ReadSavedNames(transcript?.SpeakerNames);
        var suggestionsJson = transcript?.SpeakerSuggestions;
        Dictionary<string, AiSpeakerSuggestion>? suggestions = null;
        if (!string.IsNullOrWhiteSpace(suggestionsJson))
        {
            try { suggestions = JsonSerializer.Deserialize<Dictionary<string, AiSpeakerSuggestion>>(suggestionsJson); }
            catch (JsonException) { /* Suggestions are optional; a malformed value must not prevent naming speakers. */ }
        }

        var rows = new List<SpeakerRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in LabelPattern.Matches(source))
        {
            if (!seen.Add(match.Value)) continue;
            var start = match.Index + match.Length + 2;
            var end = source.IndexOfAny(['\r', '\n'], start);
            var firstLine = source[start..(end < 0 ? source.Length : end)];
            var sentenceEnd = firstLine.IndexOfAny(['.', '?', '!', '…']);
            if (sentenceEnd >= 0 && sentenceEnd < 140) firstLine = firstLine[..(sentenceEnd + 1)];
            else if (firstLine.Length > 140) firstLine = firstLine[..140] + "…";

            string? name = null;
            string? reason = null;
            saved.TryGetValue(match.Value, out var savedName);
            if (savedName == null && suggestions != null && suggestions.TryGetValue(match.Value, out var suggestion)
                && TryNormalizeName(suggestion?.Ad, out var normalized))
            {
                name = normalized;
                reason = suggestion!.Gerekce?.Trim();
                reason = string.IsNullOrEmpty(reason) ? null : reason[..Math.Min(reason.Length, 200)];
            }
            rows.Add(new SpeakerRow(match.Value, firstLine, savedName, name, reason));
        }
        return rows;
    }

    private static bool TryNormalizeName(string? input, out string name)
    {
        name = input?.Trim() ?? "";
        // Check before collapsing whitespace so embedded tabs/newlines cannot hide control characters.
        if (string.IsNullOrWhiteSpace(name) || name.Contains(':') || name.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format)) return false;
        name = WhitespacePattern.Replace(name, " ");
        return name.Length <= MaxNameLength;
    }

    private static Dictionary<string, string> ReadSavedNames(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)?
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
        }
        catch (JsonException) { return new(StringComparer.Ordinal); }
    }

    private static string NameKey(string name) =>
        WhitespacePattern.Replace(name.Trim(), " ").ToLower(CultureInfo.GetCultureInfo("tr-TR"));

    private static string Display(string label, IReadOnlyDictionary<string, string> names) =>
        names.TryGetValue(label, out var name) ? name : label;

    private static string Render(string source, IReadOnlyDictionary<string, string> names)
    {
        var text = LabelPattern.Replace(source, match => Display(match.Value, names));
        var appliedNames = string.Join("|", names.Values.Distinct(StringComparer.Ordinal).Select(Regex.Escape));
        return Regex.Replace(text,
            @"^(?<name>" + appliedNames + @"): [^\r\n]*(?:\r?\n\k<name>: [^\r\n]*)+",
            match => match.Value.Replace("\r\n" + match.Groups["name"].Value + ": ", " ", StringComparison.Ordinal)
                .Replace("\n" + match.Groups["name"].Value + ": ", " ", StringComparison.Ordinal),
            RegexOptions.Multiline);
    }

    public async Task<SpeakerNamingResult> RenameAsync(string? userId, int meetingId, IReadOnlyList<string> labels, IReadOnlyList<string> names)
    {
        if (!await _access.CanAccessMeetingAsync(userId, meetingId)) return SpeakerNamingResult.NotFound;
        var meeting = await _db.Meetings.Include(m => m.Transcript).Include(m => m.Points).Include(m => m.ActionItems)
            .AsSplitQuery().FirstOrDefaultAsync(m => m.Id == meetingId);
        if (meeting == null) return SpeakerNamingResult.NotFound;

        var source = meeting.Transcript?.OriginalText ?? meeting.Transcript?.Text ?? "";
        var available = Labels(source).ToHashSet(StringComparer.Ordinal);
        var saved = ReadSavedNames(meeting.Transcript?.SpeakerNames);
        var next = new Dictionary<string, string>(saved, StringComparer.Ordinal);
        for (var i = 0; i < Math.Min(labels.Count, names.Count); i++)
        {
            if (string.IsNullOrWhiteSpace(names[i]) || !available.Contains(labels[i])) continue;
            if (!TryNormalizeName(names[i], out var name)) return SpeakerNamingResult.InvalidName;
            next[labels[i]] = name;
        }
        foreach (var group in available.Where(next.ContainsKey)
            // Numeric order without parsing: a shorter digit run is a smaller number.
            .OrderBy(label => label.Length).ThenBy(label => label, StringComparer.Ordinal)
            .GroupBy(label => NameKey(next[label])))
        {
            var spelling = next[group.First()];
            foreach (var label in group) next[label] = spelling;
        }
        if (next.Count == saved.Count && next.All(pair => saved.TryGetValue(pair.Key, out var name) && name == pair.Value))
            return SpeakerNamingResult.NothingToRename;
        if (meeting.Status is not (MeetingStatus.Completed or MeetingStatus.Failed)) return SpeakerNamingResult.Busy;

        var previousStatus = meeting.Status;
        var previousFailureReason = meeting.FailureReason;
        var previousVersion = meeting.RowVersion;
        // Claim the processing slot atomically, including when two callers loaded the same previous state.
        var started = await _db.Meetings
            .Where(m => m.Id == meetingId && m.Status != MeetingStatus.Processing && m.RowVersion == previousVersion)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MeetingStatus.Processing)
                .SetProperty(m => m.FailureReason, (MeetingFailureReason?)null));
        if (started == 0) return SpeakerNamingResult.Busy;

        try
        {
            // PostgreSQL advances xmin on the claim even though ExecuteUpdate bypasses tracking.
            var claimedVersion = await _db.Meetings.Where(m => m.Id == meetingId).Select(m => m.RowVersion).SingleAsync();
            _db.Entry(meeting).Property(m => m.RowVersion).OriginalValue = meeting.RowVersion = claimedVersion;
            meeting.Transcript!.OriginalText ??= meeting.Transcript.Text;
            meeting.Transcript.Text = Render(source, next);
            meeting.Transcript.SpeakerNames = JsonSerializer.Serialize(next);
            meeting.Status = previousStatus == MeetingStatus.Completed ? MeetingStatus.Completed : MeetingStatus.Processing;
            meeting.FailureReason = null;
            // ExecuteUpdate bypasses tracking; Completed must be written even though it matches the loaded value.
            _db.Entry(meeting).Property(m => m.Status).IsModified = true;
            if (previousStatus == MeetingStatus.Completed)
            {
                var replacements = available.GroupBy(label => Display(label, saved), StringComparer.Ordinal)
                    .Where(group => group.Select(label => Display(label, next)).Distinct(StringComparer.Ordinal).Count() == 1)
                    .Where(group => group.Key != Display(group.First(), next))
                    .ToDictionary(group => group.Key, group => Display(group.First(), next), StringComparer.Ordinal);
                meeting.Title = TurkishNameSuffix.ReplaceSpeakerNames(meeting.Title, replacements);
                foreach (var point in meeting.Points.Where(p => p.Source == EntrySource.Ai))
                    point.Text = TurkishNameSuffix.ReplaceSpeakerNames(point.Text, replacements);
                foreach (var action in meeting.ActionItems.Where(a => a.Source == EntrySource.Ai))
                    action.Text = TurkishNameSuffix.ReplaceSpeakerNames(action.Text, replacements);
            }
            await _db.SaveChangesAsync();
        }
        catch
        {
            await _db.Meetings.Where(m => m.Id == meetingId && m.Status == MeetingStatus.Processing)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, previousStatus)
                    .SetProperty(m => m.FailureReason, previousFailureReason));
            throw;
        }

        if (previousStatus == MeetingStatus.Completed) return SpeakerNamingResult.Renamed;
        _jobs.Enqueue<IMeetingProcessor>(p => p.ResummarizeMeetingJob(meetingId));
        return SpeakerNamingResult.RenamedAndResummarizing;
    }
}
