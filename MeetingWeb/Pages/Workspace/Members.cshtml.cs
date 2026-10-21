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

        // The team's titles, for the title list and the assignment choices.
        public List<WorkspaceTitle> Titles { get; set; } = new();
        public bool CanManageTitles { get; set; }

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
                .Include(wu => wu.Title)
                .Where(wu => wu.WorkspaceId == activeWorkspaceId)
                .ToListAsync();
            Titles = await _context.WorkspaceTitles.Where(t => t.WorkspaceId == activeWorkspaceId).OrderBy(t => t.Name).ToListAsync();

            // Establish the Role-Based Access Control (RBAC) context for the viewing user.
            var currentUserRecord = TeamMembers.FirstOrDefault(wu => wu.UserId == CurrentUserId);
            if (currentUserRecord == null) return RedirectToPage("/Index");

            CurrentUserRole = currentUserRecord.Role;
            CanManageTitles = currentUserRecord.CanManageTitles && !IsPersonalWorkspace;

            // Names and photos come with the members (Include above).
            foreach (var member in TeamMembers)
            {
                UserNames[member.UserId] = string.IsNullOrWhiteSpace(member.User.FullName) ? UserDisplay.UnknownName : member.User.FullName;
                if (member.User.AvatarFileName != null) UserAvatars[member.UserId] = UserDisplay.AvatarUrl(member.User.AvatarFileName);
            }

            return Page();
        }

        // --- TITLES ---
        // Like the other actions here, the team is the one the user was looking at (session); the permission is checked
        // against the database. The personal space has no titles.
        private async Task<WorkspaceMember?> TitleManagerAsync()
        {
            var workspaceId = HttpContext.Session.GetInt32(ActiveWorkspaceService.SessionKey);
            var userId = _userManager.GetUserId(User);
            var caller = await _context.WorkspaceMembers.Include(m => m.Workspace)
                .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId);
            return caller != null && caller.CanManageTitles && !caller.Workspace.IsPersonal ? caller : null;
        }

        // Returns an error message, or null when the name and icon can be saved.
        private async Task<string?> ValidateTitleAsync(int workspaceId, string name, string? icon, int? exceptId)
        {
            if (name.Length == 0 || name.Length > WorkspaceTitle.NameMaxLength)
                return $"Unvan adı boş olamaz ve en fazla {WorkspaceTitle.NameMaxLength} karakter olabilir.";
            if (!TitleIcons.IsAllowed(icon)) return "Lütfen listedeki ikonlardan birini seçin.";
            if (await _context.WorkspaceTitles.AnyAsync(t => t.WorkspaceId == workspaceId && t.Name == name && t.Id != exceptId))
                return "Bu adda bir unvan zaten var.";
            return null;
        }

        // The unique index still guards against two saves at the same moment.
        private async Task<IActionResult> SaveTitleAsync(string doneMessage)
        {
            try
            {
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = doneMessage;
            }
            catch (DbUpdateException)
            {
                TempData["ErrorMessage"] = "Bu adda bir unvan zaten var.";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostCreateTitleAsync(string? titleName, string? titleIcon)
        {
            var caller = await TitleManagerAsync();
            if (caller == null) return Unauthorized();

            var name = (titleName ?? "").Trim();
            var icon = string.IsNullOrEmpty(titleIcon) ? null : titleIcon;
            if (await ValidateTitleAsync(caller.WorkspaceId, name, icon, null) is { } error)
            {
                TempData["ErrorMessage"] = error;
                return RedirectToPage();
            }

            _context.WorkspaceTitles.Add(new WorkspaceTitle { WorkspaceId = caller.WorkspaceId, Name = name, Icon = icon });
            return await SaveTitleAsync($"'{name}' unvanı eklendi.");
        }

        public async Task<IActionResult> OnPostUpdateTitleAsync(int titleId, string? titleName, string? titleIcon)
        {
            var caller = await TitleManagerAsync();
            if (caller == null) return Unauthorized();
            var title = await _context.WorkspaceTitles.FirstOrDefaultAsync(t => t.Id == titleId && t.WorkspaceId == caller.WorkspaceId);
            if (title == null) return NotFound();

            var name = (titleName ?? "").Trim();
            var icon = string.IsNullOrEmpty(titleIcon) ? null : titleIcon;
            if (await ValidateTitleAsync(caller.WorkspaceId, name, icon, title.Id) is { } error)
            {
                TempData["ErrorMessage"] = error;
                return RedirectToPage();
            }

            title.Name = name;
            title.Icon = icon;
            return await SaveTitleAsync("Unvan güncellendi.");
        }

        // People with the title stay in the team; only their title is cleared.
        public async Task<IActionResult> OnPostDeleteTitleAsync(int titleId)
        {
            var caller = await TitleManagerAsync();
            if (caller == null) return Unauthorized();
            var title = await _context.WorkspaceTitles.FirstOrDefaultAsync(t => t.Id == titleId && t.WorkspaceId == caller.WorkspaceId);
            if (title == null) return NotFound();

            await using var transaction = await _context.Database.BeginTransactionAsync();
            await _context.WorkspaceMembers.Where(m => m.TitleId == title.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.TitleId, (int?)null));
            _context.WorkspaceTitles.Remove(title);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["StatusMessage"] = $"'{title.Name}' unvanı silindi; bu unvanı taşıyanlar ekipte kalır.";
            return RedirectToPage();
        }

        // An empty titleId clears the person's title.
        public async Task<IActionResult> OnPostAssignTitleAsync(string targetUserId, int? titleId)
        {
            var caller = await TitleManagerAsync();
            if (caller == null) return Unauthorized();
            var target = await _context.WorkspaceMembers.FirstOrDefaultAsync(m => m.WorkspaceId == caller.WorkspaceId && m.UserId == targetUserId);
            if (target == null) return NotFound();
            if (titleId != null && !await _context.WorkspaceTitles.AnyAsync(t => t.Id == titleId && t.WorkspaceId == caller.WorkspaceId))
                return NotFound();

            target.TitleId = titleId;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // The title was deleted in another tab between the check above and this save.
                TempData["ErrorMessage"] = "Bu unvan az önce silindi. Listeyi yenileyip tekrar deneyin.";
                return RedirectToPage();
            }
            TempData["StatusMessage"] = titleId == null ? "Unvan kaldırıldı." : "Unvan atandı.";
            return RedirectToPage();
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

            // Recheck both roles in the DELETE itself: ownership may have changed since the read above.
            var removed = await _context.WorkspaceMembers
                .Where(m => m.WorkspaceId == activeWorkspaceId && m.UserId == targetUserId &&
                    m.Role == WorkspaceRole.Member &&
                    _context.WorkspaceMembers.Any(owner => owner.WorkspaceId == m.WorkspaceId &&
                        owner.UserId == CurrentUserId && owner.Role == WorkspaceRole.Owner))
                .ExecuteDeleteAsync();
            TempData[removed == 1 ? "StatusMessage" : "ErrorMessage"] = removed == 1
                ? "Üye başarıyla ekipten çıkarıldı."
                : "Üye çıkarılamadı. Ekip üyeliklerini yeniden kontrol edin.";

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
            // Under Serializable, this read holds a lock on the target user until commit. Read it before
            // changing memberships so a concurrent deletion request cannot pass the ownership check.
            if (!await _context.Users.AnyAsync(u => u.Id == targetUserId && u.DeletionRequestedAtUtc == null))
            {
                TempData["ErrorMessage"] = "Kuruculuğu bu üyeye devredemezsiniz.";
                return RedirectToPage();
            }
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

                var removed = await _context.WorkspaceMembers
                    .Where(m => m.WorkspaceId == activeWorkspaceId && m.UserId == CurrentUserId &&
                        m.Role == WorkspaceRole.Member)
                    .ExecuteDeleteAsync();
                if (removed != 1)
                {
                    TempData["ErrorMessage"] = "Ekipten ayrılamadınız. Üyeliğinizi yeniden kontrol edin.";
                    return RedirectToPage();
                }

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
