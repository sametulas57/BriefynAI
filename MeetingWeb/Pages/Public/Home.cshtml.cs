using MeetingWeb.Services.PublicSite;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MeetingWeb.Pages.Public;

public class HomeModel(SiteCatalog catalog, UploadRules uploadRules) : PageModel
{
    // Audience section: the five roles and their descriptions come from the solutions catalog.
    public IReadOnlyList<SiteEntry> Solutions { get; } = catalog.Sections.Single(section => section.Key == "solutions").Entries;

    // FAQ file types and size limit use the upload rules (Upload:MaxMegabytes).
    public string FileTypes { get; } = string.Join(", ", UploadRules.Extensions.SkipLast(1)) + " ya da " + UploadRules.Extensions[^1];
    public int MaxMegabytes => uploadRules.MaxMegabytes;

    public void OnGet() { }
}
