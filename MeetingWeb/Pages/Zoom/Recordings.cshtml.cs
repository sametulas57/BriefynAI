using MeetingWeb.Data;
using MeetingWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Pages.Zoom
{
    // "Zoom kayıtlarım": the user's cloud recordings of the last 30 days, taken into the active team one by one.
    // The user comes only from the session; the connection used is always the user's own.
    [Authorize]
    public class RecordingsModel : PageModel
    {
        private readonly IZoomService _zoom;
        private readonly UserManager<Models.ApplicationUser> _userManager;
        private readonly IActiveWorkspaceService _activeWorkspace;
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _configuration;

        public RecordingsModel(IZoomService zoom, UserManager<Models.ApplicationUser> userManager, IActiveWorkspaceService activeWorkspace,
            ApplicationDbContext db, IConfiguration configuration)
        {
            _zoom = zoom;
            _userManager = userManager;
            _activeWorkspace = activeWorkspace;
            _db = db;
            _configuration = configuration;
        }

        public IReadOnlyList<ZoomRecording> Recordings { get; private set; } = Array.Empty<ZoomRecording>();
        // Zoom UUID -> meeting already taken into the active team.
        public Dictionary<string, int> Imported { get; private set; } = new();
        public string? ErrorMessage { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!_zoom.IsEnabled) return NotFound();
            var userId = _userManager.GetUserId(User)!;

            try
            {
                Recordings = await _zoom.ListRecentAsync(userId);
            }
            catch (ZoomNotConnectedException)
            {
                return ToProfile("Önce Zoom hesabınızı bağlayın.");
            }
            catch (ZoomGrantRejectedException)
            {
                return ToProfile("Zoom bağlantınız koptu, yeniden bağlayın.");
            }
            catch (ZoomApiException)
            {
                ErrorMessage = "Zoom'a şu an ulaşılamıyor. Lütfen biraz sonra tekrar deneyin.";
            }

            var workspaceId = (await _activeWorkspace.GetActiveMembershipAsync(userId))?.WorkspaceId;
            if (workspaceId != null && Recordings.Count > 0)
            {
                var uuids = Recordings.Select(r => r.Uuid).ToList();
                Imported = (await _db.Meetings.AsNoTracking()
                        .Where(m => m.WorkspaceId == workspaceId && m.ExternalRecordingId != null && uuids.Contains(m.ExternalRecordingId))
                        .Select(m => new { m.ExternalRecordingId, m.Id }).ToListAsync())
                    .GroupBy(m => m.ExternalRecordingId!).ToDictionary(g => g.Key, g => g.Min(m => m.Id));
            }
            return Page();
        }

        public async Task<IActionResult> OnPostImportAsync(string? uuid)
        {
            if (!_zoom.IsEnabled) return NotFound();

            ZoomImportResult result;
            try
            {
                result = await _zoom.ImportAsync(_userManager.GetUserId(User)!, uuid ?? "");
            }
            catch (ZoomNotConnectedException)
            {
                return ToProfile("Önce Zoom hesabınızı bağlayın.");
            }
            catch (ZoomGrantRejectedException)
            {
                return ToProfile("Zoom bağlantınız koptu, yeniden bağlayın.");
            }
            catch (ZoomApiException)
            {
                return Back("Zoom'a şu an ulaşılamıyor. Lütfen biraz sonra tekrar deneyin.");
            }

            return result.Outcome switch
            {
                ZoomImportOutcome.Imported or ZoomImportOutcome.AlreadyImported => RedirectToPage("/Details", new { id = result.MeetingId }),
                ZoomImportOutcome.NoTeam => Back("Henüz bir ekibiniz yok. Kayıt almak için önce bir ekip oluşturun ya da bir davete katılın."),
                ZoomImportOutcome.LeftTeam => Back("Artık bu ekibin üyesi değilsiniz; kayıt alınmadı."),
                ZoomImportOutcome.NoAudio => Back("Bu kaydın ses dosyası yok, alınamaz."),
                ZoomImportOutcome.SeveralParts => Back("Bu kayıt birden çok parçadan oluşuyor (kayıt toplantıda durdurulup yeniden başlatılmış); şimdilik buradan alınamaz. Parçaları Zoom'dan indirip ayrı ayrı yükleyebilirsiniz."),
                ZoomImportOutcome.TooLarge => Back($"Kayıt çok büyük. En fazla {_configuration.GetValue("Upload:MaxMegabytes", 300)} MB'lık kayıtlar alınabilir."),
                _ => Back("Bu kayıt Zoom'da bulunamadı.")
            };
        }

        private IActionResult Back(string message)
        {
            TempData["ErrorMessage"] = message;
            return RedirectToPage();
        }

        private IActionResult ToProfile(string message)
        {
            TempData["ErrorMessage"] = message;
            return RedirectToPage("/Profile/Index");
        }
    }
}
