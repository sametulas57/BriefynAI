using MeetingWeb.Models;
using MeetingWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MeetingWeb.Pages.Series;

[Authorize]
public class IndexModel : PageModel
{
    private readonly IMeetingSeriesService _series;
    private readonly IActiveWorkspaceService _activeWorkspace;
    private readonly UserManager<ApplicationUser> _users;

    public IndexModel(IMeetingSeriesService series, IActiveWorkspaceService activeWorkspace, UserManager<ApplicationUser> users)
    {
        _series = series;
        _activeWorkspace = activeWorkspace;
        _users = users;
    }

    public List<SeriesSummary> Series { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var userId = _users.GetUserId(User);
        var membership = await _activeWorkspace.GetActiveMembershipAsync(userId);
        if (membership != null) Series = await _series.ListAsync(userId, membership.WorkspaceId);
    }

    public async Task<IActionResult> OnPostCreateAsync(string? name)
    {
        var userId = _users.GetUserId(User);
        var membership = await _activeWorkspace.GetActiveMembershipAsync(userId);
        if (membership == null) return NotFound();
        var created = await _series.CreateAsync(userId, membership.WorkspaceId, name);
        if (created == null)
        {
            TempData["ErrorMessage"] = "Seri adı boş olamaz ve en çok 100 karakter olabilir.";
            return RedirectToPage();
        }
        return RedirectToPage("/Series/Show", new { id = created.Id });
    }
}
