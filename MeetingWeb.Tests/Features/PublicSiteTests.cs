using System.Net;
using System.Text.RegularExpressions;
using MeetingWeb.Services.PublicSite;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

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
    [MemberData(nameof(CatalogPages))]
    public async Task Public_pages_use_no_long_dash(string path)
    {
        using var client = AnonymousClient();

        var response = await client.GetAsync(path);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.DoesNotContain("—", html);
    }
}
