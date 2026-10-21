using System.Data;
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
        private readonly UserManager<ApplicationUser> _userManager;

        private readonly IActiveWorkspaceService _activeWorkspace;
        private readonly IAudioStorage _audioStorage;

        public MembersModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IActiveWorkspaceService activeWorkspace, IAudioStorage audioStorage)
        {
            _activeWorkspace = activeWorkspace;
            _audioStorage = audioStorage;
            _context = context;
            _userManager = userManager;
        }

        public string WorkspaceName { get; set; } = "";
        public WorkspaceRole? CurrentUserRole { get; set; }
        public string CurrentUserId { get; set; } = "";

        // The personal space cannot be deleted (checked again in OnPostDeleteWorkspaceAsync); decided by the flag, not the name.
        public bool IsPersonalWorkspace { get; set; }

        // Primary collection to hold the roster of workspace members.
        public List<WorkspaceMember> TeamMembers { get; set; } = new List<WorkspaceMember>();

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
            IsPersonalWorkspace = workspace?.IsPersonal == true;

            TeamMembers = await _context.WorkspaceMembers
                .Include(wu => wu.User)
                .Where(wu => wu.WorkspaceId == activeWorkspaceId)
                .ToListAsync();

            // Establish the Role-Based Access Control (RBAC) context for the viewing user.
            var currentUserRecord = TeamMembers.FirstOrDefault(wu => wu.UserId == CurrentUserId);
            if (currentUserRecord == null) return RedirectToPage("/Index");

            CurrentUserRole = currentUserRecord.Role;

            // Names and photos come with the members (Include above).
            foreach (var member in TeamMembers)
            {
                UserNames[member.UserId] = string.IsNullOrWhiteSpace(member.User.FullName) ? UserDisplay.UnknownName : member.User.FullName;
                if (member.User.AvatarFileName != null) UserAvatars[member.UserId] = UserDisplay.AvatarUrl(member.User.AvatarFileName);
            }

            return Page();
        }

        // --- MEMBER EVICTION LOGIC ---
        public async Task<IActionResult> OnPostRemoveMemberAsync(string targetUserId)
        {
            // The session's workspace is used as-is (not ActiveWorkspaceService's fallback): a destructive action must hit
            // the team the user was looking at, or nothing. Membership and role are verified against the database below.
            int? activeWorkspaceId = HttpContext.Session.GetInt32(ActiveWorkspaceService.SessionKey);
            CurrentUserId = _userManager.GetUserId(User) ?? "";

            var caller = await _context.WorkspaceMembers.FirstOrDefaultAsync(w => w.WorkspaceId == activeWorkspaceId && w.UserId == CurrentUserId);
            if (caller == null || caller.Role != WorkspaceRole.Owner) return Unauthorized();

            var targetUser = await _context.WorkspaceMembers.FirstOrDefaultAsync(w => w.WorkspaceId == activeWorkspaceId && w.UserId == targetUserId);

            // Privilege Escalation Prevention: An Owner cannot evict another Owner.
            if (targetUser != null && targetUser.Role != WorkspaceRole.Owner)
            {
                _context.WorkspaceMembers.Remove(targetUser);
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = "Üye başarıyla ekipten çıkarıldı.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostTransferOwnershipAsync(string targetUserId)
        {
            var workspaceId = HttpContext.Session.GetInt32(ActiveWorkspaceService.SessionKey);
            var callerId = _userManager.GetUserId(User);
            if (await _context.Workspaces.AnyAsync(w => w.Id == workspaceId && w.IsPersonal)) return Unauthorized();
            if (targetUserId == callerId)
            {
                TempData["ErrorMessage"] = "Kuruculuğu bu üyeye devredemezsiniz.";
                return RedirectToPage();
            }
            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var changedCaller = await _context.WorkspaceMembers
                .Where(m => m.WorkspaceId == workspaceId && m.UserId == callerId && m.Role == WorkspaceRole.Owner)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, WorkspaceRole.Member));
            if (changedCaller != 1) return Unauthorized();
            var changedTarget = await _context.WorkspaceMembers
                .Where(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId &&
                    m.Role == WorkspaceRole.Member && m.User.DeletionRequestedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, WorkspaceRole.Owner));
            if (changedTarget != 1)
            {
                TempData["ErrorMessage"] = "Kuruculuğu bu üyeye devredemezsiniz.";
                return RedirectToPage();
            }
            await tx.CommitAsync();
            TempData["StatusMessage"] = "Kuruculuk devredildi.";
            return RedirectToPage();
        }

        // --- VOLUNTARY DEPARTURE LOGIC ---
        public async Task<IActionResult> OnPostLeaveWorkspaceAsync()
        {
            // The session's workspace is used as-is (not ActiveWorkspaceService's fallback): a destructive action must hit
            // the team the user was looking at, or nothing. Membership and role are verified against the database below.
            int? activeWorkspaceId = HttpContext.Session.GetInt32(ActiveWorkspaceService.SessionKey);
            CurrentUserId = _userManager.GetUserId(User) ?? "";

            var me = await _context.WorkspaceMembers.FirstOrDefaultAsync(w => w.WorkspaceId == activeWorkspaceId && w.UserId == CurrentUserId);

            if (me != null)
            {
                // Orphan Prevention: Owners cannot leave; they must transfer ownership or delete the tenant.
                if (me.Role == WorkspaceRole.Owner)
                {
                    TempData["ErrorMessage"] = "Ekip kurucusu ekipten ayrılamaz; isterseniz ekibi silebilirsiniz.";
                    return RedirectToPage();
                }

                _context.WorkspaceMembers.Remove(me);
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
            // The session's workspace is used as-is (not ActiveWorkspaceService's fallback): a destructive action must hit
            // the team the user was looking at, or nothing. Membership and role are verified against the database below.
            int? activeWorkspaceId = HttpContext.Session.GetInt32(ActiveWorkspaceService.SessionKey);
            CurrentUserId = _userManager.GetUserId(User) ?? "";

            var caller = await _context.WorkspaceMembers
                .Include(w => w.Workspace)
                .FirstOrDefaultAsync(w => w.WorkspaceId == activeWorkspaceId && w.UserId == CurrentUserId);

            if (caller == null || caller.Role != WorkspaceRole.Owner)
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
                // Meetings, their lines, tasks, comments, reactions, members and invitations cascade in the database (ADR-0009).
                var audioFiles = await _context.Meetings
                    .Where(m => m.WorkspaceId == activeWorkspaceId && m.AudioFileName != null)
                    .Select(m => m.AudioFileName)
                    .ToListAsync();

                _context.Workspaces.Remove(workspaceToDelete);
                await _context.SaveChangesAsync();

                foreach (var audio in audioFiles) _audioStorage.Delete(audio);

                HttpContext.Session.Remove(ActiveWorkspaceService.SessionKey);
                TempData["StatusMessage"] = $"'{workspaceToDelete.Name}' ekibi silindi.";
            }

            return RedirectToPage("/Index");
        }
    }
}
