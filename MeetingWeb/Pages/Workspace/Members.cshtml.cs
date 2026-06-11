using MeetingWeb.Data;
using MeetingWeb.Models;
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

        public MembersModel(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
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

            // 1. Context Resolution: Attempt to retrieve the active tenant from the session.
            int? activeWorkspaceId = HttpContext.Session.GetInt32("ActiveWorkspaceId");

            // 2. Fallback Mechanism: Auto-select the highest privileged workspace if session is empty.
            if (activeWorkspaceId == null)
            {
                var userFirstWorkspace = await _context.WorkspaceUsers
                    .Where(wu => wu.UserId == CurrentUserId)
                    .OrderByDescending(wu => wu.Role) // Prioritize 'Owner' roles.
                    .Select(wu => wu.WorkspaceId)
                    .FirstOrDefaultAsync();

                if (userFirstWorkspace != 0)
                {
                    activeWorkspaceId = userFirstWorkspace;
                    HttpContext.Session.SetInt32("ActiveWorkspaceId", activeWorkspaceId.Value);
                }
                else
                {
                    // Redirect to onboarding if the user is completely orphaned.
                    return RedirectToPage("/Workspace/Create");
                }
            }

            // Retrieve current workspace metadata.
            var workspace = await _context.Workspaces.FindAsync(activeWorkspaceId);
            WorkspaceName = workspace?.Name ?? "Çalışma Alanı";

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
                .Where(c => userIds.Contains(c.UserId) && (c.ClaimType == "FullName" || c.ClaimType == "AvatarUrl" || c.ClaimType == "ProfilePicture"))
                .ToListAsync();

            // Populate lookup dictionaries for efficient UI rendering.
            foreach (var claim in claims)
            {
                if (claim.ClaimType == "FullName")
                {
                    UserNames[claim.UserId] = claim.ClaimValue ?? "İsimsiz Üye";
                }
                else if (claim.ClaimType == "AvatarUrl" || claim.ClaimType == "ProfilePicture")
                {
                    UserAvatars[claim.UserId] = claim.ClaimValue ?? "";
                }
            }

            return Page();
        }

        // --- INVITATION DISPATCH (Placeholder for Email Service) ---
        public async Task<IActionResult> OnPostInviteAsync(string invitedEmail)
        {
            int? activeWorkspaceId = HttpContext.Session.GetInt32("ActiveWorkspaceId");
            CurrentUserId = _userManager.GetUserId(User) ?? "";

            // Authorization Firewall: Verify ownership privileges.
            var caller = await _context.WorkspaceUsers
                .FirstOrDefaultAsync(w => w.WorkspaceId == activeWorkspaceId && w.UserId == CurrentUserId);

            if (caller == null || caller.Role != "Sahip")
            {
                TempData["ErrorMessage"] = "Güvenlik İhlali: Sadece şirket kurucuları (Sahipler) yeni üye davet edebilir.";
                return RedirectToPage();
            }

            if (string.IsNullOrWhiteSpace(invitedEmail))
            {
                TempData["ErrorMessage"] = "Lütfen geçerli bir e-posta adresi girin.";
                return RedirectToPage();
            }

            // TODO: Integrate SMTP or third-party email provider (e.g., SendGrid) here.
            TempData["StatusMessage"] = $"{invitedEmail} adresine davet başarıyla gönderildi!";
            return RedirectToPage();
        }

        // --- MEMBER EVICTION LOGIC ---
        public async Task<IActionResult> OnPostRemoveMemberAsync(string targetUserId)
        {
            int? activeWorkspaceId = HttpContext.Session.GetInt32("ActiveWorkspaceId");
            CurrentUserId = _userManager.GetUserId(User) ?? "";

            var caller = await _context.WorkspaceUsers.FirstOrDefaultAsync(w => w.WorkspaceId == activeWorkspaceId && w.UserId == CurrentUserId);
            if (caller == null || caller.Role != "Sahip") return Unauthorized();

            var targetUser = await _context.WorkspaceUsers.FirstOrDefaultAsync(w => w.WorkspaceId == activeWorkspaceId && w.UserId == targetUserId);

            // Privilege Escalation Prevention: An Owner cannot evict another Owner.
            if (targetUser != null && targetUser.Role != "Sahip")
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
            int? activeWorkspaceId = HttpContext.Session.GetInt32("ActiveWorkspaceId");
            CurrentUserId = _userManager.GetUserId(User) ?? "";

            var me = await _context.WorkspaceUsers.FirstOrDefaultAsync(w => w.WorkspaceId == activeWorkspaceId && w.UserId == CurrentUserId);

            if (me != null)
            {
                // Orphan Prevention: Owners cannot leave; they must transfer ownership or delete the tenant.
                if (me.Role == "Sahip")
                {
                    TempData["ErrorMessage"] = "Kurucu sahipler çalışma alanından ayrılamaz. Ekibi tamamen silmeniz gerekir.";
                    return RedirectToPage();
                }

                _context.WorkspaceUsers.Remove(me);
                await _context.SaveChangesAsync();

                // Clear session context upon departure.
                HttpContext.Session.Remove("ActiveWorkspaceId");
                TempData["StatusMessage"] = "Çalışma alanından başarıyla ayrıldınız.";
            }

            return RedirectToPage("/Index");
        }

        // --- TENANT ANNIHILATION (CASCADE DELETE) ---
        public async Task<IActionResult> OnPostDeleteWorkspaceAsync()
        {
            int? activeWorkspaceId = HttpContext.Session.GetInt32("ActiveWorkspaceId");
            CurrentUserId = _userManager.GetUserId(User) ?? "";

            var caller = await _context.WorkspaceUsers
                .Include(w => w.Workspace)
                .FirstOrDefaultAsync(w => w.WorkspaceId == activeWorkspaceId && w.UserId == CurrentUserId);

            if (caller == null || caller.Role != "Sahip")
                return Unauthorized();

            // Critical Guardrail: Prevent the deletion of the primary/default personal workspace.
            if (caller.Workspace?.Name != null && (caller.Workspace.Name == "Kişisel" || caller.Workspace.Name.Contains("Kişisel")))
            {
                TempData["ErrorMessage"] = "Güvenlik İhlali: Varsayılan 'Kişisel' çalışma alanınız silinemez.";
                return RedirectToPage();
            }

            var workspaceToDelete = await _context.Workspaces.FindAsync(activeWorkspaceId);
            if (workspaceToDelete != null)
            {
                // Note: Configure EF Core cascade delete to clean up associated MeetingSummaries and Invitations.
                _context.Workspaces.Remove(workspaceToDelete);
                await _context.SaveChangesAsync();

                HttpContext.Session.Remove("ActiveWorkspaceId");
                TempData["StatusMessage"] = $"'{workspaceToDelete.Name}' ekibi ve çalışma alanı tamamen silindi.";
            }

            return RedirectToPage("/Index");
        }
    }
}