using Hangfire;
using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Services
{
    // Aksiyon takibi: one e-mail to the assignee the day before the due date (Turkish calendar).
    // Runs hourly, so a task dated "tomorrow" late in the day still gets its reminder. Each (assignee, due date)
    // pair is claimed atomically before the mail is queued, so overlapping runs never send twice; the mail itself is
    // a separate job that Hangfire retries if the mail server is briefly unavailable.
    public interface IActionReminderService
    {
        Task RunAsync();
        Task<int> SendDueRemindersAsync(DateTimeOffset nowUtc);
    }

    public class ActionReminderService : IActionReminderService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBackgroundJobClient _jobs;
        private readonly ILogger<ActionReminderService> _logger;

        public ActionReminderService(ApplicationDbContext context, IBackgroundJobClient jobs, ILogger<ActionReminderService> logger)
        {
            _context = context;
            _jobs = jobs;
            _logger = logger;
        }

        public Task RunAsync() => SendDueRemindersAsync(DateTimeOffset.UtcNow);

        public async Task<int> SendDueRemindersAsync(DateTimeOffset nowUtc)
        {
            var tomorrow = DisplayTime.Today(nowUtc).AddDays(1);

            var due = await _context.Set<ActionItem>().AsNoTracking()
                .Where(a => !a.IsDone && a.DueDate == tomorrow && a.AssigneeUserId != null &&
                            _context.WorkspaceUsers.Any(w => w.UserId == a.AssigneeUserId && w.WorkspaceId == a.MeetingSummary!.WorkspaceId))
                .Select(a => new { a.Id, a.AssigneeUserId, a.DueDate, a.ReminderSentFor })
                .ToListAsync();

            var queued = 0;
            foreach (var task in due)
            {
                var key = $"{task.AssigneeUserId}|{task.DueDate:yyyy-MM-dd}";
                if (task.ReminderSentFor == key) continue;

                // Atomic claim: only the run that flips the key queues the mail.
                var claimed = await _context.Set<ActionItem>()
                    .Where(a => a.Id == task.Id && (a.ReminderSentFor == null || a.ReminderSentFor != key))
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReminderSentFor, key));
                if (claimed == 0) continue;

                _jobs.Enqueue<IEmailService>(e => e.SendActionReminderAsync(task.Id, key));
                queued++;
            }

            if (queued > 0) _logger.LogInformation("Queued {Count} action reminder(s) for {Date}.", queued, tomorrow);
            return queued;
        }
    }
}
