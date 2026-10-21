using System.Net;
using System.Text;
using System.Web;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features;

// PRD 2026-10-21-zoom-asama-1, 1a acceptance criteria 1–11. Every call to Zoom goes to StubZoomHandler.
public class ZoomTests
{
    private const string Uuid = "Zx/9Q+abc==";
    private const string RecordingPath = "/v2/meetings/Zx%2F9Q%2Babc%3D%3D/recordings";
    private const string DownloadUrl = "https://zoom.us/rec/download/abc";

    private static async Task<BriefynFactory> AppAsync(bool enabled = true, CapturingLoggerProvider? logs = null)
    {
        var app = new BriefynFactory { ZoomEnabled = enabled, LogCapture = logs };
        await app.InitializeAsync();
        return app;
    }

    private static void Connect(BriefynFactory app, string userId, TimeSpan? expiresIn = null) => app.WithDb(db =>
    {
        db.ZoomConnections.Add(new ZoomConnection
        {
            UserId = userId,
            ZoomUserId = "zoom-" + userId,
            ZoomEmail = "zoom@" + userId,
            AccessToken = StubZoomHandler.AccessToken,
            RefreshToken = StubZoomHandler.RefreshToken,
            AccessTokenExpiresAtUtc = DateTime.UtcNow + (expiresIn ?? TimeSpan.FromMinutes(50))
        });
        return db.SaveChanges();
    });

    private static ZoomConnection? ConnectionOf(BriefynFactory app, string userId) =>
        app.WithDb(db => db.ZoomConnections.AsNoTracking().SingleOrDefault(z => z.UserId == userId));

    private static async Task<string> FollowAsync(HttpClient client, HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        return WebUtility.HtmlDecode(await client.GetStringAsync(res.Headers.Location!.OriginalString));
    }

