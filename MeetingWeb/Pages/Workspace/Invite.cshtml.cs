using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MeetingWeb.Data;
using MeetingWeb.Models;

namespace MeetingWeb.Pages.Workspace
{
    // SECURITY GATEWAY: Enforce authentication for the entire page.
    [Authorize]
    public class InviteModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public InviteModel(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public string GeneratedLink { get; set; } = "";
        public string WorkspaceName { get; set; } = "";

        // HTTP GET: Intercepts page load to verify UI-level access rights.
        public async Task<IActionResult> OnGetAsync()
        {
            int? activeWorkspaceId = HttpContext.Session.GetInt32("ActiveWorkspaceId");
            if (activeWorkspaceId == null) return RedirectToPage("/Index");

            var currentUserId = _userManager.GetUserId(User);

            // DEFENSE IN DEPTH - LAYER 1: UI VISIBILITY PROTECTION
            // Ensure the requesting user holds the 'Owner' role before rendering the invitation UI.
            var userRole = await _context.WorkspaceUsers
                .Where(wu => wu.WorkspaceId == activeWorkspaceId && wu.UserId == currentUserId)
                .Select(wu => wu.Role)
                .FirstOrDefaultAsync();

            if (userRole != "Sahip")
            {
                TempData["ErrorMessage"] = "Yetki İhlali: Bu sayfaya sadece şirket kurucuları erişebilir.";
                return RedirectToPage("/Workspace/Members");
            }

            var workspace = await _context.Workspaces.FindAsync(activeWorkspaceId);
            WorkspaceName = workspace?.Name ?? "Çalışma Alanı";

            return Page();
        }

        // HTTP POST: Intercepts form submission to execute the invitation logic safely.
        public async Task<IActionResult> OnPostAsync()
        {
            int? activeWorkspaceId = HttpContext.Session.GetInt32("ActiveWorkspaceId");
            if (activeWorkspaceId == null) return RedirectToPage("/Index");

            var currentUserId = _userManager.GetUserId(User);

            // DEFENSE IN DEPTH - LAYER 2: BACKEND FIREWALL (API PROTECTION)
            // Re-verify the role to explicitly prevent bypass attacks via tools like Postman or cURL.
            var userRole = await _context.WorkspaceUsers
                .Where(wu => wu.WorkspaceId == activeWorkspaceId && wu.UserId == currentUserId)
                .Select(wu => wu.Role)
                .FirstOrDefaultAsync();

            if (userRole != "Sahip")
            {
                TempData["ErrorMessage"] = "Güvenlik İhlali: Sadece kurucular davet linki üretebilir.";
                return RedirectToPage("/Workspace/Members");
            }

            // 1. Token Generation: Create a cryptographically secure, single-use invitation.
            var invitation = new WorkspaceInvitation
            {
                WorkspaceId = activeWorkspaceId.Value,
                Token = Guid.NewGuid().ToString(), // High-entropy GUID
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7), // Time-To-Live (TTL) set to 7 days
                IsUsed = false
            };

            _context.WorkspaceInvitations.Add(invitation);
            await _context.SaveChangesAsync();

            // 2. Dynamic Routing: Construct the absolute URL matching the current host environment.
            var request = HttpContext.Request;
            GeneratedLink = $"{request.Scheme}://{request.Host}/Workspace/Join?token={invitation.Token}";

            var workspace = await _context.Workspaces.FindAsync(activeWorkspaceId);
            WorkspaceName = workspace?.Name ?? "Çalışma Alanı";

            return Page();
        }
    }
}