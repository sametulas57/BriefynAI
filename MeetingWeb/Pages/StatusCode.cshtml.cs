using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MeetingWeb.Pages
{
    // Friendly page for empty error responses (see UseStatusCodePagesWithReExecute in Program).
    public class StatusCodeModel : PageModel
    {
        public int Code { get; private set; }

        public void OnGet(int code) => Code = code;
    }
}
