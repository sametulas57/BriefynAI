using System.Net;
using System.Text;
using System.Text.Json;
using MeetingWeb.Services.ArchiveSearch;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingWeb.Tests.Features;

public class AskServiceTests : IAsyncLifetime
{
    [Fact]
    public async Task Python_stub_forwards_path_body_and_replaced_api_key()
    {
        string? forwardedPath = null, forwardedQuery = null, forwardedKey = null;
        Task<string>? forwardedBody = null;
        var forwardHandler = new CaptureHandler(request =>
        {
            forwardedPath = request.RequestUri!.AbsolutePath;
            forwardedQuery = request.RequestUri.Query;
            forwardedKey = request.Headers.GetValues("X-Api-Key").Single();
            forwardedBody = request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json") };
        });
        var stub = new StubPythonApiHandler { ForwardTo = new Uri("http://forwarded/"), ForwardApiKey = "replacement-key", ForwardHandler = forwardHandler };
        using var client = new HttpClient(stub);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://stub/api/answer?x=1")
        {
            Content = new StringContent("{\"soru\":\"uydurma\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Api-Key", "original-key");
        using var response = await client.SendAsync(request);
        Assert.Equal("/api/answer", forwardedPath);
        Assert.Equal("x=1", forwardedQuery!.TrimStart('?'));
        Assert.Equal("replacement-key", forwardedKey);
        Assert.Equal("{\"soru\":\"uydurma\"}", await forwardedBody!);
        var responseBody = "{\"ok\":true}";
        Assert.Equal(responseBody, await response.Content.ReadAsStringAsync());
        Assert.Equal(responseBody, stub.LastResponseBody);
        Assert.Equal("api/answer", stub.LastPath);
        Assert.Equal("original-key", stub.LastApiKey);
        Assert.Equal("{\"soru\":\"uydurma\"}", stub.LastBody);
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private readonly TestClock _clock = new();
    private readonly CapturingLoggerProvider _logs = new();
    private readonly BriefynFactory _app;
    private GoldSet G = null!;
    private const string Question = "abonelik fiyatı 149 TL";
    private const string Evidence = "Aylık abonelik fiyatı 149 TL olacak";

    public AskServiceTests()
    {
        _app = new BriefynFactory { TimeProviderOverride = _clock, LogCapture = _logs };
    }

    public async Task InitializeAsync()
    {
        await _app.InitializeAsync();
        G = _app.WithDb(GoldSet.Load);
    }

    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private async Task<AskResult> Ask(string? question = Question, string? user = null, int? workspace = null,
        ArchiveScope scope = ArchiveScope.Archive, int? target = null, DateOnly? from = null, DateOnly? to = null,
        CancellationToken ct = default)
    {
        using var lifetime = _app.Services.CreateScope();
        var service = lifetime.ServiceProvider.GetRequiredService<IAskService>();
        return await service.AskAsync(user ?? G.Askers["A"], workspace ?? G.Workspaces["A"],
            new AskRequest(question, scope, target, from, to), ct);
    }

    private async Task<ArchivePassage> Passage(string question = Question)
    {
        using var lifetime = _app.Services.CreateScope();
        var search = lifetime.ServiceProvider.GetRequiredService<IArchiveSearchService>();
        var result = await search.SearchAsync(G.Askers["A"], G.Workspaces["A"], new ArchiveQuery(question, Top: 12));
        return Assert.Single(result.Passages, p => p.Text.Contains(Evidence));
    }

    private void Respond(params (string Id, string Quote)[] evidence) => Respond(true, "Fiyat 149 TL olarak belirlendi.", evidence);

    private void Respond(bool enough, string answer, params (string Id, string Quote)[] evidence) =>
        _app.PythonApi.Body = JsonSerializer.Serialize(new
        {
            yeterli_bilgi = enough,
            kanitlar = evidence.Select(e => new { id = e.Id, alinti = e.Quote }),
            cevap = answer,
            belirsiz_not = "Sonradan değişmiş olabilir."
        });

    [Fact]
    public async Task Keeps_only_real_quote_and_its_meeting_metadata()
    {
        var p = await Passage();
        Respond((p.Id, Evidence), (p.Id, "Böyle bir cümle hiç söylenmedi"), ("T999-K1", Evidence), (p.Id, Evidence));
        var result = await Ask();
        Assert.Equal(AskStatus.Answered, result.Status);
        Assert.Equal(8, result.MeetingsSearched);
        Assert.Equal("Fiyat 149 TL olarak belirlendi.", result.Answer);
        Assert.Equal("Sonradan değişmiş olabilir.", result.Uncertain);
        Assert.Null(result.Message);
        var source = Assert.Single(result.Sources);
        Assert.Equal(p.Id, source.PassageId);
        Assert.Equal(p.MeetingId, source.MeetingId);
        Assert.Equal(p.MeetingTitle, source.MeetingTitle);
        Assert.Equal(p.MeetingDate, source.MeetingDate);
        Assert.Equal(p.Kind, source.Kind);
        Assert.Equal(Evidence, source.Quote);
    }

    [Fact]
    public async Task All_fabricated_or_too_short_quotes_hide_answer()
    {
        var p = await Passage();
        Respond((p.Id, "Tamamen uydurma cümle"), (p.Id, "149"));
        var result = await Ask();
        Assert.Equal(AskStatus.NotFound, result.Status);
        Assert.Null(result.Answer);
        Assert.Empty(result.Sources);
        Assert.Equal("Arşivde bununla ilgili bir şey bulamadım.", result.Message);
    }

    [Fact]
    public async Task Insufficient_information_hides_answer()
    {
        var p = await Passage();
        Respond(false, "Yanlış cevap", (p.Id, Evidence));
        Assert.Equal(AskStatus.NotFound, (await Ask()).Status);
    }

    [Fact]
    public async Task Turkish_case_whitespace_punctuation_and_curly_apostrophe_normalize()
    {
        ArchivePassage p;
        using (var lifetime = _app.Services.CreateScope())
        {
            var search = lifetime.ServiceProvider.GetRequiredService<IArchiveSearchService>();
            var found = await search.SearchAsync(G.Askers["A"], G.Workspaces["A"], new ArchiveQuery("fiyatı 129 TL", Top: 12));
            p = Assert.Single(found.Passages, x => x.Kind == ArchivePassageKind.Decision && x.Text.Contains("149 TL'den 129 TL'ye"));
        }
        Assert.Contains("149 TL'den 129 TL'ye", p.Text);
        Respond((p.Id, "  AYLIK  abonelİk fiyatı 149 TL’DEN 129 TL’YE indirildi. "));
        var result = await Ask("fiyatı 129 TL");
        Assert.Equal(AskStatus.Answered, result.Status);
        Assert.Single(result.Sources);
    }

    [Fact]
    public async Task Body_has_only_own_team_bounded_parts_and_expected_schema()
    {
        using var app = new BriefynFactory();
        app.ExtraSettings["Ask:MaxPassages"] = "3";
        app.ExtraSettings["Ask:ContextCharacters"] = "1000";
        await app.InitializeAsync();
        var gold = app.WithDb(GoldSet.Load);
        app.PythonApi.Body = """{"yeterli_bilgi":false,"kanitlar":[],"cevap":"","belirsiz_not":null}""";
        using (var lifetime = app.Services.CreateScope())
            await lifetime.ServiceProvider.GetRequiredService<IAskService>().AskAsync(gold.Askers["A"], gold.Workspaces["A"], new AskRequest(Question));
        Assert.Equal("api/answer", app.PythonApi.LastPath);
        using var body = JsonDocument.Parse(app.PythonApi.LastBody!);
        var root = body.RootElement;
        Assert.Equal(2, root.EnumerateObject().Count());
        Assert.Equal(Question, root.GetProperty("soru").GetString());
        var parts = root.GetProperty("parcalar").EnumerateArray().ToList();
        Assert.InRange(parts.Count, 1, 3);
        Assert.InRange(parts.Sum(p => p.GetProperty("metin").GetString()!.Length), 1, 1000);
        var own = gold.MeetingIdsOf("A").ToHashSet();
        foreach (var p in parts)
        {
            Assert.Equal(5, p.EnumerateObject().Count());
            var id = p.GetProperty("id").GetString()!;
            Assert.Contains(int.Parse(id[1..id.IndexOf('-')]), own);
            Assert.InRange(p.GetProperty("toplanti").GetString()!.Length, 1, 300);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", p.GetProperty("tarih").GetString()!);
            Assert.Contains(p.GetProperty("tur").GetString(), new[] { "konu", "karar", "bekleyen", "aksiyon", "döküm" });
        }
    }

    [Fact]
    public async Task Meeting_scope_limits_body_and_count()
    {
        Respond(false, "");
        var id = G.Meetings["A1"];
        var result = await Ask(target: id, scope: ArchiveScope.Meeting);
        Assert.Equal(1, result.MeetingsSearched);
        using var body = JsonDocument.Parse(_app.PythonApi.LastBody!);
        Assert.All(body.RootElement.GetProperty("parcalar").EnumerateArray().ToList(),
            p => Assert.StartsWith($"T{id}-", p.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Invalid_questions_stop_before_search_and_http()
    {
        var empty = await Ask("  ");
        Assert.Equal(AskStatus.EmptyQuestion, empty.Status);
        Assert.Equal("Lütfen bir soru yazın.", empty.Message);
        var longQuestion = await Ask(new string('x', 501));
        Assert.Equal(AskStatus.QuestionTooLong, longQuestion.Status);
        Assert.Equal("Soru en fazla 500 karakter olabilir.", longQuestion.Message);
        Assert.Equal(0, empty.MeetingsSearched);
        Assert.Equal(0, longQuestion.MeetingsSearched);
        Assert.Equal(0, _app.PythonApi.CallCount);
    }

    [Fact]
    public async Task Empty_scope_and_non_member_stop_before_http()
    {
        var empty = await Ask(from: new DateOnly(2040, 1, 1));
        Assert.Equal(AskStatus.EmptyArchive, empty.Status);
        Assert.Equal("Bu kapsamda bakılacak tamamlanmış toplantı yok.", empty.Message);
        var outsider = await Ask(user: _app.Seed.BobId);
        Assert.Equal(AskStatus.EmptyArchive, outsider.Status);
        Assert.Equal(0, _app.PythonApi.CallCount);
    }

    [Fact]
    public async Task No_matching_passages_return_not_found_without_http()
    {
        var result = await Ask("zebercet");
        Assert.Equal(AskStatus.NotFound, result.Status);
        Assert.Equal(8, result.MeetingsSearched);
        Assert.Equal(0, _app.PythonApi.CallCount);
    }

    [Fact]
    public async Task Rate_limit_is_per_user_and_slides_after_an_hour()
    {
        using var app = new BriefynFactory { TimeProviderOverride = _clock };
        app.ExtraSettings["Ask:QuestionsPerHour"] = "2";
        await app.InitializeAsync();
        var gold = app.WithDb(GoldSet.Load);
        app.PythonApi.Body = """{"yeterli_bilgi":false,"kanitlar":[],"cevap":"","belirsiz_not":null}""";
        async Task<AskResult> AskRate(string team)
        {
            using var lifetime = app.Services.CreateScope();
            return await lifetime.ServiceProvider.GetRequiredService<IAskService>().AskAsync(
                gold.Askers[team], gold.Workspaces[team], new AskRequest(Question));
        }
        using (var lifetime = app.Services.CreateScope())
            Assert.Equal(AskStatus.EmptyQuestion, (await lifetime.ServiceProvider.GetRequiredService<IAskService>()
                .AskAsync(gold.Askers["A"], gold.Workspaces["A"], new AskRequest("  "))).Status);
        await AskRate("A");
        await AskRate("A");
        var limited = await AskRate("A");
        Assert.Equal(AskStatus.RateLimited, limited.Status);
        Assert.Equal("Bu saat içinde çok fazla soru sordunuz. Lütfen biraz sonra tekrar deneyin.", limited.Message);
        Assert.Equal(0, limited.MeetingsSearched);
        Assert.Equal(2, app.PythonApi.CallCount);
        await AskRate("B");
        Assert.Equal(3, app.PythonApi.CallCount);
        _clock.Advance(TimeSpan.FromMinutes(61));
        await AskRate("A");
        Assert.Equal(4, app.PythonApi.CallCount);
    }

    [Fact]
    public async Task Http_failure_and_invalid_json_are_busy()
    {
        _app.PythonApi.Status = HttpStatusCode.ServiceUnavailable;
        var failed = await Ask();
        Assert.Equal(AskStatus.Busy, failed.Status);
        Assert.Equal("Yapay zekâ şu an yanıt veremiyor. Lütfen birazdan tekrar deneyin.", failed.Message);
        _app.PythonApi.Status = HttpStatusCode.OK;
        _app.PythonApi.Body = "not json";
        Assert.Equal(AskStatus.Busy, (await Ask()).Status);
    }

    [Fact]
    public async Task Logs_never_contain_question_passage_quote_or_answer()
    {
        var p = await Passage();
        Respond((p.Id, Evidence));
        await Ask();
        _app.PythonApi.Status = HttpStatusCode.ServiceUnavailable;
        await Ask("abonelik fiyatı 149 TL");
        var lines = string.Join("\n", _logs.Lines);
        foreach (var secret in new[] { Question, Evidence, p.Text, "Fiyat 149 TL olarak belirlendi." })
            Assert.DoesNotContain(secret, lines);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
