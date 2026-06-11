using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MeetingWeb.Pages
{
    // Controller class for the application's root landing page.
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ILogger<IndexModel> logger)
        {
            _logger = logger;
        }

        // HTTP GET: Handles initial requests to the root URL.
        public void OnGet()
        {
            // The landing page is primarily static and UI-driven.
            // No backend database queries or heavy computations are required for the initial render.
            // (Telemetry or A/B testing logic can be injected here in future iterations).
        }
    }
}