using System.Net;
using System.Text.RegularExpressions;
using MeetingWeb.Services.PublicSite;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features;

public class PublicSiteFixture : IAsyncLifetime
{
    public BriefynFactory Factory { get; } = new();
    public Task InitializeAsync() => Factory.InitializeAsync();
    public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
}

public class PublicSiteTests : IClassFixture<PublicSiteFixture>
{
    private readonly BriefynFactory _app;

    public PublicSiteTests(PublicSiteFixture fixture) => _app = fixture.Factory;

    public static IEnumerable<object[]> CatalogPages =>
        new SiteCatalog().Entries.Select(entry => new object[] { entry.Path });

    private HttpClient AnonymousClient() => _app.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    [Fact]
    public async Task Home_is_public_and_invites_an_upload()
    {
        using var client = AnonymousClient();

        var response = await client.GetAsync("/tr");
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1", html);
        Assert.Contains("href=\"/Upload\"", html);
        Assert.DoesNotContain("bootstrap.min.css", html);
        Assert.Contains("/css/app.css", html);
        Assert.DoesNotContain("—", decoded);
        Assert.DoesNotMatch(new Regex("(src|href)\\s*=\\s*\"(https?:)?//"), html);
        Assert.Contains("Giriş yap", decoded);
        Assert.Contains("href=\"/Auth/Login\"", html);
    }

    // Ana sayfa (Görev 9): açılış, kayan şerit, toplantının yolculuğu. BRIEF.md 5-7. ve 9. bölümler.
    private async Task<string> HomeHtmlAsync()
    {
        using var client = AnonymousClient();
        return await client.GetStringAsync("/tr");
    }

    private static string SectionHtml(string html, string marker)
    {
        var match = Regex.Match(html, "<section[^>]*" + marker + "[^>]*>(.*?)</section>", RegexOptions.Singleline);
        Assert.True(match.Success, marker + " bölümü yok");
        return match.Value;
    }

    [Fact]
    public async Task Home_has_one_h1_and_the_hero_invites_an_upload()
    {
        var html = await HomeHtmlAsync();

        Assert.Single(Regex.Matches(html, "<h1[\\s>]"));
        var hero = SectionHtml(html, "data-home-hero");
        Assert.Contains("<h1", hero);
        Assert.Contains("href=\"/Upload\"", hero);
        Assert.Contains("Kayıt yükle", WebUtility.HtmlDecode(hero));
    }

    [Fact]
    public async Task Home_loads_the_scroll_engine_and_its_own_script_from_our_server()
    {
        var html = await HomeHtmlAsync();

        Assert.Contains("/lib/scrollcraft/scrollcraft.css", html);
        Assert.Matches(new Regex("<script src=\"/lib/scrollcraft/scrollcraft\\.js[^\"]*\""), html);
        Assert.Matches(new Regex("<script src=\"/js/home\\.js[^\"]*\""), html);
        Assert.DoesNotMatch(new Regex("url\\(\\s*[\"']?(https?:)?//"), html);
        foreach (var file in new[] { "lib/scrollcraft/scrollcraft.js", "lib/scrollcraft/scrollcraft.css", "js/home.js" })
            Assert.True(new FileInfo(Path.Combine(ProjectWebRoot(), file)).Exists, file + " yok");
    }

    [Fact]
    public async Task Home_images_reserve_their_size_and_only_the_hero_image_loads_first()
    {
        var html = await HomeHtmlAsync();
        var images = Regex.Matches(MainHtml(html), "<img\\s[^>]*>").Select(match => match.Value).ToList();

        Assert.NotEmpty(images);
        foreach (var image in images)
        {
            Assert.Matches(new Regex("\\swidth=\"\\d+\""), image);
            Assert.Matches(new Regex("\\sheight=\"\\d+\""), image);
            Assert.Contains("alt=", image);
        }
        var hero = SectionHtml(html, "data-home-hero");
        Assert.Contains("fetchpriority=\"high\"", hero);
        Assert.Single(Regex.Matches(html, "fetchpriority=\"high\""));
    }

