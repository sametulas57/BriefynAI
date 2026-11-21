using MeetingWeb.Services.PublicSite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MeetingWeb.Pages.Public;

public class ComingSoonModel(SiteCatalog catalog) : PageModel
{
    public SiteEntry Entry { get; private set; } = null!;

    public IActionResult OnGet(string section, string? slug)
    {
        var entry = catalog.Find(section, slug);
        if (entry is null) return NotFound();

        Entry = entry;
        return Page();
    }
}