    private static async Task<(HttpResponseMessage Start, string State)> StartAsync(HttpClient client)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Profile");
        var start = await PostFormAsync(client, "/Zoom/Connect", token);
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        return (start, HttpUtility.ParseQueryString(start.Headers.Location!.Query)["state"] ?? "");
    }

    private static string Recording(string uuid, string topic, params string[] fileTypes) =>
        $$"""
        {"uuid":"{{uuid}}","id":123,"topic":"{{topic}}","start_time":"2026-10-21T18:45:00Z","duration":42,
         "recording_files":[{{string.Join(",", fileTypes.Select(t =>
            $$"""{"id":"f-{{t}}","file_type":"{{t}}","file_size":{{(t == "M4A" ? 1000 : 50000000)}},"status":"completed","download_url":"{{DownloadUrl}}"}"""))}}]}
        """;

    private static void RecordingFiles(BriefynFactory app, string json, byte[]? audio = null)
    {
        app.Zoom.Json("GET", RecordingPath, json);
        app.Zoom.On("GET", "/rec/download/abc", _ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.Found);
            res.Headers.Location = new Uri("https://cdn.example.test/signed/file.m4a?sig=1");
            return res;
        });
        app.Zoom.On("GET", "/signed/file.m4a", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(audio ?? Encoding.UTF8.GetBytes("fake-m4a-bytes"))
        });
    }

    private static async Task<HttpResponseMessage> ImportAsync(HttpClient client, string uuid = Uuid)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Profile");
        return await PostFormAsync(client, "/Zoom/Recordings?handler=Import", token, ("Uuid", uuid));
    }

    // 1
    [Fact]
    public async Task Without_settings_zoom_is_off_and_endpoints_are_404()
    {
        using var app = await AppAsync(enabled: false);
        var alice = app.ClientFor(app.Seed.AliceId);

        var profile = WebUtility.HtmlDecode(await alice.GetStringAsync("/Profile"));
        Assert.Contains("Zoom bağlantısı bu sunucuda açık değil", profile);
        Assert.DoesNotContain("Zoom'u bağla", profile);

        var token = await GetAntiforgeryTokenAsync(alice, "/Profile");
        Assert.Equal(HttpStatusCode.NotFound, (await PostFormAsync(alice, "/Zoom/Connect", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync("/Zoom/Callback?code=x&state=y")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync("/Zoom/Recordings")).StatusCode);
        Assert.Empty(app.Zoom.Requests);
    }

    // 2
    [Fact]
    public async Task Connect_redirects_to_zoom_with_state_and_callback_checks_it()
    {
        using var app = await AppAsync();
        var alice = app.ClientFor(app.Seed.AliceId);
        Assert.Contains("Zoom'u bağla", WebUtility.HtmlDecode(await alice.GetStringAsync("/Profile")));

        var (start, state) = await StartAsync(alice);
        var location = start.Headers.Location!;
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("https://zoom.us/oauth/authorize", location.GetLeftPart(UriPartial.Path));
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("test-zoom-client", query["client_id"]);
        Assert.Equal("http://127.0.0.1:8080/Zoom/Callback", query["redirect_uri"]);
        Assert.True(state.Length >= 40);

        // Wrong or missing state: nothing is exchanged or saved.
        var wrong = await alice.GetAsync("/Zoom/Callback?code=c&state=" + Uri.EscapeDataString(state + "x"));
        Assert.Contains("Zoom bağlantısı tamamlanamadı", await FollowAsync(alice, wrong));
        (_, state) = await StartAsync(alice);
        await alice.GetAsync("/Zoom/Callback?code=c");
        // The state is single use: after a failed attempt the same value no longer works.
        await alice.GetAsync("/Zoom/Callback?code=c&state=" + Uri.EscapeDataString(state));
        Assert.Empty(app.Zoom.To("POST", "/oauth/token"));
        Assert.Null(ConnectionOf(app, app.Seed.AliceId));

        // Another user's state does not work for this user either (it lives in the user's own session).
        var (_, aliceState) = await StartAsync(alice);
        var bob = app.ClientFor(app.Seed.BobId);
        await bob.GetAsync("/Zoom/Callback?code=c&state=" + Uri.EscapeDataString(aliceState));
        Assert.Null(ConnectionOf(app, app.Seed.BobId));

        // Zoom granted fewer permissions than the app needs: not saved.
        app.Zoom.Json("POST", "/oauth/token", StubZoomHandler.TokenJson("a", "r", "user:read:user"));
        var res = await alice.GetAsync("/Zoom/Callback?code=c&state=" + Uri.EscapeDataString(aliceState));
        Assert.Contains("gerekli izinler verilmedi", await FollowAsync(alice, res));
        Assert.Null(ConnectionOf(app, app.Seed.AliceId));

        // The user said no on Zoom's page.
        var (_, cancelState) = await StartAsync(alice);
        var cancel = await alice.GetAsync("/Zoom/Callback?error=access_denied&state=" + Uri.EscapeDataString(cancelState));
        Assert.Contains("Zoom bağlantısı yapılmadı", await FollowAsync(alice, cancel));
    }

    // Review finding: the state belongs to the user who started, not only to the browser.
    [Fact]
    public async Task State_started_by_one_user_does_not_connect_another_user_in_the_same_browser()
    {
        using var app = await AppAsync();
        var browser = app.ClientFor(app.Seed.AliceId);
        var (_, state) = await StartAsync(browser);

        browser.DefaultRequestHeaders.Remove(TestAuthHandler.UserHeader);
        browser.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, app.Seed.BobId);
        var res = await browser.GetAsync("/Zoom/Callback?code=c&state=" + Uri.EscapeDataString(state));

        Assert.Contains("Zoom bağlantısı tamamlanamadı", await FollowAsync(browser, res));
        Assert.Null(ConnectionOf(app, app.Seed.BobId));
        Assert.Null(ConnectionOf(app, app.Seed.AliceId));
        Assert.Empty(app.Zoom.To("POST", "/oauth/token"));
    }

    // 3
    [Fact]
    public async Task Successful_callback_saves_encrypted_tokens_and_never_logs_them()
    {
        var logs = new CapturingLoggerProvider();
        using var app = await AppAsync(logs: logs);
        var alice = app.ClientFor(app.Seed.AliceId);

        var (_, state) = await StartAsync(alice);
        var res = await alice.GetAsync("/Zoom/Callback?code=test-code&state=" + Uri.EscapeDataString(state));
        var profile = await FollowAsync(alice, res);
        Assert.Contains("Zoom hesabınız bağlandı", profile);
        Assert.Contains("ulas@zoom.test", profile);
        Assert.Contains("Bağlantıyı kes", profile);

        var tokenCall = Assert.Single(app.Zoom.To("POST", "/oauth/token"));
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("test-zoom-client:test-zoom-secret")), tokenCall.Authorization);
        Assert.Contains("grant_type=authorization_code", tokenCall.Body);
        Assert.Contains("code=test-code", tokenCall.Body);
        Assert.Equal("Bearer " + StubZoomHandler.AccessToken, Assert.Single(app.Zoom.To("GET", "/v2/users/me")).Authorization);

        var saved = ConnectionOf(app, app.Seed.AliceId)!;
        Assert.Equal("zoom-user-1", saved.ZoomUserId);
        Assert.Equal(StubZoomHandler.RefreshToken, saved.RefreshToken);
        Assert.InRange(saved.AccessTokenExpiresAtUtc, DateTime.UtcNow.AddMinutes(55), DateTime.UtcNow.AddMinutes(61));

        var raw = app.WithDb(db =>
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT \"AccessToken\", \"RefreshToken\" FROM \"ZoomConnections\"";
            using var reader = cmd.ExecuteReader();
            reader.Read();
            return new[] { reader.GetString(0), reader.GetString(1) };
        });
        Assert.All(raw, value => Assert.StartsWith("v3:", value));
        var everything = app.WithDb(db =>
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT * FROM \"ZoomConnections\"";
            using var reader = cmd.ExecuteReader();
            var values = new List<string>();
            while (reader.Read()) for (var i = 0; i < reader.FieldCount; i++) values.Add(Convert.ToString(reader.GetValue(i)) ?? "");
            return string.Join("|", values);
        });
        Assert.DoesNotContain(StubZoomHandler.AccessToken, everything);
        Assert.DoesNotContain(StubZoomHandler.RefreshToken, everything);

        var logText = string.Join("\n", logs.Lines);
        Assert.DoesNotContain(StubZoomHandler.AccessToken, logText);
        Assert.DoesNotContain(StubZoomHandler.RefreshToken, logText);
        Assert.DoesNotContain("test-zoom-secret", logText);
    }

    private static void EmptyList(BriefynFactory app) =>
        app.Zoom.Json("GET", "/v2/users/me/recordings", """{"from":"x","to":"y","meetings":[]}""");

    // 4
    [Fact]
    public async Task Token_close_to_expiry_is_refreshed_and_new_refresh_token_is_saved()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId, TimeSpan.FromMinutes(2));
        EmptyList(app);
        app.Zoom.Json("POST", "/oauth/token", StubZoomHandler.TokenJson(StubZoomHandler.NewAccessToken, StubZoomHandler.NewRefreshToken));

        var res = await app.ClientFor(app.Seed.AliceId).GetAsync("/Zoom/Recordings");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var refresh = Assert.Single(app.Zoom.To("POST", "/oauth/token"));
        Assert.Contains("grant_type=refresh_token", refresh.Body);
        Assert.Contains("refresh_token=" + StubZoomHandler.RefreshToken, refresh.Body);
        Assert.Equal("Bearer " + StubZoomHandler.NewAccessToken, Assert.Single(app.Zoom.To("GET", "/v2/users/me/recordings")).Authorization);
        var saved = ConnectionOf(app, app.Seed.AliceId)!;
        Assert.Equal(StubZoomHandler.NewRefreshToken, saved.RefreshToken);
        Assert.Equal(StubZoomHandler.NewAccessToken, saved.AccessToken);
    }

    [Fact]
    public async Task Fresh_token_is_not_refreshed()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId, TimeSpan.FromMinutes(30));
        EmptyList(app);

        await app.ClientFor(app.Seed.AliceId).GetAsync("/Zoom/Recordings");

        Assert.Empty(app.Zoom.To("POST", "/oauth/token"));
        Assert.Equal("Bearer " + StubZoomHandler.AccessToken, Assert.Single(app.Zoom.To("GET", "/v2/users/me/recordings")).Authorization);
    }

    [Fact]
    public async Task Rejected_refresh_removes_connection_and_asks_to_reconnect()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId, TimeSpan.FromMinutes(-5));
        app.Zoom.Json("POST", "/oauth/token", """{"reason":"Invalid Token!","error":"invalid_grant"}""", HttpStatusCode.BadRequest);
        var alice = app.ClientFor(app.Seed.AliceId);

        var res = await alice.GetAsync("/Zoom/Recordings");

        Assert.Equal("/Profile", res.Headers.Location!.OriginalString);
        Assert.Contains("Zoom bağlantınız koptu, yeniden bağlayın", await FollowAsync(alice, res));
        Assert.Null(ConnectionOf(app, app.Seed.AliceId));
        Assert.Empty(app.Zoom.To("GET", "/v2/users/me/recordings"));
    }

    // Two refreshes at once: the one that saves second re-reads the row and uses the tokens the first one saved.
    [Fact]
    public async Task Refresh_that_loses_the_race_uses_the_saved_tokens()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId, TimeSpan.FromMinutes(1));
        EmptyList(app);
        app.Zoom.On("POST", "/oauth/token", _ =>
        {
            app.WithDb(db =>
            {
                var row = db.ZoomConnections.Single(z => z.UserId == app.Seed.AliceId);
                row.AccessToken = "other-access";
                row.RefreshToken = "other-refresh";
                row.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1);
                return db.SaveChanges();
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(StubZoomHandler.TokenJson("mine-access", "mine-refresh")) };
        });

        var res = await app.ClientFor(app.Seed.AliceId).GetAsync("/Zoom/Recordings");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("other-refresh", ConnectionOf(app, app.Seed.AliceId)!.RefreshToken);
        Assert.Equal("Bearer other-access", Assert.Single(app.Zoom.To("GET", "/v2/users/me/recordings")).Authorization);
    }

    // If another request already used the refresh token, Zoom rejects ours; the connection is not dropped for that.
    [Fact]
    public async Task Rejected_refresh_after_another_refresh_keeps_connection()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId, TimeSpan.FromMinutes(1));
        EmptyList(app);
        app.Zoom.On("POST", "/oauth/token", _ =>
        {
            app.WithDb(db =>
            {
                var row = db.ZoomConnections.Single(z => z.UserId == app.Seed.AliceId);
                row.AccessToken = "other-access";
                row.RefreshToken = "other-refresh";
                row.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1);
                return db.SaveChanges();
            });
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"invalid_grant"}""") };
        });

        var res = await app.ClientFor(app.Seed.AliceId).GetAsync("/Zoom/Recordings");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.NotNull(ConnectionOf(app, app.Seed.AliceId));
        Assert.Equal("Bearer other-access", Assert.Single(app.Zoom.To("GET", "/v2/users/me/recordings")).Authorization);
    }

    // 5
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Disconnect_revokes_at_zoom_and_always_deletes_the_row(HttpStatusCode zoomAnswer)
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);
        app.Zoom.Json("POST", "/oauth/revoke", "{}", zoomAnswer);
        var alice = app.ClientFor(app.Seed.AliceId);

        var token = await GetAntiforgeryTokenAsync(alice, "/Profile");
        var res = await PostFormAsync(alice, "/Zoom/Connect?handler=Disconnect", token);

        Assert.Contains("Zoom bağlantısı kesildi", await FollowAsync(alice, res));
        var revoke = Assert.Single(app.Zoom.To("POST", "/oauth/revoke"));
        Assert.Contains("token=" + StubZoomHandler.AccessToken, revoke.Body);
        Assert.StartsWith("Basic ", revoke.Authorization);
        Assert.Null(ConnectionOf(app, app.Seed.AliceId));
    }

    // Review finding: an expired access token cannot be revoked, so it is refreshed first.
    [Fact]
    public async Task Disconnect_with_expired_token_refreshes_before_revoking()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId, TimeSpan.FromMinutes(-30));
        app.Zoom.Json("POST", "/oauth/token", StubZoomHandler.TokenJson(StubZoomHandler.NewAccessToken, StubZoomHandler.NewRefreshToken));
        var alice = app.ClientFor(app.Seed.AliceId);

        var token = await GetAntiforgeryTokenAsync(alice, "/Profile");
        await PostFormAsync(alice, "/Zoom/Connect?handler=Disconnect", token);

        Assert.Contains("refresh_token=" + StubZoomHandler.RefreshToken, Assert.Single(app.Zoom.To("POST", "/oauth/token")).Body);
        Assert.Contains("token=" + StubZoomHandler.NewAccessToken, Assert.Single(app.Zoom.To("POST", "/oauth/revoke")).Body);
        Assert.Null(ConnectionOf(app, app.Seed.AliceId));
    }

    // Review finding: Zoom pages long lists with next_page_token.
    [Fact]
    public async Task List_reads_every_page()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);
        app.Zoom.On("GET", "/v2/users/me/recordings", req =>
        {
            var second = HttpUtility.ParseQueryString(req.RequestUri!.Query)["next_page_token"] == "page-2";
            var body = second
                ? "{\"next_page_token\":\"\",\"meetings\":[" + Recording("uuid-2", "İkinci sayfa", "M4A") + "]}"
                : "{\"next_page_token\":\"page-2\",\"meetings\":[" + Recording("uuid-1", "Birinci sayfa", "M4A") + "]}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        });

        var page = WebUtility.HtmlDecode(await app.ClientFor(app.Seed.AliceId).GetStringAsync("/Zoom/Recordings"));

        Assert.Contains("Birinci sayfa", page);
        Assert.Contains("İkinci sayfa", page);
        Assert.Equal(2, app.Zoom.To("GET", "/v2/users/me/recordings").Count());
    }

    // 6
    [Fact]
    public async Task List_asks_for_the_last_30_days_and_marks_missing_audio_and_imported()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);
        var imported = app.WithDb(db =>
        {
            var m = new Meeting { WorkspaceId = app.Seed.WorkspaceA, Title = "x", Source = MeetingSource.Zoom, ExternalRecordingId = "uuid-imported" };
            // The same recording taken into another team does not count for this team.
            db.Meetings.Add(new Meeting { WorkspaceId = app.Seed.WorkspaceB, Title = "y", Source = MeetingSource.Zoom, ExternalRecordingId = "uuid-new" });
            db.Meetings.Add(m);
            db.SaveChanges();
            return m.Id;
        });
        app.Zoom.Json("GET", "/v2/users/me/recordings", "{\"meetings\":[" +
            Recording("uuid-new", "Yeni toplantı", "M4A", "MP4") + "," +
            Recording("uuid-video", "Yalnız video", "MP4") + "," +
            Recording("uuid-imported", "Alınmış toplantı", "M4A") + "]}");

        var page = WebUtility.HtmlDecode(await app.ClientFor(app.Seed.AliceId).GetStringAsync("/Zoom/Recordings"));

        var query = HttpUtility.ParseQueryString(Assert.Single(app.Zoom.To("GET", "/v2/users/me/recordings")).Uri.Query);
        Assert.Equal(DateTime.UtcNow.Date.AddDays(-30).ToString("yyyy-MM-dd"), query["from"]);
        Assert.Equal(DateTime.UtcNow.Date.ToString("yyyy-MM-dd"), query["to"]);
        Assert.Contains("Yeni toplantı", page);
        Assert.Contains("ses dosyası yok", page);
        Assert.Contains("Alındı", page);
        Assert.Contains($"/Details?id={imported}", page);
        // Times are shown in Turkish time: 18:45 UTC is 21:45.
        Assert.Contains("21:45", page);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, "Alındı"));
    }

    [Fact]
    public async Task Recording_without_audio_cannot_be_imported()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);
        RecordingFiles(app, Recording(Uuid, "Video", "MP4", "TRANSCRIPT"));
        var alice = app.ClientFor(app.Seed.AliceId);
        var before = app.WithDb(db => db.Meetings.Count());

        Assert.Contains("ses dosyası yok", await FollowAsync(alice, await ImportAsync(alice)));
        Assert.Equal(before, app.WithDb(db => db.Meetings.Count()));
        Assert.Empty(app.Zoom.To("GET", "/rec/download/abc"));
    }

    // 7
    [Fact]
    public async Task Import_downloads_audio_and_starts_the_usual_analysis()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);
        RecordingFiles(app, Recording(Uuid, "Haftalık toplantı", "MP4", "M4A", "TRANSCRIPT"));

        var res = await ImportAsync(app.ClientFor(app.Seed.AliceId));

        var meeting = app.WithDb(db => db.Meetings.AsNoTracking().Single(m => m.ExternalRecordingId == Uuid));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal($"/Details?id={meeting.Id}", res.Headers.Location!.OriginalString);
        Assert.Equal(MeetingSource.Zoom, meeting.Source);
        Assert.Equal("Haftalık toplantı", meeting.Title);
        Assert.Equal(app.Seed.WorkspaceA, meeting.WorkspaceId);
        Assert.Equal(MeetingStatus.Processing, meeting.Status);
        Assert.Equal(app.Seed.AliceId, meeting.CreatedByUserId);
        Assert.Equal("fake-m4a-bytes", File.ReadAllText(Path.Combine(app.AudioRoot, meeting.AudioFileName!)));
        Assert.Equal(1, app.Jobs.CountFor("ProcessMeetingJob", args => (int)args[0]! == meeting.Id));

        // The bearer token goes to Zoom only; the signed file address on another host gets no token.
        Assert.Equal("Bearer " + StubZoomHandler.AccessToken, Assert.Single(app.Zoom.To("GET", "/rec/download/abc")).Authorization);
        Assert.Null(Assert.Single(app.Zoom.To("GET", "/signed/file.m4a")).Authorization);
    }

    [Fact]
    public async Task Download_address_outside_zoom_is_refused()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);
        RecordingFiles(app, Recording(Uuid, "T", "M4A").Replace(DownloadUrl, "https://cdn.example.test/signed/file.m4a"));
        var alice = app.ClientFor(app.Seed.AliceId);
        var before = app.WithDb(db => db.Meetings.Count());

        await ImportAsync(alice);

        Assert.Empty(app.Zoom.To("GET", "/signed/file.m4a"));
        Assert.Equal(before, app.WithDb(db => db.Meetings.Count()));
    }

    // Review finding: a recording stopped and restarted has several audio parts; taking only the first would lose the rest.
    [Fact]
    public async Task Recording_with_several_audio_parts_is_not_taken_half()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);
        RecordingFiles(app, Recording(Uuid, "Parçalı", "M4A", "M4A"));
        var alice = app.ClientFor(app.Seed.AliceId);
        var before = app.WithDb(db => db.Meetings.Count());

        Assert.Contains("birden çok parçadan", await FollowAsync(alice, await ImportAsync(alice)));
        Assert.Equal(before, app.WithDb(db => db.Meetings.Count()));
        Assert.Empty(app.Zoom.To("GET", "/rec/download/abc"));
    }

    // Review finding: the connection dropping in the middle of the file gives a plain message, not an error page.
    [Fact]
    public async Task Download_broken_midway_shows_a_message_and_keeps_nothing()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);
        RecordingFiles(app, Recording(Uuid, "Kopuk", "M4A"));
        app.Zoom.On("GET", "/signed/file.m4a", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) });
        var alice = app.ClientFor(app.Seed.AliceId);
        var before = app.WithDb(db => db.Meetings.Count());

        Assert.Contains("Zoom'a şu an ulaşılamıyor", await FollowAsync(alice, await ImportAsync(alice)));
        Assert.Equal(before, app.WithDb(db => db.Meetings.Count()));
        Assert.Empty(Directory.GetFiles(app.AudioRoot));
    }

    private sealed class BrokenStream : MemoryStream
    {
        private bool _sent;
        public BrokenStream() : base(new byte[1024]) { }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_sent) throw new IOException("connection reset");
            _sent = true;
            return base.ReadAsync(buffer, cancellationToken);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    // 8
    [Fact]
    public async Task Same_recording_is_taken_once_per_team()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);
        Connect(app, app.Seed.BobId);
        RecordingFiles(app, Recording(Uuid, "Toplantı", "M4A"));
        var alice = app.ClientFor(app.Seed.AliceId);

        var first = await ImportAsync(alice);
        var second = await ImportAsync(alice);

        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Equal(1, app.WithDb(db => db.Meetings.Count(m => m.ExternalRecordingId == Uuid)));
        Assert.Single(app.Zoom.To("GET", "/rec/download/abc"));
        Assert.Single(app.Jobs.Created);

        await ImportAsync(app.ClientFor(app.Seed.BobId));
        Assert.Equal(1, app.WithDb(db => db.Meetings.Count(m => m.ExternalRecordingId == Uuid && m.WorkspaceId == app.Seed.WorkspaceB)));
    }

    // 9
    [Fact]
    public async Task Recording_over_the_size_limit_is_not_downloaded()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);
        RecordingFiles(app, Recording(Uuid, "Uzun", "M4A").Replace("\"file_size\":1000", "\"file_size\":2000000"));
        var alice = app.ClientFor(app.Seed.AliceId);
        var before = app.WithDb(db => db.Meetings.Count());

        Assert.Contains("Kayıt çok büyük", await FollowAsync(alice, await ImportAsync(alice)));
        Assert.Empty(app.Zoom.To("GET", "/rec/download/abc"));
        Assert.Equal(before, app.WithDb(db => db.Meetings.Count()));
    }

    [Fact]
    public async Task Download_that_turns_out_too_large_leaves_nothing_behind()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);
        RecordingFiles(app, Recording(Uuid, "Uzun", "M4A"), new byte[1024 * 1024 + 10]);
        var alice = app.ClientFor(app.Seed.AliceId);
        var before = app.WithDb(db => db.Meetings.Count());

        Assert.Contains("Kayıt çok büyük", await FollowAsync(alice, await ImportAsync(alice)));
        Assert.Equal(before, app.WithDb(db => db.Meetings.Count()));
        Assert.Empty(Directory.GetFiles(app.AudioRoot));
        Assert.Empty(app.Jobs.Created);
    }

    // 10
    [Fact]
    public async Task Without_own_connection_list_and_import_go_to_profile()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);
        RecordingFiles(app, Recording(Uuid, "Toplantı", "M4A"));
        var carol = app.ClientFor(app.Seed.CarolId);

        var list = await carol.GetAsync("/Zoom/Recordings");
        Assert.Equal("/Profile", list.Headers.Location!.OriginalString);
        Assert.Contains("Önce Zoom hesabınızı bağlayın", await FollowAsync(carol, list));
        var import = await ImportAsync(carol);
        Assert.Equal("/Profile", import.Headers.Location!.OriginalString);

        Assert.Empty(app.Zoom.Requests);
        Assert.Equal(0, app.WithDb(db => db.Meetings.Count(m => m.ExternalRecordingId == Uuid)));

        var anonymous = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.NotEqual(HttpStatusCode.OK, (await anonymous.GetAsync("/Zoom/Recordings")).StatusCode);
    }

    [Fact]
    public async Task User_without_a_team_cannot_import()
    {
        using var app = await AppAsync();
        var daveId = app.WithDb(db =>
        {
            var dave = new ApplicationUser { UserName = "dave@d.test", Email = "dave@d.test", SecurityStamp = Guid.NewGuid().ToString() };
            db.Users.Add(dave);
            db.SaveChanges();
            return dave.Id;
        });
        Connect(app, daveId);
        RecordingFiles(app, Recording(Uuid, "Toplantı", "M4A"));
        var dave = app.ClientFor(daveId);

        Assert.Contains("Henüz bir ekibiniz yok", await FollowAsync(dave, await ImportAsync(dave)));
        Assert.Equal(0, app.WithDb(db => db.Meetings.Count(m => m.ExternalRecordingId == Uuid)));
        Assert.Empty(app.Zoom.To("GET", "/rec/download/abc"));
    }

    [Fact]
    public async Task Upload_page_links_to_zoom_only_when_connected()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);

        Assert.Contains("/Zoom/Recordings", await app.ClientFor(app.Seed.AliceId).GetStringAsync("/Upload"));
        Assert.DoesNotContain("/Zoom/Recordings", await app.ClientFor(app.Seed.CarolId).GetStringAsync("/Upload"));
    }

    // 11
    [Fact]
    public async Task Account_deletion_removes_the_zoom_connection()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.BobId);
        app.WithDb(db => { db.Users.Single(u => u.Id == app.Seed.BobId).DeletionRequestedAtUtc = DateTime.UtcNow.AddDays(-8); return db.SaveChanges(); });

        using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IAccountDeletionService>().RunAsync();

        Assert.False(app.WithDb(db => db.Users.Any(u => u.Id == app.Seed.BobId)));
        Assert.Null(ConnectionOf(app, app.Seed.BobId));
        Assert.Contains("token=" + StubZoomHandler.AccessToken, Assert.Single(app.Zoom.To("POST", "/oauth/revoke")).Body);
    }
}
