using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Pages.Workspace
{
    // SECURITY GATEWAY: Restrict access to authenticated users only.
    [Authorize]
    public class MembersModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        private readonly IActiveWorkspaceService _activeWorkspace;
        private readonly IAudioStorage _audioStorage;

        public MembersModel(ApplicationDbContext context, UserManager<IdentityUser> userManager, IActiveWorkspaceService activeWorkspace, IAudioStorage audioStorage)
        {
            _activeWorkspace = activeWorkspace;
            _audioStorage = audioStorage;
            _context = context;
            _userManager = userManager;
        }

        public string WorkspaceName { get; set; } = "";
        public string CurrentUserRole { get; set; } = "";
        public string CurrentUserId { get; set; } = "";

        // Primary collection to hold the roster of workspace members.
        public List<WorkspaceUser> TeamMembers { get; set; } = new List<WorkspaceUser>();

        // Lookup dictionaries for O(1) retrieval of user metadata (Names and Avatars).
        public Dictionary<string, string> UserNames { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> UserAvatars { get; set; } = new Dictionary<string, string>();

        // HTTP GET: Initializes the dashboard and resolves tenant context.
        public async Task<IActionResult> OnGetAsync()
        {
            CurrentUserId = _userManager.GetUserId(User) ?? "";

            // 1. Context Resolution (S7): verified membership, falling back to an owned workspace when the session is stale.
            int? activeWorkspaceId = (await _activeWorkspace.GetActiveMembershipAsync(CurrentUserId))?.WorkspaceId;

            // 2. Redirect to onboarding if the user is completely orphaned.
            if (activeWorkspaceId == null) return RedirectToPage("/Workspace/Create");

            // Retrieve current workspace metadata.
            var workspace = await _context.Workspaces.FindAsync(activeWorkspaceId);
            WorkspaceName = workspace?.Name ?? "Ekip";

            TeamMembers = await _context.WorkspaceUsers
                .Include(wu => wu.User)
                .Where(wu => wu.WorkspaceId == activeWorkspaceId)
                .ToListAsync();

            // Establish the Role-Based Access Control (RBAC) context for the viewing user.
            var currentUserRecord = TeamMembers.FirstOrDefault(wu => wu.UserId == CurrentUserId);
            if (currentUserRecord == null) return RedirectToPage("/Index");

            CurrentUserRole = currentUserRecord.Role ?? "";

            // Eagerly fetch Identity Claims (Full Name, Avatar) for the entire roster.
            var userIds = TeamMembers.Select(wu => wu.UserId).ToList();
            var claims = await _context.UserClaims
                .Where(c => userIds.Contains(c.UserId) && (c.ClaimType == UserClaimTypes.FullName || c.ClaimType == UserClaimTypes.AvatarUrl || c.ClaimType == UserClaimTypes.LegacyProfilePicture))
                .ToListAsync();

            // Populate lookup dictionaries for efficient UI rendering.
            foreach (var claim in claims)
            {
                if (claim.ClaimType == UserClaimTypes.FullName)
                {
                    UserNames[claim.UserId] = claim.ClaimValue ?? "İsimsiz Üye";
                }
                else if (claim.ClaimType == UserClaimTypes.AvatarUrl || claim.ClaimType == UserClaimTypes.LegacyProfilePicture)
                {
                    UserAvatars[claim.UserId] = claim.ClaimValue ?? "";
                }
            }

            return Page();
        }

        // --- MEMBER EVICTION LOGIC ---
        public async Task<IActionResult> OnPostRemoveMemberAsync(string targetUserId)
        {
            int? activeWorkspaceId = HttpContext.Session.GetInt32(ActiveWorkspaceService.SessionKey);
            CurrentUserId = _userManager.GetUserId(User) ?? "";

            var caller = await _context.WorkspaceUsers.FirstOrDefaultAsync(w => w.WorkspaceId == activeWorkspaceId && w.UserId == CurrentUserId);
            if (caller == null || caller.Role != WorkspaceRoles.Owner) return Unauthorized();

            var targetUser = await _context.WorkspaceUsers.FirstOrDefaultAsync(w => w.WorkspaceId == activeWorkspaceId && w.UserId == targetUserId);

            // Privilege Escalation Prevention: An Owner cannot evict another Owner.
            if (targetUser != null && targetUser.Role != WorkspaceRoles.Owner)
            {
                _context.WorkspaceUsers.Remove(targetUser);
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = "Üye başarıyla ekipten çıkarıldı.";
            }

            return RedirectToPage();
        }

        // --- VOLUNTARY DEPARTURE LOGIC ---
        public async Task<IActionResult> OnPostLeaveWorkspaceAsync()
        {
            int? activeWorkspaceId = HttpContext.Session.GetInt32(ActiveWorkspaceService.SessionKey);
            CurrentUserId = _userManager.GetUserId(User) ?? "";

            var me = await _context.WorkspaceUsers.FirstOrDefaultAsync(w => w.WorkspaceId == activeWorkspaceId && w.UserId == CurrentUserId);

            if (me != null)
            {
                // Orphan Prevention: Owners cannot leave; they must transfer ownership or delete the tenant.
                if (me.Role == WorkspaceRoles.Owner)
                {
                    TempData["ErrorMessage"] = "Ekip kurucusu ekipten ayrılamaz; isterseniz ekibi silebilirsiniz.";
                    return RedirectToPage();
                }

                _context.WorkspaceUsers.Remove(me);
                await _context.SaveChangesAsync();

                // Clear session context upon departure.
                HttpContext.Session.Remove(ActiveWorkspaceService.SessionKey);
                TempData["StatusMessage"] = "Ekipten ayrıldınız.";
            }

            return RedirectToPage("/Index");
        }

        // --- TENANT ANNIHILATION (CASCADE DELETE) ---
        public async Task<IActionResult> OnPostDeleteWorkspaceAsync()
        {
            int? activeWorkspaceId = HttpContext.Session.GetInt32(ActiveWorkspaceService.SessionKey);
            CurrentUserId = _userManager.GetUserId(User) ?? "";

            var caller = await _context.WorkspaceUsers
                .Include(w => w.Workspace)
                .FirstOrDefaultAsync(w => w.WorkspaceId == activeWorkspaceId && w.UserId == CurrentUserId);

            if (caller == null || caller.Role != WorkspaceRoles.Owner)
                return Unauthorized();

            // Critical Guardrail: Prevent the deletion of the primary/default personal workspace.
            if (caller.Workspace?.IsPersonal == true)
            {
                TempData["ErrorMessage"] = "Kişisel alanınız silinemez; yalnız ekipler silinebilir.";
                return RedirectToPage();
            }

            var workspaceToDelete = await _context.Workspaces.FindAsync(activeWorkspaceId);
            if (workspaceToDelete != null)
            {
                // The meeting -> workspace foreign key does not cascade, so the team's meetings (with their comments;
                // action items, reactions, members and invitations cascade in the database) are removed first.
                var meetings = await _context.MeetingSummaries.Where(m => m.WorkspaceId == activeWorkspaceId).ToListAsync();
                var meetingIds = meetings.Select(m => m.Id).ToList();
                var comments = await _context.MeetingComments.Where(c => meetingIds.Contains(c.MeetingId)).ToListAsync();
                var audioFiles = meetings.Select(m => m.AudioFilePath).ToList();

                await using var transaction = await _context.Database.BeginTransactionAsync();
                _context.MeetingComments.RemoveRange(comments.Where(c => c.ParentCommentId != null));
                await _context.SaveChangesAsync();
                _context.MeetingComments.RemoveRange(comments.Where(c => c.ParentCommentId == null));
                _context.MeetingSummaries.RemoveRange(meetings);
                _context.Workspaces.Remove(workspaceToDelete);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                foreach (var audio in audioFiles) _audioStorage.Delete(audio);

                HttpContext.Session.Remove(ActiveWorkspaceService.SessionKey);
                TempData["StatusMessage"] = $"'{workspaceToDelete.Name}' ekibi silindi.";
            }

            return RedirectToPage("/Index");
        }
    }
}