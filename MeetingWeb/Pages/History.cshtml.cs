using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using Hangfire;

namespace MeetingWeb.Pages
{
    // SECURITY GATEWAY: Restrict history archive access to authenticated users.
    [Authorize]
    public class HistoryModel : PageModel
    {
        private static readonly TimeZoneInfo TurkeyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAudioStorage _audioStorage;
        private readonly IActiveWorkspaceService _activeWorkspace;
        private readonly ICalendarSyncService _calendar;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly ILogger<HistoryModel> _logger;

        public HistoryModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IAudioStorage audioStorage,
            IActiveWorkspaceService activeWorkspace, ICalendarSyncService calendar,
            IBackgroundJobClient backgroundJobClient, ILogger<HistoryModel> logger)
        {
            _calendar = calendar;
            _backgroundJobClient = backgroundJobClient;
            _logger = logger;
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
                .Include(m => m.Series)
                .Where(m => m.WorkspaceId == activeWorkspaceId)
                .AsQueryable();

            // The selected day is the Turkish calendar day; boundaries must be UTC for PostgreSQL.
            if (StartDate.HasValue)
            {
                var startOfSelectedDay = TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(StartDate.Value.Date, DateTimeKind.Unspecified), TurkeyTimeZone);
                sqlQuery = sqlQuery.Where(m => m.CreatedAtUtc >= startOfSelectedDay);
            }

            if (EndDate.HasValue)
            {
                var endOfSelectedDay = TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(EndDate.Value.Date.AddDays(1), DateTimeKind.Unspecified), TurkeyTimeZone);
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
                var invitedTaskIds = await _calendar.InvitedTaskIdsForMeetingAsync(meeting.Id);
                _context.Meetings.Remove(meeting);
                await _context.SaveChangesAsync();
                foreach (var taskId in invitedTaskIds) _backgroundJobClient.QueueCalendarSync(taskId, _logger);
                _audioStorage.Delete(meeting.AudioFileName);

                TempData["StatusMessage"] = "Toplantı özeti kalıcı olarak silindi.";
            }

            return RedirectToPage();
        }
    }
}
