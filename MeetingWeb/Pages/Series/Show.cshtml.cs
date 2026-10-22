using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Pages.Series;

[Authorize]
public class ShowModel : PageModel
{
    private readonly IMeetingSeriesService _series;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public ShowModel(IMeetingSeriesService series, ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        _series = series;
        _db = db;
        _users = users;
    }

    public MeetingSeries Series { get; private set; } = default!;
    public List<Meeting> Meetings { get; private set; } = new();
    public bool CanManage { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var userId = _users.GetUserId(User);
        var series = await _series.FindAsync(userId, id);
        if (series == null) return NotFound();
        Series = series;
        CanManage = await _series.CanManageAsync(userId, series);
        Meetings = await _db.Meetings.AsNoTracking().Where(m => m.SeriesId == id && m.WorkspaceId == series.WorkspaceId)
            .OrderByDescending(m => m.CreatedAtUtc).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRenameAsync(int id, string? name)
    {
        var result = await _series.RenameAsync(_users.GetUserId(User), id, name);
        if (result == SeriesResult.NotFound) return NotFound();
        if (result == SeriesResult.Forbidden) return Forbid();
        if (result == SeriesResult.Invalid) TempData["ErrorMessage"] = "Seri adı boş olamaz ve en çok 100 karakter olabilir.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var result = await _series.DeleteAsync(_users.GetUserId(User), id);
        if (result == SeriesResult.NotFound) return NotFound();
        if (result == SeriesResult.Forbidden) return Forbid();
        return RedirectToPage("/Series/Index");
    }
}
