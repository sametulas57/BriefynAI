using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using MeetingWeb.Data;
using MeetingWeb.Models;

namespace MeetingWeb.Pages
{
    // SECURITY GATEWAY: Restrict history archive access to authenticated users.
    [Authorize]
    public class HistoryModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public HistoryModel(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public IList<MeetingSummary> MeetingSummaries { get; set; } = default!;

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

            int? activeWorkspaceId = HttpContext.Session.GetInt32("ActiveWorkspaceId");

            // Session Authorization: Verify the user actively belongs to the queried tenant.
            var workspaceUser = await _context.WorkspaceUsers
                .FirstOrDefaultAsync(wu => wu.UserId == userId && wu.WorkspaceId == activeWorkspaceId);

            // FALLBACK MECHANISM: Auto-resolve to the first available tenant if the session is empty/invalid.
            if (activeWorkspaceId == null || workspaceUser == null)
            {
                var firstWorkspace = await _context.WorkspaceUsers
                    .FirstOrDefaultAsync(wu => wu.UserId == userId);

                activeWorkspaceId = firstWorkspace?.WorkspaceId;
                workspaceUser = firstWorkspace;

                if (activeWorkspaceId != null)
                {
                    HttpContext.Session.SetInt32("ActiveWorkspaceId", activeWorkspaceId.Value);
                }
            }

            // Flag evaluation for privileged UI actions.
            if (workspaceUser != null && workspaceUser.Role == "Sahip")
            {
                IsOwner = true;
            }

            // --- PHASE 1: DATABASE (SQL) FILTERING ---
            // Perform queries ONLY on unencrypted indexing columns (Tenant ID, Dates).
            var sqlQuery = _context.MeetingSummaries
                .Where(m => m.WorkspaceId == activeWorkspaceId)
                .AsQueryable();

            if (StartDate.HasValue)
            {
                sqlQuery = sqlQuery.Where(m => m.CreatedDate >= StartDate.Value.Date);
            }

            if (EndDate.HasValue)
            {
                var endOfSelectedDay = EndDate.Value.Date.AddDays(1);
                sqlQuery = sqlQuery.Where(m => m.CreatedDate < endOfSelectedDay);
            }

            // Execute SQL query and decrypt data sequentially into RAM.
            var decryptedMeetings = await sqlQuery
                .OrderByDescending(m => m.CreatedDate)
                .ToListAsync();

            // --- PHASE 2: IN-MEMORY (RAM) FILTERING ---
            // Execute string manipulation/search ONLY after data is decrypted in memory.
            if (!string.IsNullOrWhiteSpace(SearchString))
            {
                MeetingSummaries = decryptedMeetings
                    .Where(m =>
                        (!string.IsNullOrEmpty(m.ToplantiKonusu) && m.ToplantiKonusu.Contains(SearchString, StringComparison.OrdinalIgnoreCase)) ||
                        (m.GorusulenKonular != null && m.GorusulenKonular.Any(k => k.Contains(SearchString, StringComparison.OrdinalIgnoreCase)))
                    )
                    .ToList();
            }
            else
            {
                MeetingSummaries = decryptedMeetings;
            }
        }

        // --- TENANT DATA ANNIHILATION ---
        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var userId = _userManager.GetUserId(User);
            int? activeWorkspaceId = HttpContext.Session.GetInt32("ActiveWorkspaceId");

            // SECURITY FIREWALL: Re-verify ownership status to prevent explicit POST attacks.
            var isOwner = await _context.WorkspaceUsers
                .AnyAsync(wu => wu.UserId == userId && wu.WorkspaceId == activeWorkspaceId && wu.Role == "Sahip");

            if (!isOwner)
            {
                TempData["ErrorMessage"] = "Yetkisiz İşlem: Geçmiş toplantıları sadece Kurucular silebilir.";
                return RedirectToPage();
            }

            // Ensure the meeting strictly belongs to the current tenant scope.
            var meeting = await _context.MeetingSummaries
                .FirstOrDefaultAsync(m => m.Id == id && m.WorkspaceId == activeWorkspaceId);

            if (meeting != null)
            {
                _context.MeetingSummaries.Remove(meeting);
                await _context.SaveChangesAsync();

                TempData["StatusMessage"] = "Toplantı özeti kalıcı olarak silindi.";
            }

            return RedirectToPage();
        }
    }
}