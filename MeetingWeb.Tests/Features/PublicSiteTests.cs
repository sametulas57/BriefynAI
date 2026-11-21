using System.Net;
using System.Text.RegularExpressions;
using MeetingWeb.Services.PublicSite;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
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
        var entries = _app.Services.GetRequiredService<SiteCatalog>().Entries;

        Assert.Equal(28, entries.Count);
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

        var unavailable = WebUtility.HtmlDecode(await client.GetStringAsync("/tr/integrations/teams"));
        var available = WebUtility.HtmlDecode(await client.GetStringAsync("/tr/product/ask-archive"));

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
}
