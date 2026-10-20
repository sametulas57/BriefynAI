using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;

namespace MeetingWeb.Pages.Workspace
{
    // SECURITY GATEWAY: Enforce authentication for the entire page.
    [Authorize]
    public class InviteModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        private readonly IActiveWorkspaceService _activeWorkspace;

        // Inviting into a personal space is allowed but gently discouraged on the page.
        public bool IsPersonalWorkspace { get; set; }

        public InviteModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IActiveWorkspaceService activeWorkspace)
        {
            _activeWorkspace = activeWorkspace;
            _context = context;
            _userManager = userManager;
        }

        public string GeneratedLink { get; set; } = "";
        public string WorkspaceName { get; set; } = "";

        // HTTP GET: Intercepts page load to verify UI-level access rights.
        public async Task<IActionResult> OnGetAsync()
        {
            var currentUserId = _userManager.GetUserId(User);
            var membership = await _activeWorkspace.GetActiveMembershipAsync(currentUserId);
            if (membership == null) return RedirectToPage("/Workspace/Create");
            int? activeWorkspaceId = membership.WorkspaceId;
            IsPersonalWorkspace = membership.Workspace.IsPersonal;

            // DEFENSE IN DEPTH - LAYER 1: UI VISIBILITY PROTECTION
            // Ensure the requesting user holds the 'Owner' role before rendering the invitation UI.
            var userRole = await _context.WorkspaceMembers
                .Where(wu => wu.WorkspaceId == activeWorkspaceId && wu.UserId == currentUserId)
                .Select(wu => wu.Role)
                .FirstOrDefaultAsync();

            if (userRole != WorkspaceRole.Owner)
            {
                TempData["ErrorMessage"] = "Davet bağlantısını yalnız ekip kurucusu oluşturabilir.";
                return RedirectToPage("/Workspace/Members");
            }

            var workspace = await _context.Workspaces.FindAsync(activeWorkspaceId);
            WorkspaceName = workspace?.Name ?? "Ekip";

            return Page();
        }

        // HTTP POST: Intercepts form submission to execute the invitation logic safely.
        public async Task<IActionResult> OnPostAsync()
        {
            var currentUserId = _userManager.GetUserId(User);
            var membership = await _activeWorkspace.GetActiveMembershipAsync(currentUserId);
            if (membership == null) return RedirectToPage("/Workspace/Create");
            int? activeWorkspaceId = membership.WorkspaceId;
            IsPersonalWorkspace = membership.Workspace.IsPersonal;

            // DEFENSE IN DEPTH - LAYER 2: BACKEND FIREWALL (API PROTECTION)
            // Re-verify the role to explicitly prevent bypass attacks via tools like Postman or cURL.
            var userRole = await _context.WorkspaceMembers
                .Where(wu => wu.WorkspaceId == activeWorkspaceId && wu.UserId == currentUserId)
                .Select(wu => wu.Role)
                .FirstOrDefaultAsync();

            if (userRole != WorkspaceRole.Owner)
            {
                TempData["ErrorMessage"] = "Davet bağlantısını yalnız ekip kurucusu oluşturabilir.";
                return RedirectToPage("/Workspace/Members");
            }

            // 1. Token Generation: a random, single-use token; only its hash is stored (ADR-0009).
            var token = WorkspaceInvitation.NewToken();
            var invitation = new WorkspaceInvitation
            {
                WorkspaceId = activeWorkspaceId.Value,
                TokenHash = WorkspaceInvitation.HashOf(token),
                CreatedByUserId = currentUserId
            };

            _context.WorkspaceInvitations.Add(invitation);
            await _context.SaveChangesAsync();

            // 2. Dynamic Routing: Construct the absolute URL matching the current host environment.
            var request = HttpContext.Request;
            GeneratedLink = $"{request.Scheme}://{request.Host}/Workspace/Join?token={token}";

            var workspace = await _context.Workspaces.FindAsync(activeWorkspaceId);
            WorkspaceName = workspace?.Name ?? "Ekip";

            return Page();
        }
    }
}