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
    public string? ScopeMessage { get; private set; }
    public bool IsNarrowed => Scope is "Series" or "Meeting" || From.HasValue || To.HasValue;

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
        // A chosen series or meeting with no target is never widened to the whole archive.
        ScopeMessage = Scope switch
        {
            "Series" when !SeriesId.HasValue => "Lütfen bir seri seçin.",
            "Meeting" when !MeetingId.HasValue => "Lütfen bir toplantı seçin.",
            _ => null
        };
        if (ScopeMessage != null) return Answer();
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
        return Answer();
    }

    // ask.js gets only the answer to add to the conversation; without scripts the whole page comes back.
    private IActionResult Answer() =>
        Request.Headers.XRequestedWith == "XMLHttpRequest" ? Partial("_AskAnswer", this) : Page();

    public string ScopeLabel()
    {
        var place = Scope switch
        {
            "Series" => Series.FirstOrDefault(s => s.Id == SeriesId)?.Name ?? "Bir seri",
            "Meeting" => Meetings.FirstOrDefault(m => m.Id == MeetingId)?.Title ?? "Bir toplantı",
            _ => "Tüm arşiv"
        };
        static string Day(DateOnly date) => date.ToString("dd.MM.yyyy", DisplayTime.Culture);
        return (From, To) switch
        {
            ({ } from, { } to) => $"{place}, {Day(from)} ile {Day(to)} arası",
            ({ } from, null) => $"{place}, {Day(from)} ve sonrası",
            (null, { } to) => $"{place}, {Day(to)} ve öncesi",
            _ => place
        };
    }

    // Each question names the meeting or series because the search matches those names in every passage.
    public IReadOnlyList<AskStarter> Starters()
    {
        static string Short(string name) => name.Length <= 120 ? name : name[..120].TrimEnd();
        var starters = new List<AskStarter>();
        if (Meetings.Count > 0)
            starters.Add(new AskStarter($"“{Short(Meetings[0].Title)}” kaydında hangi kararlar alındı?", "Meeting", Meetings[0].Id));
        if (Meetings.Count > 1)
            starters.Add(new AskStarter($"“{Short(Meetings[1].Title)}” sonrasında kime hangi iş kaldı?", "Meeting", Meetings[1].Id));
        if (Series.Count > 0)
            starters.Add(new AskStarter($"“{Short(Series[0].Name)}” serisinde hangi konular hâlâ bekliyor?", "Series", Series[0].Id));
        return starters;
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

public record AskStarter(string Question, string Scope, int TargetId);
