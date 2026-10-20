using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;

namespace MeetingWeb.Pages.Workspace
{
    // SECURITY GATEWAY: Enforce authentication to prevent anonymous token exploitation.
    [Authorize]
    public class JoinModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public JoinModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
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
                .FirstOrDefaultAsync(i => i.TokenHash == WorkspaceInvitation.HashOf(Token));

            // VALIDATION PIPELINE: Check for non-existent, expired, or previously consumed tokens.
            if (invitation == null)
            {
                ErrorMessage = "Böyle bir davet bulunamadı veya sistemden silinmiş.";
                return Page();
            }

            if (!invitation.IsUsable(DateTime.UtcNow))
            {
                ErrorMessage = "Bu davet linkinin süresi dolmuş veya daha önce kullanılmış.";
                return Page();
            }

            WorkspaceName = invitation.Workspace?.Name ?? "Bilinmeyen Ekip";
            return Page();
        }

        // HTTP POST: Executes the final join operation and invalidates the token.
        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Token)) return RedirectToPage("/Index");

            var invitation = await _context.WorkspaceInvitations
                .Include(i => i.Workspace)
                .FirstOrDefaultAsync(i => i.TokenHash == WorkspaceInvitation.HashOf(Token));

            // REDUNDANT SECURITY CHECK: Re-validate status before database write to prevent race conditions.
            if (invitation == null || !invitation.IsUsable(DateTime.UtcNow))
            {
                ErrorMessage = "Bu davet geçerliliğini yitirmiş.";
                return Page();
            }

            var userId = _userManager.GetUserId(User);
            if (userId == null) return RedirectToPage("/Auth/Login");

            // CONSUME TOKEN first, with one conditional update: of two people using the same link at once, only one
            // gets a row back; the other is refused. Joining happens in the same transaction.
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var consumed = await _context.WorkspaceInvitations
                .Where(i => i.Id == invitation.Id && i.UsedAtUtc == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.UsedAtUtc, DateTime.UtcNow)
                    .SetProperty(i => i.UsedByUserId, userId));
            if (consumed == 0)
            {
                ErrorMessage = "Bu davet geçerliliğini yitirmiş.";
                return Page();
            }

            // IDEMPOTENCY CHECK: Ensure the user is not already a member to prevent duplicate join records.
            var isAlreadyMember = await _context.WorkspaceMembers
                .AnyAsync(wu => wu.WorkspaceId == invitation.WorkspaceId && wu.UserId == userId);

            if (!isAlreadyMember)
            {
                // Assign the user with the default 'Member' role.
                _context.WorkspaceMembers.Add(new WorkspaceMember
                {
                    WorkspaceId = invitation.WorkspaceId,
                    UserId = userId,
                    Role = WorkspaceRole.Member
                });
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // CONTEXT SWITCH: Immediately update the session to reflect the new workspace as active.
            HttpContext.Session.SetInt32(ActiveWorkspaceService.SessionKey, invitation.WorkspaceId);

            TempData["StatusMessage"] = $"Tebrikler! '{invitation.Workspace?.Name}' ekibine başarıyla katıldınız.";

            return RedirectToPage("/History");
        }
    }
}