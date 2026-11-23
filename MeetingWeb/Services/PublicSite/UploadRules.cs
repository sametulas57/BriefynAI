namespace MeetingWeb.Services.PublicSite;

// Public page upload information (the home page FAQ) comes from here; no hard-coded numbers in the page.
// Extensions match validation in Pages/Upload.cshtml.cs; the limit uses the same Upload:MaxMegabytes setting as uploads.
public sealed class UploadRules(IConfiguration configuration)
{
    public static IReadOnlyList<string> Extensions { get; } = [".mp3", ".wav", ".m4a"];

    public int MaxMegabytes => configuration.GetValue("Upload:MaxMegabytes", 300);
}
