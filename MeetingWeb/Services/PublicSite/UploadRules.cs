namespace MeetingWeb.Services.PublicSite;

// Herkese açık sayfaların yükleme hakkında söyledikleri (ana sayfadaki sık sorulanlar) buradan gelir; sayfaya elle sayı yazılmaz.
// Uzantılar Pages/Upload.cshtml.cs'teki denetimle aynı; sınır, yüklemenin de okuduğu Upload:MaxMegabytes ayarı.
public sealed class UploadRules(IConfiguration configuration)
{
    public static IReadOnlyList<string> Extensions { get; } = [".mp3", ".wav", ".m4a"];

    public int MaxMegabytes => configuration.GetValue("Upload:MaxMegabytes", 300);
}
