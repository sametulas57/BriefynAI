using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MeetingWeb.Data;
using MeetingWeb.Models;

namespace MeetingWeb.Pages.Workspace
{
    // SECURITY GATEWAY: Enforce authentication prior to tenant creation.
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public CreateModel(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // Data binding for the incoming workspace name payload.
        [BindProperty]
        public string WorkspaceName { get; set; } = string.Empty;

        // HTTP POST handler: Executed upon form submission to provision a new tenant.
        public async Task<IActionResult> OnPostAsync()
        {
            // Input validation fallback.
            if (string.IsNullOrWhiteSpace(WorkspaceName)) return Page();

            var userId = _userManager.GetUserId(User);
            if (userId == null) return RedirectToPage("/Auth/Login");

            // 1. Core Entity Creation: Provision the new Workspace (Tenant) in the database.
            var newWorkspace = new MeetingWeb.Models.Workspace { Name = WorkspaceName };
            _context.Workspaces.Add(newWorkspace);

            // Commit to database sequentially to retrieve the newly generated Identity ID.
            await _context.SaveChangesAsync();

            // 2. Role-Based Access Control (RBAC): Bind the creator as the 'Owner' of the new workspace.
            var wsUser = new WorkspaceUser
            {
                WorkspaceId = newWorkspace.Id,
                UserId = userId,
                Role = "Sahip"
            };
            _context.WorkspaceUsers.Add(wsUser);
            await _context.SaveChangesAsync();

            // 3. UX Optimization: Seamlessly inject the newly created workspace ID into the active session.
            HttpContext.Session.SetInt32("ActiveWorkspaceId", newWorkspace.Id);

            // Set a volatile status message for the UI toast notification.
            TempData["StatusMessage"] = $"🏢 '{WorkspaceName}' başarıyla oluşturuldu ve aktif edildi.";
            return RedirectToPage("/Index");
        }
    }
}