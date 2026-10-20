using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;

namespace MeetingWeb.Pages
{
    // SECURITY GATEWAY: Restrict history archive access to authenticated users.
    [Authorize]
    public class HistoryModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAudioStorage _audioStorage;
        private readonly IActiveWorkspaceService _activeWorkspace;

        public HistoryModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IAudioStorage audioStorage,
            IActiveWorkspaceService activeWorkspace)
        {
            _activeWorkspace = activeWorkspace;
            _context = context;
            _userManager = userManager;
            _audioStorage = audioStorage;
        }

        public IList<Meeting> Meetings { get; set; } = default!;

        // Role-Based Access Control (RBAC) flag for UI rendering (e.g., Delete button).
        public bool IsOwner { get; set; } = false;

        [BindProperty(SupportsGet = true)]
        public DateTime? StartDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? EndDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SearchString { get; set; }

        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null) return;

            // Session Authorization (S7): verified membership, with fallback to the user's own default workspace.
            var workspaceUser = await _activeWorkspace.GetActiveMembershipAsync(userId);
            int? activeWorkspaceId = workspaceUser?.WorkspaceId;

            // Flag evaluation for privileged UI actions.
            if (workspaceUser != null && workspaceUser.Role == WorkspaceRole.Owner)
            {
                IsOwner = true;
            }

            // --- PHASE 1: DATABASE (SQL) FILTERING ---
            // Perform queries ONLY on unencrypted indexing columns (Tenant ID, Dates).
            // Topic lines are loaded for the card preview and the search; the transcript stays in its own table.
            var sqlQuery = _context.Meetings
                .Include(m => m.Points.Where(p => p.Kind == MeetingPointKind.Topic))
                .Where(m => m.WorkspaceId == activeWorkspaceId)
                .AsQueryable();

            if (StartDate.HasValue)
            {
                sqlQuery = sqlQuery.Where(m => m.CreatedAtUtc >= StartDate.Value.Date);
            }

            if (EndDate.HasValue)
            {
                var endOfSelectedDay = EndDate.Value.Date.AddDays(1);
                sqlQuery = sqlQuery.Where(m => m.CreatedAtUtc < endOfSelectedDay);
            }

            // Execute SQL query and decrypt data sequentially into RAM.
            var decryptedMeetings = await sqlQuery
                .OrderByDescending(m => m.CreatedAtUtc)
                .ToListAsync();

            // --- PHASE 2: IN-MEMORY (RAM) FILTERING ---
            // Execute string manipulation/search ONLY after data is decrypted in memory.
            if (!string.IsNullOrWhiteSpace(SearchString))
            {
                Meetings = decryptedMeetings
                    .Where(m =>
                        (!string.IsNullOrEmpty(m.Title) && m.Title.Contains(SearchString, StringComparison.OrdinalIgnoreCase)) ||
                        m.Points.Any(p => p.Text.Contains(SearchString, StringComparison.OrdinalIgnoreCase))
                    )
                    .ToList();
            }
            else
            {
                Meetings = decryptedMeetings;
            }
        }

        // --- TENANT DATA ANNIHILATION ---
        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var userId = _userManager.GetUserId(User);
            // The session's workspace is used as-is (not ActiveWorkspaceService's fallback): a destructive action must hit
            // the team the user was looking at, or nothing. Membership and role are verified against the database below.
            int? activeWorkspaceId = HttpContext.Session.GetInt32(ActiveWorkspaceService.SessionKey);

            // SECURITY FIREWALL: Re-verify ownership status to prevent explicit POST attacks.
            var isOwner = await _context.WorkspaceMembers
                .AnyAsync(wu => wu.UserId == userId && wu.WorkspaceId == activeWorkspaceId && wu.Role == WorkspaceRole.Owner);

            if (!isOwner)
            {
                TempData["ErrorMessage"] = "Geçmiş toplantıları yalnız ekip kurucusu silebilir.";
                return RedirectToPage();
            }

            // Ensure the meeting strictly belongs to the current tenant scope.
            var meeting = await _context.Meetings
                .FirstOrDefaultAsync(m => m.Id == id && m.WorkspaceId == activeWorkspaceId);

            if (meeting != null)
            {
                _context.Meetings.Remove(meeting);
                await _context.SaveChangesAsync();
                _audioStorage.Delete(meeting.AudioFileName);

                TempData["StatusMessage"] = "Toplantı özeti kalıcı olarak silindi.";
            }

            return RedirectToPage();
        }
    }
}