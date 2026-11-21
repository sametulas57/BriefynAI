using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MeetingWeb.Pages
{
    // Eski tanıtım sayfası /tr'ye taşındı; RedirectToPage("/Index") çağrıları
    // ve eski sayfalardaki bağlantılar bu yönlendirmeyle çalışmaya devam eder (plan karar 1).
    public class IndexModel : PageModel
    {
        public IActionResult OnGet() => Redirect("/tr");
    }
}
