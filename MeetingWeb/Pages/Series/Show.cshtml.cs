using Hangfire;
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
    private readonly ISeriesAgendaService _agenda;
    private readonly IBackgroundJobClient _jobs;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public ShowModel(IMeetingSeriesService series, ApplicationDbContext db, UserManager<ApplicationUser> users, ISeriesAgendaService agenda, IBackgroundJobClient jobs)
    {
        _series = series;
        _db = db;
        _users = users;
        _agenda = agenda;
        _jobs = jobs;
    }

    public MeetingSeries Series { get; private set; } = default!;
    public List<Meeting> Meetings { get; private set; } = new();
    public bool CanManage { get; private set; }
    public SeriesAgenda Agenda { get; private set; } = default!;
    public List<WorkspaceMember> WorkspaceMembers { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var userId = _users.GetUserId(User);
        var series = await _series.FindAsync(userId, id);
        if (series == null) return NotFound();
        Series = series;
        Agenda = await _agenda.BuildAsync(id, DisplayTime.TodayIn(DateTime.UtcNow));
        WorkspaceMembers = await _db.WorkspaceMembers.AsNoTracking().Include(m => m.User).Include(m => m.Title)
            .Where(m => m.WorkspaceId == series.WorkspaceId).ToListAsync();
        CanManage = await _series.CanManageAsync(userId, series);
        Meetings = await _db.Meetings.AsNoTracking().Where(m => m.SeriesId == id && m.WorkspaceId == series.WorkspaceId)
            .OrderByDescending(m => m.CreatedAtUtc).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSendAgendaAsync(int id, List<string> selectedEmails, string? extraEmails)
    {
        var series = await _series.FindAsync(_users.GetUserId(User), id);
        if (series == null) return NotFound();
        var extras = (extraEmails ?? "")
            .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (extras.Count > MeetingWeb.Pages.DetailsModel.MaxExtraRecipients)
        {
            TempData["ErrorMessage"] = $"Ekip dışından en fazla {MeetingWeb.Pages.DetailsModel.MaxExtraRecipients} adres ekleyebilirsiniz. Gündem gönderilmedi.";
            return RedirectToPage(new { id });
        }
        var invalid = extras.Where(e => !ValidEmail(e)).ToList();
        if (invalid.Count > 0)
        {
            TempData["ErrorMessage"] = $"{string.Join(", ", invalid)} geçerli bir e-posta adresi değil. Düzeltip tekrar deneyin; gündem gönderilmedi.";
            return RedirectToPage(new { id });
        }
        var members = await _db.WorkspaceMembers.Where(m => m.WorkspaceId == series.WorkspaceId)
            .Select(m => m.User.Email).ToListAsync();
        var recipients = selectedEmails.Where(e => members.Contains(e, StringComparer.OrdinalIgnoreCase))
            .Concat(extras).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (recipients.Count == 0)
        {
            TempData["ErrorMessage"] = "Gündemi göndermek için en az bir kişi seçin ya da bir e-posta adresi yazın.";
            return RedirectToPage(new { id });
        }
        foreach (var email in recipients)
        {
            var protectedRecipient = EmailService.ProtectRecipient(email);
            _jobs.Enqueue<IEmailService>(x => x.SendSeriesAgendaAsync(id, protectedRecipient));
        }
        TempData["StatusMessage"] = "Gündem e-postayla gönderiliyor. Birkaç dakika içinde seçtiğiniz kişilerin gelen kutusunda olur.";
        return RedirectToPage(new { id });
    }

    private static bool ValidEmail(string value)
    {
        try { return new System.Net.Mail.MailAddress(value).Address == value; }
        catch (FormatException) { return false; }
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
