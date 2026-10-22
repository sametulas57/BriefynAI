using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Services;

public record SeriesAgendaTask(int Id, string Text, string? AssigneeName, DateOnly? DueDate, int? OverdueDays,
    int MeetingId, string MeetingTitle, DateTime MeetingCreatedAtUtc);
public record SeriesAgendaPoint(string Text);
public record SeriesAgenda(List<SeriesAgendaTask> OpenTasks, List<SeriesAgendaPoint> Pending, List<SeriesAgendaPoint> Decisions,
    int? LastMeetingId, DateTime? LastMeetingCreatedAtUtc)
{
    public bool HasCompletedMeeting => LastMeetingId != null;
    public bool IsEmpty => OpenTasks.Count == 0 && Pending.Count == 0 && Decisions.Count == 0;
}

// The caller must first find the series with IMeetingSeriesService.FindAsync; this service does not check access.
// It reads saved rows on every call and never stores an agenda or invokes AI.
public interface ISeriesAgendaService
{
    Task<SeriesAgenda> BuildAsync(int seriesId, DateOnly today);
}

public class SeriesAgendaService : ISeriesAgendaService
{
    private readonly ApplicationDbContext _db;
    public SeriesAgendaService(ApplicationDbContext db) => _db = db;

    public async Task<SeriesAgenda> BuildAsync(int seriesId, DateOnly today)
    {
        var meetings = await _db.Meetings.AsNoTracking().Where(m => m.SeriesId == seriesId)
            .Include(m => m.ActionItems).ThenInclude(a => a.Assignee)
            .Include(m => m.Points).AsSplitQuery().ToListAsync();
        var tasks = meetings.SelectMany(m => m.ActionItems.Where(a => !a.IsDone).Select(a => new { Meeting = m, Task = a }))
            .OrderBy(x => x.Task.DueDate == null ? 2 : x.Task.DueDate < today ? 0 : 1)
            .ThenBy(x => x.Task.DueDate)
            .ThenByDescending(x => x.Task.DueDate == null ? x.Meeting.CreatedAtUtc : DateTime.MinValue)
            .ThenBy(x => x.Task.SortOrder).ThenBy(x => x.Task.Id)
            .Select(x => new SeriesAgendaTask(x.Task.Id, x.Task.Text,
                x.Task.Assignee == null ? null : string.IsNullOrWhiteSpace(x.Task.Assignee.FullName) ? UserDisplay.UnknownName : x.Task.Assignee.FullName,
                x.Task.DueDate, x.Task.DueDate < today ? today.DayNumber - x.Task.DueDate.Value.DayNumber : null,
                x.Meeting.Id, x.Meeting.Title, x.Meeting.CreatedAtUtc)).ToList();
        var latest = meetings.Where(m => m.Status == MeetingStatus.Completed)
            .OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id).FirstOrDefault();
        return new SeriesAgenda(tasks,
            latest?.PointsOf(MeetingPointKind.Pending).Select(p => new SeriesAgendaPoint(p.Text)).ToList() ?? new(),
            latest?.PointsOf(MeetingPointKind.Decision).Select(p => new SeriesAgendaPoint(p.Text)).ToList() ?? new(),
            latest?.Id, latest?.CreatedAtUtc);
    }
}
