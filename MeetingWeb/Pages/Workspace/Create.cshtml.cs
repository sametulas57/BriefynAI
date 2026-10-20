using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;

namespace MeetingWeb.Pages.Workspace
{
    // SECURITY GATEWAY: Enforce authentication prior to tenant creation.
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public CreateModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
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

            // Provision the new Workspace (Tenant) with the creator as its 'Owner', in one save.
            var newWorkspace = new MeetingWeb.Models.Workspace { Name = WorkspaceName, CreatedByUserId = userId };
            newWorkspace.Members.Add(new WorkspaceMember { UserId = userId, Role = WorkspaceRole.Owner });
            _context.Workspaces.Add(newWorkspace);
            await _context.SaveChangesAsync();

            // 3. UX Optimization: Seamlessly inject the newly created workspace ID into the active session.
            HttpContext.Session.SetInt32(ActiveWorkspaceService.SessionKey, newWorkspace.Id);

            // Set a volatile status message for the UI toast notification.
            TempData["StatusMessage"] = $"🏢 '{WorkspaceName}' başarıyla oluşturuldu ve aktif edildi.";
            return RedirectToPage("/Index");
        }
    }
}