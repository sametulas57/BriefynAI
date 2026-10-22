using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Services
{
    public class OverdueReminderService : IOverdueReminderService
    {
        private readonly ApplicationDbContext _db;
        private readonly IEmailService _email;
        private readonly TimeProvider _time;
        private readonly ReminderOptions _options;
        private readonly ILogger<OverdueReminderService> _logger;

        public OverdueReminderService(ApplicationDbContext db, IEmailService email, TimeProvider time, IOptions<ReminderOptions> options, ILogger<OverdueReminderService> logger)
        { _db = db; _email = email; _time = time; _options = options.Value; _logger = logger; }

        public async Task RunAsync()
        {
            var today = DisplayTime.TodayIn(_time.GetUtcNow().UtcDateTime);
            var assigneeIds = await OverdueTasks(today).Select(a => a.AssigneeUserId!).Distinct().ToListAsync();
            var sent = 0; var failed = 0;
            foreach (var userId in assigneeIds)
            {
                var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.DeletionRequestedAtUtc == null && u.OverdueRemindersEnabled && u.Email != null && u.Email != "");
                if (user == null) continue;
                // Queried per person right before sending, so a membership removed while earlier mails were going out
                // already counts: tasks of a team the person has left never reach them (ADR-0005).
                var tasks = (await OverdueTasks(today).Where(a => a.AssigneeUserId == userId)
                        .Select(a => new { a.Text, a.DueDate, MeetingTitle = a.Meeting!.Title, a.Meeting.CreatedAtUtc })
                        .ToListAsync())
                    .Select(a => new OverdueTask(a.Text, a.MeetingTitle, a.CreatedAtUtc, today.DayNumber - a.DueDate!.Value.DayNumber))
                    .OrderByDescending(a => a.DaysOverdue).ToList();
                if (!tasks.Any(a => _options.Days.Contains(a.DaysOverdue))) continue;
                try { await _email.SendOverdueReminderAsync(user.Email!, user.FullName, tasks); sent++; }
                catch (Exception ex) { failed++; _logger.LogError("Geciken görev hatırlatması gönderilemedi. HataTürü={ErrorType}", ex.GetType().Name); }
            }
            _logger.LogInformation("Geciken görev hatırlatmaları tamamlandı. Gönderilen={Sent}, Hata={Failed}", sent, failed);
        }

        // Open, assigned, dated tasks past their due date whose assignee is still a member of the meeting's team.
        private IQueryable<ActionItem> OverdueTasks(DateOnly today) => _db.ActionItems.AsNoTracking()
            .Where(a => a.CompletedAtUtc == null && a.AssigneeUserId != null && a.DueDate != null && a.DueDate < today
                && _db.WorkspaceMembers.Any(m => m.UserId == a.AssigneeUserId && m.WorkspaceId == a.Meeting!.WorkspaceId));
    }
}
