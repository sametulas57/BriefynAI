using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Pages
{
    // "Bana atananlar": the signed-in user's open tasks from every team they currently belong to.
    [Authorize]
    public class AssignedToMeModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public AssignedToMeModel(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public record Row(int Id, string Text, int MeetingId, string MeetingTopic, string TeamName, DateOnly? DueDate);

        public List<Row> Overdue { get; } = new();
        public List<Row> Today { get; } = new();
        public List<Row> Upcoming { get; } = new();
        public List<Row> NoDate { get; } = new();
        public int Total => Overdue.Count + Today.Count + Upcoming.Count + NoDate.Count;

        // Shared with the navigation badge: open tasks assigned to the user in teams they are still a member of.
        public static IQueryable<ActionItem> OpenTasksFor(ApplicationDbContext context, string userId) =>
            context.Set<ActionItem>().Where(a =>
                !a.IsDone && a.AssigneeUserId == userId &&
                context.WorkspaceUsers.Any(w => w.UserId == userId && w.WorkspaceId == a.MeetingSummary!.WorkspaceId));

        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null) return;

            var tasks = await OpenTasksFor(_context, userId)
                .Include(a => a.MeetingSummary).ThenInclude(m => m!.Workspace)
                .AsNoTracking()
                .ToListAsync();

            var today = DisplayTime.Today();
            foreach (var task in tasks.OrderBy(t => t.DueDate ?? DateOnly.MaxValue).ThenBy(t => t.Id))
            {
                var row = new Row(task.Id, task.Text.Split("|~|")[0], task.MeetingSummaryId,
                    task.MeetingSummary!.ToplantiKonusu, task.MeetingSummary.Workspace?.Name ?? "", task.DueDate);
                var bucket = task.DueDate == null ? NoDate : task.DueDate < today ? Overdue : task.DueDate == today ? Today : Upcoming;
                bucket.Add(row);
            }
        }

        public async Task<IActionResult> OnPostCompleteAsync(int taskId)
        {
            var userId = _userManager.GetUserId(User);
            var task = userId == null ? null : await OpenTasksFor(_context, userId).FirstOrDefaultAsync(a => a.Id == taskId);
            if (task == null)
            {
                TempData["ErrorMessage"] = "Bu görev artık listenizde değil.";
                return RedirectToPage();
            }

            task.IsDone = true;
            task.CompletedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = "Görev tamamlandı olarak işaretlendi.";
            return RedirectToPage();
        }
    }
}