    [Theory]
    [InlineData("Yükleme", "Kaydı yükleyin ya da Zoom'dan tek tıkla aktarın.")]
    [InlineData("Yazıya dökme", "Konuşulan her şey okunabilir bir metne dönüşür.")]
    [InlineData("Konuşmacılar", "Konuşmacılar birbirinden ayrılır, her birine adını siz verirsiniz.")]
    [InlineData("Özet, kararlar, yapılacaklar", "Özet, kararlar ve yapılacaklar tek sayfada toplanır; işler kişilere atanır.")]
    [InlineData("Arşive sor", "Geçmiş toplantılarınıza soru sorun; cevap, geldiği toplantıyla birlikte gelir.")]
    [InlineData("Takvim ve paylaşma", "Tarihi olan işler takvim daveti olarak gelir; özeti ekibinize e-postayla gönderin.")]
    public async Task Journey_steps_are_written_in_the_page_itself(string step, string sentence)
    {
        var journey = WebUtility.HtmlDecode(SectionHtml(await HomeHtmlAsync(), "data-home-journey"));
        var steps = Regex.Match(journey, "<ol[^>]*>(.*?)</ol>", RegexOptions.Singleline);

        Assert.True(steps.Success, "yolculuk adımları bir liste değil");
        Assert.Equal(6, Regex.Matches(steps.Value, "<li[\\s>]").Count);
        Assert.Matches(new Regex("<h3[^>]*>\\s*" + Regex.Escape(step) + "\\s*</h3>"), steps.Value);
        // Cümlenin içinde satır bölünmesini önleyen etiket olabilir; okunan metin etiketsiz karşılaştırılır.
        Assert.Contains(sentence, Regex.Replace(steps.Value, "<[^>]+>", ""));
    }

    [Fact]
    public async Task Journey_marks_its_meeting_as_an_example()
    {
        var journey = WebUtility.HtmlDecode(SectionHtml(await HomeHtmlAsync(), "data-home-journey"));

        Assert.Contains("Örnek toplantı", journey);
        Assert.Contains("Bir toplantının yolculuğu", journey);
    }

    [Fact]
    public async Task Strip_lists_meeting_types_without_logos_and_can_be_paused()
    {
        var strip = WebUtility.HtmlDecode(SectionHtml(await HomeHtmlAsync(), "data-home-strip"));
        var button = Regex.Match(strip, "<button[^>]*data-strip-toggle[^>]*>(.*?)</button>", RegexOptions.Singleline);

        Assert.True(button.Success, "şeritte durdurma düğmesi yok");
        Assert.Contains("type=\"button\"", button.Value);
        var name = Regex.Match(button.Value, "aria-label=\"([^\"]+)\"").Groups[1].Value
                   + Regex.Replace(button.Groups[1].Value, "<[^>]+>", "");
        Assert.Contains("Şeridi durdur", name);
        Assert.DoesNotContain("<img", strip);
        foreach (var meeting in new[] { "Sprint planlama", "Müşteri görüşmesi", "Birebir görüşme" })
            Assert.Contains(meeting, strip);
    }

    // Ana sayfa (Görev 10): kimler için, güven, sık sorulanlar, kapanış. SPEC "Ana sayfa içeriği" 4-7, BRIEF.md 5. ve 9. bölümler.
    private const string NotUsedForTraining = "Toplantılarınız yapay zekâ eğitiminde kullanılmaz.";
    private const string AudioDeletedTranscriptEncrypted = "Ses analizden sonra silinir, döküm şifreli saklanır.";

    [Fact]
    public async Task Home_sections_come_in_the_planned_order()
    {
        var html = await HomeHtmlAsync();
        var markers = new[] { "data-home-hero", "data-home-strip", "data-home-journey", "data-home-audience", "data-home-trust", "data-home-faq", "data-home-closing" };

        var positions = markers.Select(marker => html.IndexOf(marker, StringComparison.Ordinal)).ToList();

        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
    }

    [Fact]
    public async Task Audience_lists_the_five_solutions_exactly_as_the_catalog_says()
    {
        var solutions = _app.Services.GetRequiredService<SiteCatalog>().Entries.Where(entry => entry.Section == "solutions").ToList();
        var audience = WebUtility.HtmlDecode(SectionHtml(await HomeHtmlAsync(), "data-home-audience"));
        var roles = Regex.Match(audience, "<ul[^>]*>(.*?)</ul>", RegexOptions.Singleline).Value;

        Assert.Equal(5, solutions.Count);
        Assert.Equal(5, Regex.Matches(roles, "<li[\\s>]").Count);
        foreach (var solution in solutions)
        {
            Assert.Matches(new Regex("<h3[^>]*>\\s*" + Regex.Escape(solution.Title) + "\\s*</h3>"), roles);
            Assert.Contains(solution.Summary, roles);
        }
        // Rollerin "yakında" sayfaları boş; satırlar çağrıdan uzaklaştırmasın diye bağlantı değil (BRIEF 5. bölüm).
        Assert.DoesNotContain("<a ", roles);
    }

