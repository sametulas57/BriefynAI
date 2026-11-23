namespace MeetingWeb.Services.PublicSite;

public sealed record SiteEntry(string Section, string? Group, string? Slug, string Title, string Summary, bool Available)
{
    public string Path => Slug is null ? $"/tr/{Section}" : $"/tr/{Section}/{Slug}";
}

// A navigation heading: a dropdown when it contains pages with slugs, otherwise a link to a single page.
public sealed record SiteSection(string Key, string Title, IReadOnlyList<SiteEntry> Entries)
{
    public bool IsMenu => Entries.Any(entry => entry.Slug is not null);
    public string Path => Entries[0].Path;
    public IEnumerable<IGrouping<string?, SiteEntry>> Groups => Entries.GroupBy(entry => entry.Group);
}

// Public site map (SPEC.md): generates navigation and coming-soon pages.
public sealed class SiteCatalog
{
    public IReadOnlyList<SiteEntry> Entries { get; } =
    [
        new("product", "Kaydet", "upload", "Dosya yükleme", "Toplantı kaydınızı .mp3, .wav ya da .m4a olarak yükleyin, gerisini Briefyn yapsın.", true),
        new("product", "Kaydet", "zoom", "Zoom'dan aktarma", "Zoom hesabınızı bağlayın, bulut kayıtlarınızı tek tıkla aktarın.", true),
        new("product", "Anla", "transcription", "Yazıya dökme", "Toplantıda konuşulan her şey okunabilir bir metne dönüşür.", true),
        new("product", "Anla", "summary", "Özet ve kararlar", "Toplantının özeti, kararları ve konuşulan başlıklar tek sayfada toplanır.", true),
        new("product", "Anla", "action-items", "Yapılacaklar", "Toplantıdan çıkan işler kişilere atanır ve takip edilir.", true),
        new("product", "Bul", "ask-archive", "Arşive sor", "Geçmiş toplantılarınıza soru sorun, cevabı kendi toplantılarınızdan alın.", true),
        new("product", "Bul", "series", "Toplantı serileri", "Düzenli toplantıları bir seride toplayın, sıradaki toplantının gündemi hazır olsun.", true),
        new("product", "Paylaş", "calendar", "Takvim daveti", "Tarihi olan yapılacaklar takvim daveti olarak e-postanıza gelir.", true),
        new("product", "Paylaş", "share", "E-postayla paylaşma", "Toplantının özetini ekip arkadaşlarınıza e-postayla gönderin.", true),
        new("product", "Paylaş", "workspaces", "Ekip çalışma alanı", "Ekibinizi davet edin, toplantılarınızı ortak bir alanda tutun.", true),
        new("solutions", null, "leadership", "Yönetim ve liderler", "Toplantı serilerinin kararlarını ve açık işlerini tek yerden görün.", true),
        new("solutions", null, "project-teams", "Proje ve yazılım ekipleri", "Toplantıdan çıkan işleri kaçırmadan sahiplerine atayın.", true),
        new("solutions", null, "sales", "Satış ve müşteri görüşmeleri", "Müşteriyle ne konuşulduğunu ve ne söz verildiğini kayıt altında tutun.", true),
        new("solutions", null, "hr", "İnsan kaynakları ve işe alım", "Not tutmayı bırakın, görüşmenin kendisine odaklanın.", true),
        new("solutions", null, "agencies", "Danışmanlık ve ajanslar", "Her müşteri toplantısının özetini ve yapılacaklarını hızla paylaşın.", true),
        new("integrations", null, "zoom", "Zoom", "Hesabınızı bağlayın, Zoom bulut kayıtlarınızı listeden seçip aktarın.", true),
        new("integrations", null, "calendar", "Takvim (Outlook, Google)", "Yapılacaklar takvim daveti olarak gelir, Outlook ya da Google takviminize eklenir.", true),
        new("integrations", null, "teams", "Microsoft Teams", "Teams toplantılarının kayıtlarını doğrudan almak için çalışıyoruz.", false),
        new("integrations", null, "google-meet", "Google Meet", "Google Meet kayıtlarını doğrudan almak için çalışıyoruz.", false),
        new("integrations", null, "slack", "Slack", "Toplantı özetlerini Slack kanallarınıza göndermek için çalışıyoruz.", false),
        new("integrations", null, "trello-jira", "Trello ve Jira", "Yapılacakları Trello ve Jira'ya aktarmak için çalışıyoruz.", false),
        new("resources", null, "help", "Yardım merkezi", "Briefyn'i kullanırken aklınıza takılan soruların cevapları burada olacak.", true),
        new("resources", null, "security", "Güvenlik ve gizlilik", "Toplantı verilerinizi nasıl koruduğumuzu burada anlatacağız.", true),
        new("resources", null, "changelog", "Yenilikler", "Briefyn'e eklenen yenilikleri burada duyuracağız.", true),
        new("resources", null, "contact", "İletişim", "Bize nasıl ulaşabileceğiniz burada yazacak.", true),
        new("enterprise", null, null, "Kurumsal", "Büyük ekipler ve kurumlar için Briefyn seçeneklerini hazırlıyoruz.", true),
        new("pricing", null, null, "Fiyatlandırma", "Şimdilik her şey ücretsiz.", true)
    ];

    private static readonly (string Key, string Title)[] SectionTitles =
    [
        ("product", "Ürün"),
        ("solutions", "Çözümler"),
        ("integrations", "Entegrasyonlar"),
        ("resources", "Kaynaklar"),
        ("enterprise", "Kurumsal"),
        ("pricing", "Fiyatlandırma")
    ];

    // Navigation and footer follow this order.
    public IReadOnlyList<SiteSection> Sections { get; }

    public SiteCatalog() => Sections = SectionTitles
        .Select(section => new SiteSection(section.Key, section.Title,
            Entries.Where(entry => entry.Section == section.Key).ToList()))
        .ToList();

    public SiteEntry? Find(string section, string? slug) => Entries.FirstOrDefault(entry =>
        string.Equals(entry.Section, section, StringComparison.Ordinal) &&
        string.Equals(entry.Slug, string.IsNullOrEmpty(slug) ? null : slug, StringComparison.Ordinal));
}
