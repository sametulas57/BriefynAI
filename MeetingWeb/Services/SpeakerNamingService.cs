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

    public async Task<SpeakerNamingResult> RenameAsync(string? userId, int meetingId, IReadOnlyList<string> labels, IReadOnlyList<string> names)
    {
        if (!await _access.CanAccessMeetingAsync(userId, meetingId)) return SpeakerNamingResult.NotFound;
        var meeting = await _db.Meetings.Include(m => m.Transcript).FirstOrDefaultAsync(m => m.Id == meetingId);
        if (meeting == null) return SpeakerNamingResult.NotFound;

        var available = Labels(meeting.Transcript?.Text).ToHashSet(StringComparer.Ordinal);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < Math.Min(labels.Count, names.Count); i++)
        {
            var name = names[i]?.Trim();
            if (string.IsNullOrWhiteSpace(name) || !available.Contains(labels[i])) continue;
            // Check before collapsing whitespace so embedded tabs/newlines cannot hide control characters.
            if (name.Contains(':') || name.Any(char.IsControl)) return SpeakerNamingResult.InvalidName;
            name = WhitespacePattern.Replace(name, " ");
            if (name.Length > MaxNameLength) return SpeakerNamingResult.InvalidName;
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