    [Fact]
    public async Task Trust_section_uses_only_the_two_allowed_sentences()
    {
        var html = WebUtility.HtmlDecode(await HomeHtmlAsync());
        var trust = SectionHtml(html, "data-home-trust");
        var said = Regex.Matches(trust, "<p(?:\\s[^>]*)?>(.*?)</p>", RegexOptions.Singleline)
            .Select(match => Regex.Replace(match.Groups[1].Value, "<[^>]+>", "").Trim())
            .Where(text => text.Length > 0)
            .ToList();

        Assert.Equal(new[] { NotUsedForTraining, AudioDeletedTranscriptEncrypted }, said);
        Assert.DoesNotContain("hiç saklanmaz", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sertifika", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Faq_uses_native_details_and_answers_data_like_the_trust_section()
    {
        var faq = WebUtility.HtmlDecode(SectionHtml(await HomeHtmlAsync(), "data-home-faq"));
        var items = Regex.Matches(faq, "<details[^>]*>(.*?)</details>", RegexOptions.Singleline).Select(match => match.Value).ToList();

        Assert.InRange(items.Count, 4, 5);
        Assert.All(items, item => Assert.Contains("<summary", item));
        var data = Assert.Single(items, item => item.Contains("Verilerim ne oluyor?"));
        Assert.Contains(NotUsedForTraining, data);
        Assert.Contains(AudioDeletedTranscriptEncrypted, data);
        Assert.Contains(items, item => item.Contains("ücretli mi?") && item.Contains("Şimdilik her şey ücretsiz."));
    }

    [Fact]
    public async Task Faq_file_types_and_size_limit_come_from_the_upload_settings()
    {
        // Test sunucusu Upload:MaxMegabytes = 1 veriyor (BriefynFactory); sayfa elle yazılmış 300'ü değil ayarı göstermeli.
        var limit = _app.Services.GetRequiredService<IConfiguration>().GetValue<int>("Upload:MaxMegabytes");
        var faq = WebUtility.HtmlDecode(SectionHtml(await HomeHtmlAsync(), "data-home-faq"));
        var files = Assert.Single(Regex.Matches(faq, "<details[^>]*>(.*?)</details>", RegexOptions.Singleline).Select(match => match.Value),
            item => item.Contains("Hangi dosyaları"));

        Assert.Equal(1, limit);
        foreach (var extension in UploadRules.Extensions)
            Assert.Contains(extension, files);
        Assert.Contains("en fazla " + limit + " MB", files);
        Assert.DoesNotContain("300", files);
    }

    [Fact]
    public async Task Faq_zoom_answer_promises_a_one_click_import_not_an_automatic_one()
    {
        var faq = WebUtility.HtmlDecode(SectionHtml(await HomeHtmlAsync(), "data-home-faq"));
        var zoom = Assert.Single(Regex.Matches(faq, "<details[^>]*>(.*?)</details>", RegexOptions.Singleline).Select(match => match.Value),
            item => item.Contains("Zoom"));

        Assert.Contains("tek tıkla aktarın", zoom);
        Assert.DoesNotContain("kendiliğinden", zoom, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("otomatik", zoom, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Closing_invites_an_upload()
    {
        var closing = WebUtility.HtmlDecode(SectionHtml(await HomeHtmlAsync(), "data-home-closing"));

        Assert.Contains("href=\"/Upload\"", closing);
        Assert.Contains("Kayıt yükle", closing);
        Assert.Contains("<h2", closing);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Index")]
    public async Task Root_and_old_index_redirect_to_tr(string path)
    {
        using var client = AnonymousClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/tr", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Signed_in_user_stays_on_tr_and_sees_meetings_link()
    {
        using var client = _app.ClientFor(_app.Seed.AliceId);

        var response = await client.GetAsync("/tr");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Toplantılarım", WebUtility.HtmlDecode(html));
        Assert.Contains("href=\"/History\"", html);
    }

    [Fact]
    public async Task Old_pages_do_not_load_the_new_stylesheet()
    {
        using var client = _app.ClientFor(_app.Seed.AliceId);

        var response = await client.GetAsync("/History");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("/css/app.css", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Anonymous_upload_goes_to_login_and_comes_back_after_sign_in()
    {
        using var app = await PasswordResetTests.PasswordAppAsync();
        using var client = PasswordResetTests.CookieClient(app);

        var upload = await client.GetAsync("/Upload");

        Assert.Equal(HttpStatusCode.Redirect, upload.StatusCode);
        Assert.EndsWith("/Auth/Login?ReturnUrl=%2FUpload", upload.Headers.Location!.OriginalString);

        const string loginUrl = "/Auth/Login?ReturnUrl=%2FUpload";
        var token = await GetAntiforgeryTokenAsync(client, loginUrl);
        var login = await PostFormAsync(client, loginUrl, token,
            ("Input.Email", SeedData.AliceEmail), ("Input.Password", PasswordResetTests.OldPassword));

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/Upload", login.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Signed_in_redirects_that_used_index_now_reach_tr()
    {
        using var client = _app.ClientFor(_app.Seed.AliceId);

        var login = await client.GetAsync("/Auth/Login");

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var home = await client.GetAsync(login.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
        Assert.Equal("/tr", home.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Login_passes_return_url_to_register()
    {
        using var client = AnonymousClient();

        var withReturnUrl = await client.GetStringAsync("/Auth/Login?ReturnUrl=%2FUpload");
        var withoutReturnUrl = await client.GetStringAsync("/Auth/Login");

        Assert.Contains("href=\"/Auth/Register?returnUrl=%2FUpload\"", withReturnUrl);
        Assert.Contains("href=\"/Auth/Register\"", withoutReturnUrl);
    }

    [Fact]
    public async Task Register_returns_to_local_return_url()
    {
        var response = await RegisterAsync("/Auth/Register?returnUrl=%2FUpload");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Upload", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Register_without_return_url_goes_home()
    {
        var response = await RegisterAsync("/Auth/Register");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(response.Headers.Location!.OriginalString, new[] { "/", "/Index" });
    }

    [Theory]
    [InlineData("https://saldirgan.example")]
    [InlineData("//saldirgan.example")]
    [InlineData("/\\saldirgan.example")]
    public async Task Register_never_redirects_off_site(string returnUrl)
    {
        var response = await RegisterAsync("/Auth/Register?returnUrl=" + Uri.EscapeDataString(returnUrl));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.Contains(location, new[] { "/", "/Index" });
        Assert.False(location.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        Assert.False(location.StartsWith("//", StringComparison.Ordinal));
        Assert.False(location.StartsWith("/\\", StringComparison.Ordinal));
    }

    private async Task<HttpResponseMessage> RegisterAsync(string url)
    {
        using var visitor = AnonymousClient();
        var token = await GetAntiforgeryTokenAsync(visitor, url);
        return await PostFormAsync(visitor, url, token,
            ("Input.FullName", "Yeni Kullanıcı"), ("Input.Email", $"yeni-{Guid.NewGuid():N}@kisi.test"),
            ("Input.Password", "Guclu-Parola-123"), ("Input.ConfirmPassword", "Guclu-Parola-123"),
            ("Input.IsKvkkAccepted", "true"));
    }

    [Fact]
    public void Catalog_matches_the_site_map()
    {
        var catalog = _app.Services.GetRequiredService<SiteCatalog>();
        var entries = catalog.Entries;

        Assert.Equal(28, entries.Count);
        Assert.Equal(entries, catalog.Sections.SelectMany(section => section.Entries));
        Assert.Equal(new Dictionary<string, int>
        {
            ["product"] = 11,
            ["solutions"] = 5,
            ["integrations"] = 6,
            ["resources"] = 4,
            ["enterprise"] = 1,
            ["pricing"] = 1
        }, entries.GroupBy(entry => entry.Section).ToDictionary(group => group.Key, group => group.Count()));
        Assert.Equal(new[] { "Kaydet", "Anla", "Bul", "Paylaş" },
            entries.Where(entry => entry.Section == "product").Select(entry => entry.Group).Distinct());
        Assert.Equal(entries.Count, entries.Select(entry => entry.Path).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(new[] { "google-meet", "slack", "teams", "trello-jira" },
            entries.Where(entry => !entry.Available).Select(entry => entry.Slug).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(CatalogPages))]
    public async Task Every_catalog_page_is_a_coming_soon_page(string path)
    {
        var entry = _app.Services.GetRequiredService<SiteCatalog>().Entries.Single(entry => entry.Path == path);
        using var client = AnonymousClient();

        var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(entry.Title, decoded);
        Assert.Contains(entry.Summary, decoded);
        Assert.Contains("Bu sayfa hazırlanıyor", decoded);
        Assert.Contains("href=\"/Upload\"", html);
        Assert.Contains("href=\"/tr\"", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\"", html);
    }

    [Theory]
    [InlineData("/tr/product/olmayan")]
    [InlineData("/tr/olmayan")]
    [InlineData("/tr/product")]
    [InlineData("/tr/enterprise/fazla")]
    [InlineData("/tr/pricing/fazla")]
    public async Task Unknown_public_addresses_are_not_found(string path)
    {
        using var client = AnonymousClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/tr/product/ask-archive")]
    [InlineData("/tr/pricing")]
    public async Task Public_pages_load_nothing_from_other_servers_and_no_bootstrap(string path)
    {
        using var client = AnonymousClient();

        var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("bootstrap.min.css", html);
        Assert.DoesNotContain("bootstrap-icons", html);
        Assert.Contains("/css/app.css", html);
        Assert.DoesNotMatch(new Regex("(src|href)\\s*=\\s*\"(https?:)?//"), html);
    }

    [Theory]
    [InlineData("/tr")]
    [InlineData("/tr/product/ask-archive")]
    [InlineData("/tr/integrations/teams")]
    public async Task Public_pages_show_the_logo_and_tab_icon(string path)
    {
        using var client = AnonymousClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("<link rel=\"icon\" type=\"image/svg+xml\" href=\"/img/brand/favicon.svg\"", html);
        Assert.Matches(new Regex("<a href=\"/tr\"[^>]*>\\s*<img src=\"/img/brand/logo-a\\.svg\"[^>]*alt=\"Briefyn\""), html);
    }

    // Test sunucusu geçici bir wwwroot kullanıyor; dosyalar projenin kendi wwwroot'unda aranır.
    [Theory]
    [InlineData("img/brand/favicon.svg")]
    [InlineData("img/brand/logo-a.svg")]
    [InlineData("img/brand/logo-a-acik.svg")]
    [InlineData("fonts/bricolage-grotesque/BricolageGrotesque.woff2")]
    [InlineData("fonts/bricolage-grotesque/OFL.txt")]
    [InlineData("fonts/atkinson-hyperlegible-next/AtkinsonHyperlegibleNext.woff2")]
    [InlineData("fonts/atkinson-hyperlegible-next/OFL.txt")]
    public void Brand_files_live_in_our_own_web_root(string relativePath)
    {
        var file = new FileInfo(Path.Combine(ProjectWebRoot(), relativePath));

        Assert.True(file.Exists, relativePath + " yok");
        Assert.True(file.Length > 0, relativePath + " boş");
        if (file.Extension == ".svg")
            Assert.DoesNotMatch(new Regex("(href|src)\\s*=\\s*\"(https?:)?//"), File.ReadAllText(file.FullName));
    }

    private static string ProjectWebRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "MeetingWeb", "wwwroot");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("MeetingWeb/wwwroot bulunamadı");
    }

    [Fact]
    public async Task Coming_soon_page_marks_only_unavailable_features_as_soon()
    {
        using var client = AnonymousClient();

        // Menüde "Yakında" işaretleri her sayfada var; burada yalnız sayfanın kendi içeriğine bakılır.
        var unavailable = MainHtml(WebUtility.HtmlDecode(await client.GetStringAsync("/tr/integrations/teams")));
        var available = MainHtml(WebUtility.HtmlDecode(await client.GetStringAsync("/tr/product/ask-archive")));

        Assert.Contains(">Yakında<", unavailable);
        Assert.DoesNotContain(">Yakında<", available);
    }

    [Theory]
    [MemberData(nameof(CatalogPages))]
    public async Task Public_pages_use_no_long_dash(string path)
    {
        using var client = AnonymousClient();

        var response = await client.GetAsync(path);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.DoesNotContain("—", html);
    }

    private static string MainHtml(string html) =>
        Regex.Match(html, "<main[^>]*>(.*?)</main>", RegexOptions.Singleline).Groups[1].Value;

    private static string MainNavHtml(string html)
    {
        var match = Regex.Match(html, "<nav[^>]*aria-label=\"Ana menü\"[^>]*>(.*?)</nav>", RegexOptions.Singleline);
        Assert.True(match.Success, "ana menü (nav) yok");
        return match.Groups[1].Value;
    }

    private static string FooterHtml(string html)
    {
        var match = Regex.Match(html, "<footer[^>]*>(.*?)</footer>", RegexOptions.Singleline);
        Assert.True(match.Success, "alt bilgi (footer) yok");
        return match.Groups[1].Value;
    }

    private static IEnumerable<string> TrHrefs(string html) =>
        Regex.Matches(html, "href=\"(/tr/[^\"]*)\"").Select(match => match.Groups[1].Value);

    [Fact]
    public async Task Top_menu_has_six_sections_in_order()
    {
        using var client = AnonymousClient();

        var nav = MainNavHtml(WebUtility.HtmlDecode(await client.GetStringAsync("/tr")));
        var titles = Regex.Matches(nav, "data-nav-section[^>]*>\\s*([^<]+?)\\s*<").Select(match => match.Groups[1].Value);

        Assert.Equal(new[] { "Ürün", "Çözümler", "Entegrasyonlar", "Kaynaklar", "Kurumsal", "Fiyatlandırma" }, titles);
        Assert.Contains("href=\"/tr/enterprise\"", nav);
        Assert.Contains("href=\"/tr/pricing\"", nav);
    }

    [Fact]
    public async Task Menu_links_match_the_catalog_and_all_open()
    {
        var catalogPaths = _app.Services.GetRequiredService<SiteCatalog>().Entries.Select(entry => entry.Path);
        using var client = AnonymousClient();

        var nav = MainNavHtml(WebUtility.HtmlDecode(await client.GetStringAsync("/tr")));
        var menuPaths = TrHrefs(nav).Distinct(StringComparer.Ordinal).ToList();

        Assert.Equal(28, menuPaths.Count);
        Assert.Equal(catalogPaths.Order(StringComparer.Ordinal), menuPaths.Order(StringComparer.Ordinal));
        foreach (var path in menuPaths)
        {
            var response = await client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, path + " " + (int)response.StatusCode);
        }
    }

    [Fact]
    public async Task Product_menu_has_four_groups()
    {
        using var client = AnonymousClient();

        var nav = MainNavHtml(WebUtility.HtmlDecode(await client.GetStringAsync("/tr")));
        var product = nav.Split("data-nav-section")[1];
        var groups = Regex.Matches(product, "data-nav-group[^>]*>\\s*([^<]+?)\\s*<").Select(match => match.Groups[1].Value);

        Assert.Equal(new[] { "Kaydet", "Anla", "Bul", "Paylaş" }, groups);
    }

    [Fact]
    public async Task Unavailable_features_are_marked_soon_in_menu()
    {
        var entries = _app.Services.GetRequiredService<SiteCatalog>().Entries;
        using var client = AnonymousClient();

        var nav = MainNavHtml(WebUtility.HtmlDecode(await client.GetStringAsync("/tr")));
        var links = Regex.Matches(nav, "<a\\s[^>]*href=\"(/tr/[^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline)
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);

        foreach (var slug in new[] { "teams", "google-meet", "slack", "trello-jira" })
            Assert.Contains("Yakında", links["/tr/integrations/" + slug]);
        Assert.DoesNotContain("Yakında", links["/tr/product/ask-archive"]);
        foreach (var entry in entries)
            Assert.True(links[entry.Path].Contains("Yakında") == !entry.Available, entry.Path);
    }

    [Fact]
    public async Task Footer_links_privacy_and_contact()
    {
        var catalogPaths = _app.Services.GetRequiredService<SiteCatalog>().Entries.Select(entry => entry.Path);
        using var client = AnonymousClient();

        var footer = FooterHtml(WebUtility.HtmlDecode(await client.GetStringAsync("/tr/product/ask-archive")));

        Assert.Contains("href=\"/Privacy\"", footer);
        Assert.Contains("href=\"/tr/resources/contact\"", footer);
        Assert.Contains(">Gizlilik<", footer);
        Assert.Contains(">İletişim<", footer);
        Assert.Equal(catalogPaths.Order(StringComparer.Ordinal), TrHrefs(footer).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Menu_button_has_a_name_and_starts_closed()
    {
        using var client = AnonymousClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/tr"));
        var button = Regex.Match(html, "<button[^>]*data-nav-toggle[^>]*>(.*?)</button>", RegexOptions.Singleline);

        Assert.True(button.Success, "menü düğmesi yok");
        Assert.Contains("aria-expanded=\"false\"", button.Value);
        Assert.Contains("Menü", Regex.Replace(button.Groups[1].Value, "<[^>]+>", ""));
        var controls = Regex.Match(button.Value, "aria-controls=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(controls);
        Assert.Contains("id=\"" + controls + "\"", html);
    }
}
