using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire.States;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingWeb.Tests.Features;

// PRD 2026-10-22-zoom-asama-2 (ADR-0012): Zoom's notifications import new recordings without pressing "Al".
public class ZoomAutoImportTests
{
    [Fact]
    public async Task Profile_shows_auto_import_only_when_enabled_and_connected()
    {
        using (var app = await WebhookAppAsync(secret: false))
        {
            Connect(app, app.Seed.AliceId);
            var html = await app.ClientFor(app.Seed.AliceId).GetStringAsync("/Profile");
            Assert.DoesNotContain("Yeni Zoom kayıtlarını kendiliğinden al", html);
        }

        using (var app = await WebhookAppAsync())
        {
            Connect(app, app.Seed.AliceId);
            var aliceHtml = await app.ClientFor(app.Seed.AliceId).GetStringAsync("/Profile");
            var bobHtml = await app.ClientFor(app.Seed.BobId).GetStringAsync("/Profile");
            Assert.Contains("Yeni Zoom kayıtlarını kendiliğinden al", aliceHtml);
            Assert.DoesNotContain("Yeni Zoom kayıtlarını kendiliğinden al", bobHtml);
        }
    }

    [Fact]
    public async Task Profile_auto_import_form_saves_and_closes_selection()
    {
        using var app = await WebhookAppAsync();
        Connect(app, app.Seed.AliceId);
        var client = app.ClientFor(app.Seed.AliceId);
        var token = await FormHelpers.GetAntiforgeryTokenAsync(client, "/Profile");

        var saved = await FormHelpers.PostFormAsync(client, "/Profile?handler=ZoomAutoImport", token,
            ("workspaceId", app.Seed.WorkspaceA.ToString()));
        Assert.Equal("/Profile", saved.Headers.Location!.OriginalString);
        Assert.Equal(app.Seed.WorkspaceA, AutoImportOf(app, app.Seed.AliceId));
        Assert.Contains("Otomatik alma ayarı kaydedildi.", WebUtility.HtmlDecode(await client.GetStringAsync("/Profile")));

        token = await FormHelpers.GetAntiforgeryTokenAsync(client, "/Profile");
        var closed = await FormHelpers.PostFormAsync(client, "/Profile?handler=ZoomAutoImport", token, ("workspaceId", ""));
        Assert.Equal("/Profile", closed.Headers.Location!.OriginalString);
        Assert.Null(AutoImportOf(app, app.Seed.AliceId));
    }

    [Fact]
    public async Task Profile_rejects_another_teams_id_and_does_not_list_its_name()
    {
        using var app = await WebhookAppAsync();
        Connect(app, app.Seed.AliceId);
        var client = app.ClientFor(app.Seed.AliceId);
        var html = await client.GetStringAsync("/Profile");
        Assert.DoesNotContain("B Şirketi", html);

        var token = await FormHelpers.GetAntiforgeryTokenAsync(client, "/Profile");
        var response = await FormHelpers.PostFormAsync(client, "/Profile?handler=ZoomAutoImport", token,
            ("workspaceId", app.Seed.WorkspaceB.ToString()));
        Assert.Equal("/Profile", response.Headers.Location!.OriginalString);
        Assert.Null(AutoImportOf(app, app.Seed.AliceId));
        Assert.Contains("Bu ekip seçilemedi.", WebUtility.HtmlDecode(await client.GetStringAsync("/Profile")));
    }

    private static Task<BriefynFactory> AppAsync() => WebhookAppAsync();

    private static void Connect(BriefynFactory app, string userId, int? autoImportWorkspaceId = null) => app.WithDb(db =>
    {
        db.ZoomConnections.Add(new ZoomConnection
        {
            UserId = userId,
            ZoomUserId = "zoom-" + userId,
            ZoomEmail = "zoom@" + userId,
            AccessToken = StubZoomHandler.AccessToken,
            RefreshToken = StubZoomHandler.RefreshToken,
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(50),
            AutoImportWorkspaceId = autoImportWorkspaceId
        });
        return db.SaveChanges();
    });

    private static int? AutoImportOf(BriefynFactory app, string userId) =>
        app.WithDb(db => db.ZoomConnections.AsNoTracking().Single(z => z.UserId == userId).AutoImportWorkspaceId);

