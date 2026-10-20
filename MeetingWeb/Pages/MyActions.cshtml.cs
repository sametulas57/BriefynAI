using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MeetingWeb.Data;
using MeetingWeb.Models;

namespace MeetingWeb.Pages
{
    // "Bana atananlar": every task assigned to the signed-in user, across all the teams they belong to,
    // with the team, the meeting it came from and the due date. A team the user has left drops out of the list.
    [Authorize]
    public class MyActionsModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public MyActionsModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
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
                    a.Id, a.Text, a.DueDate, a.CompletedAtUtc,
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
