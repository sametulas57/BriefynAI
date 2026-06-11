using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MeetingWeb.Data;

namespace MeetingWeb.Pages.Workspace
{
    // SECURITY GATEWAY: Restrict context switching mechanism to authenticated users only.
    [Authorize]
    public class SwitchModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public SwitchModel(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // HTTP GET: Intercepts the workspace switch request and securely updates the session state.
        public async Task<IActionResult> OnGetAsync(int workspaceId, string returnUrl = "/")
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null) return LocalRedirect(returnUrl);

            // IDOR PROTECTION: Validate that the requesting user actively belongs to the target tenant.
            // Executing an optimal O(1) boolean check via AnyAsync to preserve server resources.
            var hasAccess = await _context.WorkspaceUsers
                .AnyAsync(wu => wu.UserId == userId && wu.WorkspaceId == workspaceId);

            if (hasAccess)
            {
                // CONTEXT SWITCH: Overwrite the active tenant ID in the user's volatile session memory.
                HttpContext.Session.SetInt32("ActiveWorkspaceId", workspaceId);
            }

            // SECURITY: Mitigate Open Redirect vulnerabilities by enforcing local domain routing.
            if (Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            return RedirectToPage("/Index");
        }
    }
}