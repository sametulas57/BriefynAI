using System.Security.Cryptography;
using MeetingWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace MeetingWeb.Pages.Zoom
{
    // Starts the Zoom connection (ADR-0011) and removes it. No page of its own: both actions are forms on the profile.
    [Authorize]
    public class ConnectModel : PageModel
    {
        // One-time random value kept in the session together with the user who started, checked when Zoom sends the
        // user back (another account signing in on the same browser meanwhile must not receive this connection).
        public const string StateSessionKey = "ZoomOAuthState";

        public static string SessionValue(string userId, string state) => userId + "\n" + state;

        private readonly IZoomService _zoom;
        private readonly UserManager<Models.ApplicationUser> _userManager;

        public ConnectModel(IZoomService zoom, UserManager<Models.ApplicationUser> userManager)
        {
            _zoom = zoom;
            _userManager = userManager;
        }

        public IActionResult OnGet() => NotFound();

        public IActionResult OnPost()
        {
            if (!_zoom.IsEnabled) return NotFound();

            var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            HttpContext.Session.SetString(StateSessionKey, SessionValue(_userManager.GetUserId(User)!, state));
            return Redirect(_zoom.AuthorizeUrl(state));
        }

        public async Task<IActionResult> OnPostDisconnectAsync()
        {
            await _zoom.DisconnectAsync(_userManager.GetUserId(User)!);
            TempData["StatusMessage"] = "Zoom bağlantısı kesildi.";
            return RedirectToPage("/Profile/Index");
        }
    }
}
