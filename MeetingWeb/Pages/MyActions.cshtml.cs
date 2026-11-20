using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using Hangfire;
using System.Text;

namespace MeetingWeb.Pages
{
    // "Bana atananlar": every task assigned to the signed-in user, across all the teams they belong to,
    // with the team, the meeting it came from and the due date. A team the user has left drops out of the list.
    [Authorize]
    public class MyActionsModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICalendarSyncService _calendar;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly ILogger<MyActionsModel> _logger;

        public MyActionsModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager,
            ICalendarSyncService calendar, IBackgroundJobClient backgroundJobClient, ILogger<MyActionsModel> logger)
        {
            _context = context;
            _userManager = userManager;
            _calendar = calendar;
            _backgroundJobClient = backgroundJobClient;
            _logger = logger;
        }

        public record AssignedTask(int Id, string Text, string Team, int MeetingId, string Meeting, DateTime MeetingDate,
            DateOnly? DueDate, bool IsDone, DateTime? CompletedAtUtc);

        public List<AssignedTask> Open { get; private set; } = new();
        public List<AssignedTask> Done { get; private set; } = new();

        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User);

            var tasks = await MyTasks(userId)
                .Select(a => new
                {
                    a.Id,
                    a.Text,
                    a.DueDate,
                    a.CompletedAtUtc,
                    Team = a.Meeting!.Workspace!.Name,
                    a.MeetingId,
                    Meeting = a.Meeting.Title,
                    MeetingDate = a.Meeting.CreatedAtUtc
                })
                .ToListAsync();

            var list = tasks
                .Select(t => new AssignedTask(t.Id, t.Text, t.Team, t.MeetingId, t.Meeting, t.MeetingDate,
                    t.DueDate, t.CompletedAtUtc != null, t.CompletedAtUtc))
                .ToList();

            Open = list.Where(t => !t.IsDone)
                .OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenByDescending(t => t.MeetingDate)
                .ToList();
            Done = list.Where(t => t.IsDone).OrderByDescending(t => t.CompletedAtUtc).ToList();
        }

        public async Task<IActionResult> OnPostCompleteAsync(int taskId) => await SetDoneAsync(taskId, true);

        public async Task<IActionResult> OnPostReopenAsync(int taskId) => await SetDoneAsync(taskId, false);

        public async Task<IActionResult> OnPostSendToCalendarAsync(int taskId)
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null || !await _calendar.IsOwnDatedTaskAsync(taskId, userId)) return CalendarTaskUnavailable();
            try
            {
                _backgroundJobClient.Enqueue<ICalendarSyncService>(s => s.ResendAsync(taskId));
                TempData["StatusMessage"] = "Takvim daveti e-postanıza gönderiliyor. Birkaç dakika içinde gelen kutunuzda olur.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Takvim daveti işi kuyruğa alınamadı. Görev={ActionItemId}, HataTürü={ErrorType}", taskId, ex.GetType().Name);
                TempData["ErrorMessage"] = "Takvim daveti şu an gönderilemedi. Lütfen biraz sonra tekrar deneyin.";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostCalendarFileAsync(int taskId)
        {
            var userId = _userManager.GetUserId(User);
            var ics = userId == null ? null : await _calendar.BuildDownloadAsync(taskId, userId);
            if (ics == null) return CalendarTaskUnavailable();
            return File(Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8", $"briefyn-gorev-{taskId}.ics");
        }

        private IActionResult CalendarTaskUnavailable()
        {
            TempData["ErrorMessage"] = "Bu görev size atanmış, açık ve son tarihli görünmüyor.";
            return RedirectToPage();
        }

        private void QueueCalendarSync(int taskId)
        {
            try { _backgroundJobClient.Enqueue<ICalendarSyncService>(s => s.SyncAsync(taskId)); }
            catch (Exception ex) { _logger.LogWarning("Takvim eşitleme işi kuyruğa alınamadı. Görev={ActionItemId}, HataTürü={ErrorType}", taskId, ex.GetType().Name); }
        }

        private async Task<IActionResult> SetDoneAsync(int taskId, bool done)
        {
            var userId = _userManager.GetUserId(User);
            var task = await MyTasks(userId).FirstOrDefaultAsync(a => a.Id == taskId);
            if (task == null)
            {
                TempData["ErrorMessage"] = "Bu görev size atanmış görünmüyor.";
                return RedirectToPage();
            }

            task.SetDone(done, userId);
            try
            {
                await _context.SaveChangesAsync();
                QueueCalendarSync(taskId);
            }
            catch (DbUpdateConcurrencyException)
            {
                TempData["ErrorMessage"] = "Bu görev bu arada başka biri tarafından değiştirildi. Güncel hâli aşağıda; isterseniz tekrar deneyin.";
                return RedirectToPage();
            }

            TempData["StatusMessage"] = done
                ? "Görev tamamlandı olarak işaretlendi. Toplantı özetinde de adınızla görünüyor."
                : "Görev yeniden açıldı.";
            return RedirectToPage();
        }

        // Tasks assigned to the user in meetings of teams they are still a member of.
        private IQueryable<ActionItem> MyTasks(string? userId) =>
            _context.ActionItems.Where(a =>
                userId != null &&
                a.AssigneeUserId == userId &&
                _context.WorkspaceMembers.Any(wu => wu.WorkspaceId == a.Meeting!.WorkspaceId && wu.UserId == userId));
    }
}
