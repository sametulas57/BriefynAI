using System.Security.Cryptography;
using System.Text;
using MeetingWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MeetingWeb.Pages.Zoom
{
    // Zoom sends the user back here after its consent page (Zoom:RedirectUri). The state must match the value this
    // user's session got when the connection started, so nobody can slip their own Zoom account into someone else's.
    [Authorize]
    public class CallbackModel : PageModel
    {
        private readonly IZoomService _zoom;
        private readonly UserManager<Models.ApplicationUser> _userManager;

        public CallbackModel(IZoomService zoom, UserManager<Models.ApplicationUser> userManager)
        {
            _zoom = zoom;
            _userManager = userManager;
        }

        public async Task<IActionResult> OnGetAsync(string? code, string? state, string? error)
        {
            if (!_zoom.IsEnabled) return NotFound();

            var expected = HttpContext.Session.GetString(ConnectModel.StateSessionKey);
            HttpContext.Session.Remove(ConnectModel.StateSessionKey);
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(state) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(state)))
            {
                return Back("ErrorMessage", "Zoom bağlantısı tamamlanamadı. Lütfen tekrar deneyin.");
            }

            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            {
                return Back("ErrorMessage", "Zoom bağlantısı yapılmadı.");
            }

            return await _zoom.ConnectAsync(_userManager.GetUserId(User)!, code) switch
            {
                ZoomConnectOutcome.Connected => Back("StatusMessage", "Zoom hesabınız bağlandı."),
                ZoomConnectOutcome.MissingScopes => Back("ErrorMessage", "Zoom bağlantısı tamamlanamadı: gerekli izinler verilmedi."),
                _ => Back("ErrorMessage", "Zoom bağlantısı tamamlanamadı. Lütfen tekrar deneyin.")
            };
        }

        private IActionResult Back(string key, string message)
        {
            TempData[key] = message;
            return RedirectToPage("/Profile/Index");
        }
    }
}