    private static async Task<bool> SetAsync(BriefynFactory app, string userId, int? workspaceId)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IZoomService>().SetAutoImportAsync(userId, workspaceId);
    }

    // 13
    [Fact]
    public async Task Auto_import_can_be_set_only_to_own_team()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);

        Assert.True(await SetAsync(app, app.Seed.AliceId, app.Seed.WorkspaceA));
        Assert.Equal(app.Seed.WorkspaceA, AutoImportOf(app, app.Seed.AliceId));

        Assert.False(await SetAsync(app, app.Seed.AliceId, app.Seed.WorkspaceB));
        Assert.Equal(app.Seed.WorkspaceA, AutoImportOf(app, app.Seed.AliceId));

        Assert.True(await SetAsync(app, app.Seed.AliceId, null));
        Assert.Null(AutoImportOf(app, app.Seed.AliceId));

        // Without a connection there is nothing to turn on.
        Assert.False(await SetAsync(app, app.Seed.CarolId, app.Seed.WorkspaceA));
    }

    [Fact]
    public async Task Deleting_the_team_turns_auto_import_off()
    {
        using var app = await AppAsync();
        var teamId = app.WithDb(db =>
        {
            var team = new Workspace { Name = "Silinecek" };
            db.Workspaces.Add(team);
            db.SaveChanges();
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = team.Id, UserId = app.Seed.AliceId, Role = WorkspaceRole.Owner });
            db.SaveChanges();
            return team.Id;
        });
        Connect(app, app.Seed.AliceId, teamId);

        app.WithDb(db => db.Workspaces.Where(w => w.Id == teamId).ExecuteDelete());

        Assert.Null(AutoImportOf(app, app.Seed.AliceId));
    }

    // --- Notification endpoint (criteria 1–4, 12) ---

    private const string Secret = "test-webhook-secret";
    private const string Uuid = "Zx/9Q+abc==";
    private const string RecordingPath = "/v2/meetings/Zx%2F9Q%2Babc%3D%3D/recordings";
    private const string DownloadToken = "DOWNLOAD-TOKEN-PLAIN";

    private static async Task<BriefynFactory> WebhookAppAsync(bool secret = true, CapturingLoggerProvider? logs = null, int? limitPerMinute = null)
    {
        var app = new BriefynFactory { ZoomEnabled = true, LogCapture = logs };
        if (secret) app.ExtraSettings["Zoom:WebhookSecret"] = Secret;
        if (limitPerMinute != null) app.ExtraSettings["Zoom:WebhookLimitPerMinute"] = limitPerMinute.ToString()!;
        await app.InitializeAsync();
        return app;
    }

    private static string Hmac(string message) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(message)));

    // Signs `signed` the way Zoom does and sends `sent` (the same body unless a test changes it).
    private static async Task<HttpResponseMessage> PostAsync(BriefynFactory app, string signed, string? sent = null,
        DateTimeOffset? at = null, bool sign = true, string path = "/Zoom/Webhook")
    {
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString();
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(sent ?? signed, Encoding.UTF8, "application/json")
        };
        if (sign)
        {
            request.Headers.Add("x-zm-request-timestamp", timestamp);
            request.Headers.Add("x-zm-signature", "v0=" + Hmac($"v0:{timestamp}:{signed}"));
        }
        var anonymous = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        return await anonymous.SendAsync(request);
    }

    private static string RecordingEvent(string name, string hostId, string uuid = Uuid) =>
        $$$"""
        {"event":"{{{name}}}","event_ts":1761120000000,"download_token":"{{{DownloadToken}}}",
         "payload":{"account_id":"acc-1","object":{"uuid":"{{{uuid}}}","id":123,"host_id":"{{{hostId}}}","host_email":"ulas@zoom.test",
          "topic":"Haftalık","recording_files":[{"file_type":"M4A","status":"completed","download_url":"https://zoom.us/rec/download/abc"}]}} }
        """;

    // 1
    [Fact]
    public async Task Without_webhook_secret_endpoint_is_404_and_auto_import_is_unavailable()
    {
        using var app = await WebhookAppAsync(secret: false);
        Connect(app, app.Seed.AliceId);

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(app, RecordingEvent("recording.completed", "zoom-" + app.Seed.AliceId))).StatusCode);
        using var scope = app.Services.CreateScope();
        var zoom = scope.ServiceProvider.GetRequiredService<IZoomService>();
        Assert.False(zoom.IsAutoImportEnabled);
        Assert.False(await zoom.SetAutoImportAsync(app.Seed.AliceId, app.Seed.WorkspaceA));
        Assert.Empty(app.Jobs.Created);
    }

    // 2
    [Fact]
    public async Task Url_validation_answers_with_signed_token_only_to_signed_requests()
    {
        using var app = await WebhookAppAsync();
        var body = """{"event":"endpoint.url_validation","payload":{"plainToken":"qgg8vlvZRS6UYooatFL8Aw"},"event_ts":1654503849680}""";

        var res = await PostAsync(app, body);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("qgg8vlvZRS6UYooatFL8Aw", json.RootElement.GetProperty("plainToken").GetString());
        Assert.Equal(Hmac("qgg8vlvZRS6UYooatFL8Aw"), json.RootElement.GetProperty("encryptedToken").GetString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(app, body, sign: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(app, body.Replace("qgg8", "xxxx"), body)).StatusCode);
    }

    // 3
    [Fact]
    public async Task Signature_covers_raw_body_and_fresh_time_and_body_size_is_limited()
    {
        using var app = await WebhookAppAsync();
        var body = RecordingEvent("recording.completed", "zoom-" + app.Seed.AliceId);

        // One byte changed, or the same JSON written differently: the signature no longer matches.
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(app, body, body.Replace("Haftalık", "Haftalik"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(app, body, body.Replace("\n", ""))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(app, body, at: DateTimeOffset.UtcNow.AddMinutes(-6))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(app, body, at: DateTimeOffset.UtcNow.AddMinutes(6))).StatusCode);

        var large = body.Replace("\"Haftalık\"", "\"" + new string('a', 256 * 1024) + "\"");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await PostAsync(app, large)).StatusCode);
        Assert.Empty(app.Jobs.Created);
    }

    // 4, 9 (the transcript event does not wait)
    [Fact]
    public async Task Recording_events_only_queue_a_job_with_ids_and_never_the_download_token()
    {
        var logs = new CapturingLoggerProvider();
        using var app = await WebhookAppAsync(logs: logs);
        var hostId = "zoom-" + app.Seed.AliceId;

        var res = await PostAsync(app, RecordingEvent("recording.completed", hostId));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var job = Assert.Single(app.Jobs.Created);
        Assert.Equal(nameof(IZoomService.AutoImportAsync), job.Method.Name);
        Assert.Equal(new object?[] { hostId, Uuid }, job.Args);
        var scheduled = Assert.IsType<ScheduledState>(app.Jobs.States[job]);
        Assert.InRange(scheduled.EnqueueAt, DateTime.UtcNow.AddMinutes(9), DateTime.UtcNow.AddMinutes(11));
        Assert.Empty(app.Zoom.Requests);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(app, RecordingEvent("recording.transcript_completed", hostId))).StatusCode);
        var now = Assert.Single(app.Jobs.Created, j => j != job);
        Assert.Equal(new object?[] { hostId, Uuid }, now.Args);
        Assert.IsType<EnqueuedState>(app.Jobs.States[now]);

        // Events the app does not use are accepted and ignored.
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(app, RecordingEvent("meeting.started", hostId))).StatusCode);
        Assert.Equal(2, app.Jobs.Created.Count);

        Assert.DoesNotContain(app.Jobs.Created.SelectMany(j => j.Args), a => a?.ToString()?.Contains(DownloadToken) == true);
        Assert.DoesNotContain(logs.Lines, l => l.Contains(DownloadToken));
        Assert.Empty(app.Zoom.Requests);
    }

    // 12
    [Fact]
    public async Task App_removed_in_zoom_deletes_that_connection()
    {
        using var app = await WebhookAppAsync();
        Connect(app, app.Seed.AliceId);
        Connect(app, app.Seed.BobId);
        string Removed(string userId, string clientId, DateTime? at = null) =>
            $$$"""{"event":"app_deauthorized","event_ts":1,"payload":{"account_id":"acc-1","user_id":"zoom-{{{userId}}}","signature":"s-1","deauthorization_time":"{{{(at ?? DateTime.UtcNow.AddMinutes(1)):yyyy-MM-ddTHH:mm:ssZ}}}","client_id":"{{{clientId}}}"}}""";

        // Another app's notice changes nothing.
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(app, Removed(app.Seed.AliceId, "other-client"))).StatusCode);
        Assert.Equal(2, app.WithDb(db => db.ZoomConnections.Count()));

        // A late notice about an earlier removal keeps the connection made again after it.
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(app, Removed(app.Seed.AliceId, "test-zoom-client", DateTime.UtcNow.AddHours(-1)))).StatusCode);
        Assert.True(app.WithDb(db => db.ZoomConnections.Any(z => z.UserId == app.Seed.AliceId)));

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(app, Removed(app.Seed.AliceId, "test-zoom-client"))).StatusCode);
        Assert.False(app.WithDb(db => db.ZoomConnections.Any(z => z.UserId == app.Seed.AliceId)));
        Assert.True(app.WithDb(db => db.ZoomConnections.Any(z => z.UserId == app.Seed.BobId)));
        Assert.Empty(app.Zoom.Requests);
    }

    [Fact]
    public async Task Endpoint_has_a_rate_limit()
    {
        using var app = await WebhookAppAsync(limitPerMinute: 2);
        var body = RecordingEvent("meeting.started", "zoom-x");

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(app, body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(app, body)).StatusCode);
        var limited = await PostAsync(app, body);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Null(limited.Headers.Location);
        // The same endpoint written with a trailing slash shares the limit.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostAsync(app, body, path: "/Zoom/Webhook/")).StatusCode);
    }

    // --- The job (criteria 5–11) ---

    private static void Recording(BriefynFactory app, string hostId, bool transcript = false)
    {
        var files = """{"file_type":"M4A","file_size":1000,"status":"completed","download_url":"https://zoom.us/rec/download/abc"}""";
        if (transcript) files += """,{"file_type":"TRANSCRIPT","file_size":30,"status":"completed","download_url":"https://zoom.us/rec/download/vtt"}""";
        app.Zoom.Json("GET", RecordingPath,
            $$"""{"uuid":"{{Uuid}}","id":123,"host_id":"{{hostId}}","topic":"Haftalık toplantı","start_time":"2026-10-22T09:00:00Z","duration":30,"recording_files":[{{files}}]}""");
        app.Zoom.On("GET", "/rec/download/abc", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("fake-m4a-bytes") });
        app.Zoom.On("GET", "/rec/download/vtt", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("WEBVTT\n\nAli: söz") });
    }

    private static async Task RunAsync(BriefynFactory app, string zoomUserId)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IZoomService>().AutoImportAsync(zoomUserId, Uuid);
    }

    private static int SecondTeamOf(BriefynFactory app, string userId) => app.WithDb(db =>
    {
        var team = new Workspace { Name = "İkinci ekip" };
        db.Workspaces.Add(team);
        db.SaveChanges();
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = team.Id, UserId = userId, Role = WorkspaceRole.Member });
        db.SaveChanges();
        return team.Id;
    });

    private static int ZoomMeetings(BriefynFactory app) => app.WithDb(db => db.Meetings.Count(m => m.ExternalRecordingId == Uuid));

    // 5
    [Fact]
    public async Task Job_takes_nothing_when_off_not_connected_or_not_the_host()
    {
        using var app = await WebhookAppAsync();
        var alice = "zoom-" + app.Seed.AliceId;
        Connect(app, app.Seed.AliceId);
        Recording(app, alice);

        await RunAsync(app, alice);              // auto import is off
        await RunAsync(app, "zoom-nobody");      // no connection for this Zoom user
        Assert.Empty(app.Zoom.Requests);

        Assert.True(await SetAsync(app, app.Seed.AliceId, app.Seed.WorkspaceA));
        Recording(app, "zoom-someone-else");     // Zoom says another person hosted it
        await RunAsync(app, alice);

        Assert.Equal(0, ZoomMeetings(app));
        Assert.Empty(app.Zoom.To("GET", "/rec/download/abc"));
        Assert.Empty(app.Jobs.Created);
    }

    // 6, 10
    [Fact]
    public async Task Job_imports_into_the_chosen_team_without_transcript_when_there_is_none()
    {
        using var app = await WebhookAppAsync();
        var alice = "zoom-" + app.Seed.AliceId;
        var team = SecondTeamOf(app, app.Seed.AliceId);
        Connect(app, app.Seed.AliceId, team);
        Recording(app, alice);

        await RunAsync(app, alice);

        var meeting = app.WithDb(db => db.Meetings.AsNoTracking().Single(m => m.ExternalRecordingId == Uuid));
        Assert.Equal(team, meeting.WorkspaceId);
        Assert.Equal(MeetingSource.Zoom, meeting.Source);
        Assert.Equal("Haftalık toplantı", meeting.Title);
        Assert.Equal(app.Seed.AliceId, meeting.CreatedByUserId);
        Assert.Equal(MeetingStatus.Processing, meeting.Status);
        Assert.True(File.Exists(Path.Combine(app.AudioRoot, meeting.AudioFileName!)));
        Assert.False(File.Exists(Path.Combine(app.AudioRoot, meeting.AudioFileName! + ".vtt")));
        Assert.Equal(1, app.Jobs.CountFor("ProcessMeetingJob", args => (int)args[0]! == meeting.Id));
    }

    // 9
    [Fact]
    public async Task Job_brings_the_transcript_when_zoom_has_it()
    {
        using var app = await WebhookAppAsync();
        var alice = "zoom-" + app.Seed.AliceId;
        Connect(app, app.Seed.AliceId, app.Seed.WorkspaceA);
        Recording(app, alice, transcript: true);

        await RunAsync(app, alice);

        var stored = app.WithDb(db => db.Meetings.Single(m => m.ExternalRecordingId == Uuid).AudioFileName!);
        Assert.Equal("WEBVTT\n\nAli: söz", File.ReadAllText(Path.Combine(app.AudioRoot, stored + ".vtt")));
    }

    // 7
    [Fact]
    public async Task Job_turns_auto_import_off_when_the_user_left_the_team()
    {
        using var app = await WebhookAppAsync();
        var alice = "zoom-" + app.Seed.AliceId;
        var team = SecondTeamOf(app, app.Seed.AliceId);
        Connect(app, app.Seed.AliceId, team);
        Recording(app, alice);
        app.WithDb(db => db.WorkspaceMembers.Where(m => m.WorkspaceId == team).ExecuteDelete());

        await RunAsync(app, alice);

        Assert.Equal(0, ZoomMeetings(app));
        Assert.Null(AutoImportOf(app, app.Seed.AliceId));
        Assert.Empty(app.Zoom.Requests);
    }

    // 8
    [Fact]
    public async Task Two_events_and_a_manual_import_make_one_meeting()
    {
        using var app = await WebhookAppAsync();
        var alice = "zoom-" + app.Seed.AliceId;
        Connect(app, app.Seed.AliceId, app.Seed.WorkspaceA);
        Recording(app, alice);

        await RunAsync(app, alice);
        await RunAsync(app, alice);
        var client = app.ClientFor(app.Seed.AliceId);
        var token = await FormHelpers.GetAntiforgeryTokenAsync(client, "/Profile");
        var manual = await FormHelpers.PostFormAsync(client, "/Zoom/Recordings?handler=Import", token, ("Uuid", Uuid));

        Assert.Equal(1, ZoomMeetings(app));
        var id = app.WithDb(db => db.Meetings.Single(m => m.ExternalRecordingId == Uuid).Id);
        Assert.Equal($"/Details?id={id}", manual.Headers.Location!.OriginalString);
        Assert.Single(app.Zoom.To("GET", "/rec/download/abc"));
        Assert.Equal(1, app.Jobs.CountFor("ProcessMeetingJob"));
    }

    [Fact]
    public async Task Choice_changed_during_the_download_adds_nothing_to_the_old_team()
    {
        using var app = await WebhookAppAsync();
        var alice = "zoom-" + app.Seed.AliceId;
        Connect(app, app.Seed.AliceId, app.Seed.WorkspaceA);
        Recording(app, alice);
        app.Zoom.On("GET", "/rec/download/abc", _ =>
        {
            app.WithDb(db => db.ZoomConnections.ExecuteUpdate(s => s.SetProperty(z => z.AutoImportWorkspaceId, (int?)null)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("fake-m4a-bytes") };
        });

        await RunAsync(app, alice);

        Assert.Equal(0, ZoomMeetings(app));
        Assert.Empty(Directory.GetFiles(app.AudioRoot));
        Assert.Empty(app.Jobs.Created);
    }

    // 11
    [Fact]
    public async Task Rejected_grant_removes_the_connection_and_the_job_ends_without_retry()
    {
        using var app = await WebhookAppAsync();
        var alice = "zoom-" + app.Seed.AliceId;
        Connect(app, app.Seed.AliceId, app.Seed.WorkspaceA);
        app.WithDb(db => db.ZoomConnections.Where(z => z.UserId == app.Seed.AliceId)
            .ExecuteUpdate(s => s.SetProperty(z => z.AccessTokenExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1))));
        app.Zoom.Json("POST", "/oauth/token", """{"reason":"Invalid Token!","error":"invalid_grant"}""", HttpStatusCode.BadRequest);
        Recording(app, alice);

        await RunAsync(app, alice); // does not throw, so Hangfire does not try again

        Assert.False(app.WithDb(db => db.ZoomConnections.Any()));
        Assert.Equal(0, ZoomMeetings(app));
    }
}
