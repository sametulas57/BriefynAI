using MeetingWeb.Services.PublicSite;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MeetingWeb.Pages.Public;

public class HomeModel(SiteCatalog catalog, UploadRules uploadRules) : PageModel
{
    // "Kimler için": Çözümler menüsündeki beş rol, cümleleriyle birlikte katalogdan.
    public IReadOnlyList<SiteEntry> Solutions { get; } = catalog.Sections.Single(section => section.Key == "solutions").Entries;

    // Sık sorulanlardaki dosya türleri ve sınır: yüklemenin kendi kuralları (Upload:MaxMegabytes).
    public string FileTypes { get; } = string.Join(", ", UploadRules.Extensions.SkipLast(1)) + " ya da " + UploadRules.Extensions[^1];
    public int MaxMegabytes => uploadRules.MaxMegabytes;

    public void OnGet() { }
}
