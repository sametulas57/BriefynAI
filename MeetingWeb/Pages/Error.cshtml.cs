using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;

namespace MeetingWeb.Pages
{
    // SECURITY & UX: Prevent caching of the error page to ensure the generated Trace ID is always fresh and accurate.
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    // Bypass CSRF validation for the error page to ensure it renders even if token validation fails.
    [IgnoreAntiforgeryToken]
    public class ErrorModel : PageModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        private readonly ILogger<ErrorModel> _logger;

        public ErrorModel(ILogger<ErrorModel> logger)
        {
            _logger = logger;
        }

        // HTTP GET: Captures the active telemetry context when an unhandled exception propagates to the pipeline.
        public void OnGet()
        {
            // Retrieve distributed tracing ID (Activity) or fallback to the local HTTP context identifier.
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            // Note: Actual exception details are logged securely on the server side via the ILogger pipeline,
            // preventing sensitive Information Disclosure to the end-user.
        }
    }
}