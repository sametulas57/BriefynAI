using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Services.ArchiveSearch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Pages;

[Authorize]
public class AskModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IActiveWorkspaceService _activeWorkspace;
    private readonly IAskService _ask;

    public AskModel(ApplicationDbContext db, UserManager<ApplicationUser> users,
        IActiveWorkspaceService activeWorkspace, IAskService ask)
    {
        _db = db;
        _users = users;
        _activeWorkspace = activeWorkspace;
        _ask = ask;
    }

    [BindProperty] public string? Question { get; set; }
    [BindProperty] public string Scope { get; set; } = "Archive";
    [BindProperty] public int? SeriesId { get; set; }
    [BindProperty] public int? MeetingId { get; set; }
    [BindProperty] public DateOnly? From { get; set; }
    [BindProperty] public DateOnly? To { get; set; }

    public List<MeetingSeries> Series { get; private set; } = new();
    public List<Meeting> Meetings { get; private set; } = new();
    public AskResult? Result { get; private set; }

    public async Task OnGetAsync(string? kapsam, int? hedef)
    {
        Scope = kapsam switch
        {
            "seri" => "Series",
            "toplanti" => "Meeting",
            _ => "Archive"
        };
        if (Scope == "Series") SeriesId = hedef;
        if (Scope == "Meeting") MeetingId = hedef;
        await LoadChoicesAsync();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var workspaceId = await LoadChoicesAsync();
        var scope = Scope switch
        {
            "Series" when SeriesId.HasValue => ArchiveScope.Series,
            "Meeting" when MeetingId.HasValue => ArchiveScope.Meeting,
            _ => ArchiveScope.Archive
        };
        var targetId = scope switch
        {
            ArchiveScope.Series => SeriesId,
            ArchiveScope.Meeting => MeetingId,
            _ => null
        };
        if (workspaceId.HasValue)
            Result = await _ask.AskAsync(_users.GetUserId(User), workspaceId.Value,
                new AskRequest(Question, scope, targetId, From, To), ct);
        else
            Result = new AskResult(AskStatus.EmptyArchive, 0, null, null, Array.Empty<AskSource>());
        return Page();
    }

    private async Task<int?> LoadChoicesAsync()
    {
        var membership = await _activeWorkspace.GetActiveMembershipAsync(_users.GetUserId(User));
        if (membership == null) return null;
        var workspaceId = membership.WorkspaceId;
        Series = await _db.MeetingSeries.AsNoTracking().Where(s => s.WorkspaceId == workspaceId)
            .OrderBy(s => s.Name).ToListAsync();
        Meetings = await _db.Meetings.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.Status == MeetingStatus.Completed)
            .OrderByDescending(m => m.CreatedAtUtc).ToListAsync();
        return workspaceId;
    }
}
