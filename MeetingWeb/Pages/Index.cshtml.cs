using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MeetingWeb.Pages
{
    // The old landing page moved to /tr; this redirect preserves RedirectToPage("/Index") calls
    // and links on older pages (plan decision 1).
    public class IndexModel : PageModel
    {
        public IActionResult OnGet() => Redirect("/tr");
    }
}
