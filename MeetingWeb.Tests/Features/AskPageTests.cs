using System.Net;
using System.Text.Json;
using MeetingWeb.Models;
using MeetingWeb.Services.ArchiveSearch;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features;

public class AskPageTests : IAsyncLifetime
{
    private readonly BriefynFactory _app = new();
    private GoldSet G = null!;
    private const string Question = "abonelik fiyatı 149 TL";
    private const string Quote = "Aylık abonelik fiyatı 149 TL olacak";

    public async Task InitializeAsync()
    {
        await _app.InitializeAsync();
        G = _app.WithDb(GoldSet.Load);
    }

    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private HttpClient Client() => _app.ClientFor(G.Askers["A"]);
    private static Task<string> Html(HttpResponseMessage response) => response.Content.ReadAsStringAsync();
    // Razor writes Turkish letters as character references.
    private static string Text(string html) => WebUtility.HtmlDecode(html);

    private async Task<string> PassageId()
    {
        using var scope = _app.Services.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<IArchiveSearchService>();
        var found = await search.SearchAsync(G.Askers["A"], G.Workspaces["A"], new ArchiveQuery(Question, Top: 12));
        return Assert.Single(found.Passages, p => p.Text.Contains(Quote)).Id;
    }

    private void Respond(string answer, params (string Id, string Text)[] quotes) =>
        _app.PythonApi.Body = JsonSerializer.Serialize(new
        {
            yeterli_bilgi = true,
            kanitlar = quotes.Select(q => new { id = q.Id, alinti = q.Text }),
            cevap = answer,
            belirsiz_not = "Sonradan değişmiş olabilir."
        });

    private async Task<string> Post(HttpClient client, params (string Key, string Value)[] fields)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Ask");
        var response = await PostFormAsync(client, "/Ask", token, fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Html(response);
    }

    [Fact]
    public async Task Member_sees_form_and_menu_while_guest_is_redirected()
    {
        using var member = Client();
        var html = await Html(await member.GetAsync("/Ask"));
        Assert.Contains("Arşive Sor", Text(html));
        Assert.Contains("name=\"Question\"", html);
        Assert.Contains("__RequestVerificationToken", html);
        // Test sign-in scheme answers a guest with 401; the real cookie scheme turns it into the login redirect.
        using var guest = _app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/Ask")).StatusCode);
    }

    [Fact]
    public async Task Get_selects_meeting_and_series_from_query()
    {
        var seriesId = _app.WithDb(db =>
        {
            var s = new MeetingSeries { WorkspaceId = G.Workspaces["A"], CreatedByUserId = G.Askers["A"], Name = "Test serisi" };
            db.MeetingSeries.Add(s); db.SaveChanges(); return s.Id;
        });
        using var client = Client();
        var meeting = await Html(await client.GetAsync($"/Ask?kapsam=toplanti&hedef={G.Meetings["A1"]}"));
        Assert.Matches($"<option(?=[^>]*value=\"{G.Meetings["A1"]}\")(?=[^>]*selected)[^>]*>", meeting);
        var series = await Html(await client.GetAsync($"/Ask?kapsam=seri&hedef={seriesId}"));
        Assert.Matches($"<option(?=[^>]*value=\"{seriesId}\")(?=[^>]*selected)[^>]*>", series);
    }

    [Fact]
    public async Task Answer_shows_verified_quote_count_source_and_escaped_model_text()
    {
        var id = await PassageId();
        Respond("<script>alert(1)</script> Fiyat 149 TL.", (id, Quote), (id, "Uydurma alıntı"));
        using var client = Client();
        var html = await Post(client, ("Question", Question), ("Scope", "Archive"));
        Assert.Contains("8 toplantıya bakıldı", Text(html));
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains(Quote, Text(html));
        Assert.DoesNotContain("Uydurma alıntı", Text(html));
        Assert.Contains($"/Details?id={G.Meetings["A1"]}", html);
    }

    [Fact]
    public async Task Fabricated_quotes_hide_answer()
    {
        Respond("Sahte cevap", (await PassageId(), "Uydurma alıntı"));
        using var client = Client();
        var html = await Post(client, ("Question", Question));
        Assert.Contains("Arşivde bununla ilgili bir şey bulamadım.", Text(html));
        Assert.DoesNotContain("Sahte cevap", html);
    }

    [Fact]
    public async Task Long_question_and_busy_service_have_messages()
    {
        using var client = Client();
        var html = await Post(client, ("Question", new string('x', 501)));
        Assert.Contains("Soru en fazla 500 karakter olabilir.", Text(html));
        Assert.Equal(0, _app.PythonApi.CallCount);
        _app.PythonApi.Status = HttpStatusCode.ServiceUnavailable;
        html = await Post(client, ("Question", Question));
        Assert.Contains("Yapay zekâ şu an yanıt veremiyor.", Text(html));
    }

    [Fact]
    public async Task Details_and_series_link_to_ask()
    {
        var seriesId = _app.WithDb(db =>
        {
            var s = new MeetingSeries { WorkspaceId = G.Workspaces["A"], CreatedByUserId = G.Askers["A"], Name = "Test serisi" };
            db.MeetingSeries.Add(s); db.SaveChanges(); return s.Id;
        });
        using var client = Client();
        Assert.Contains($"/Ask?kapsam=toplanti&amp;hedef={G.Meetings["A1"]}", await Html(await client.GetAsync($"/Details?id={G.Meetings["A1"]}")));
        Assert.Contains($"/Ask?kapsam=seri&amp;hedef={seriesId}", await Html(await client.GetAsync($"/Series/{seriesId}")));
    }

    [Fact]
    public async Task Other_teams_meeting_cannot_be_asked()
    {
        using var client = Client();
        var other = G.Meetings["B1"];
        var html = await Post(client, ("Question", Question), ("Scope", "Meeting"), ("MeetingId", other.ToString()));
        Assert.Contains("Bu kapsamda bakılacak tamamlanmış toplantı yok.", Text(html));
        Assert.Equal(0, _app.PythonApi.CallCount);
    }
}
