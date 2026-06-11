using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using MeetingWeb.Data;
using MeetingWeb.Models;

namespace MeetingWeb.Pages.Workspace
{
    // SECURITY GATEWAY: Enforce authentication to prevent anonymous token exploitation.
    [Authorize]
    public class JoinModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public JoinModel(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public string WorkspaceName { get; set; } = "";
        public string ErrorMessage { get; set; } = "";

        // Automatically captures the 'token' parameter from the query string (e.g., ?token=xyz)
        [BindProperty(SupportsGet = true)]
        public string Token { get; set; } = "";

        // HTTP GET: Validates the token's existence, expiration, and status upon page entry.
        public async Task<IActionResult> OnGetAsync()
        {
            if (string.IsNullOrEmpty(Token))
            {
                ErrorMessage = "Geçersiz veya eksik davet linki.";
                return Page();
            }

            // Retrieve invitation along with the associated tenant (Workspace) data.
            var invitation = await _context.WorkspaceInvitations
                .Include(i => i.Workspace)
                .FirstOrDefaultAsync(i => i.Token == Token);

            // VALIDATION PIPELINE: Check for non-existent, expired, or previously consumed tokens.
            if (invitation == null)
            {
                ErrorMessage = "Böyle bir davet bulunamadı veya sistemden silinmiş.";
                return Page();
            }

            if (invitation.IsUsed || invitation.ExpiresAt < DateTime.UtcNow)
            {
                ErrorMessage = "Bu davet linkinin süresi dolmuş veya daha önce kullanılmış.";
                return Page();
            }

            WorkspaceName = invitation.Workspace?.Name ?? "Bilinmeyen Çalışma Alanı";
            return Page();
        }

        // HTTP POST: Executes the final join operation and invalidates the token.
        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Token)) return RedirectToPage("/Index");

            var invitation = await _context.WorkspaceInvitations
                .Include(i => i.Workspace)
                .FirstOrDefaultAsync(i => i.Token == Token);

            // REDUNDANT SECURITY CHECK: Re-validate status before database write to prevent race conditions.
            if (invitation == null || invitation.IsUsed || invitation.ExpiresAt < DateTime.UtcNow)
            {
                ErrorMessage = "Bu davet geçerliliğini yitirmiş.";
                return Page();
            }

            var userId = _userManager.GetUserId(User);
            if (userId == null) return RedirectToPage("/Auth/Login");

            // IDEMPOTENCY CHECK: Ensure the user is not already a member to prevent duplicate join records.
            var isAlreadyMember = await _context.WorkspaceUsers
                .AnyAsync(wu => wu.WorkspaceId == invitation.WorkspaceId && wu.UserId == userId);

            if (!isAlreadyMember)
            {
                // Assign the user with the default 'Member' role.
                var workspaceUser = new WorkspaceUser
                {
                    WorkspaceId = invitation.WorkspaceId,
                    UserId = userId,
                    Role = "Üye"
                };
                _context.WorkspaceUsers.Add(workspaceUser);
            }

            // CONSUME TOKEN: Mark as used to enforce the single-use security policy.
            invitation.IsUsed = true;
            await _context.SaveChangesAsync();

            // CONTEXT SWITCH: Immediately update the session to reflect the new workspace as active.
            HttpContext.Session.SetInt32("ActiveWorkspaceId", invitation.WorkspaceId);

            TempData["StatusMessage"] = $"Tebrikler! '{invitation.Workspace?.Name}' ekibine başarıyla katıldınız.";

            return RedirectToPage("/History");
        }
    }
}