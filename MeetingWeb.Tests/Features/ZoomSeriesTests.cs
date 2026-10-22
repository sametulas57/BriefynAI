using System.Net;
using System.Text;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features;

// PRD 2026-10-22 meeting series, criteria 6–8: both Zoom import paths share the series rule.
public class ZoomSeriesTests
{
    private const long Number = 98765432109;

    private static async Task<BriefynFactory> AppAsync(CapturingLoggerProvider? logs = null)
    {
        var app = new BriefynFactory { ZoomEnabled = true, LogCapture = logs };
        // Automatic import runs only when Zoom's notifications are set up.
        app.ExtraSettings["Zoom:WebhookSecret"] = "test-webhook-secret";
        await app.InitializeAsync();
        app.Zoom.Json("GET", "/v2/users/me/recordings", """{"meetings":[]}""");
        app.Zoom.On("GET", "/rec/download/audio", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("fake-m4a-bytes")
        });
        return app;
    }

    private static void Connect(BriefynFactory app, string userId, int? autoTeam = null) => app.WithDb(db =>
    {
        db.ZoomConnections.Add(new ZoomConnection
        {
            UserId = userId,
            ZoomUserId = "zoom-" + userId,
            ZoomEmail = "zoom@" + userId,
            AccessToken = StubZoomHandler.AccessToken,
            RefreshToken = StubZoomHandler.RefreshToken,
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(50),
            AutoImportWorkspaceId = autoTeam
        });
        return db.SaveChanges();
    });

    private static void Recording(BriefynFactory app, string uuid, string topic, string type, long number = Number)
    {
        var json = $$$"""{"uuid":"{{{uuid}}}","id":{{{number}}},"type":{{{type}}},"host_id":"zoom-{{{app.Seed.AliceId}}}","topic":"{{{topic}}}","start_time":"2026-10-22T09:00:00Z","duration":30,"recording_files":[{"file_type":"M4A","file_size":1000,"download_url":"https://zoom.us/rec/download/audio"}]}""";
        app.Zoom.Json("GET", $"/v2/meetings/{uuid}/recordings", json);
        app.Zoom.Json("GET", "/v2/users/me/recordings", $$"""{"meetings":[{{json}}]}""");
    }

    private static async Task ImportAsync(BriefynFactory app, string userId, string uuid)
    {
        var client = app.ClientFor(userId);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Zoom/Recordings")).StatusCode);
        var token = await GetAntiforgeryTokenAsync(client, "/Profile");
        var response = await PostFormAsync(client, "/Zoom/Recordings?handler=Import", token, ("Uuid", uuid));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Details?id=", response.Headers.Location!.OriginalString);
    }

    private static int? SeriesOf(BriefynFactory app, string uuid, int workspaceId) => app.WithDb(db =>
        db.Meetings.AsNoTracking().Single(m => m.ExternalRecordingId == uuid && m.WorkspaceId == workspaceId).SeriesId);

    [Fact]
    public async Task Recurring_recordings_share_a_series_in_one_team_but_not_between_teams()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId, app.Seed.WorkspaceA);
        Connect(app, app.Seed.BobId);
        Recording(app, "first-8", "Haftalık toplantı", "8");
        Recording(app, "second-8", "Yeni Zoom konusu", "\"8\"");
        Recording(app, "first-3", "Sürekli toplantı", "3", Number + 1);

        await ImportAsync(app, app.Seed.AliceId, "first-8");
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IZoomService>().AutoImportAsync("zoom-" + app.Seed.AliceId, "second-8");
        await ImportAsync(app, app.Seed.AliceId, "first-3");
        await ImportAsync(app, app.Seed.BobId, "first-8");

        var first = SeriesOf(app, "first-8", app.Seed.WorkspaceA);
        Assert.NotNull(first);
        Assert.Equal(first, SeriesOf(app, "second-8", app.Seed.WorkspaceA));
        Assert.NotEqual(first, SeriesOf(app, "first-3", app.Seed.WorkspaceA));
        Assert.NotNull(SeriesOf(app, "first-3", app.Seed.WorkspaceA));
        Assert.NotEqual(first, SeriesOf(app, "first-8", app.Seed.WorkspaceB));
        var series = app.WithDb(db => db.MeetingSeries.AsNoTracking().ToList());
        Assert.Equal(3, series.Count);
        Assert.Equal("Haftalık toplantı", series.Single(s => s.Id == first).Name);
        Assert.Equal("Sürekli toplantı", series.Single(s => s.Id == SeriesOf(app, "first-3", app.Seed.WorkspaceA)).Name);
    }

    [Theory]
    [InlineData(2, Number)]
    [InlineData(4, Number)]
    [InlineData(8, 0L)]
    public async Task Nonrecurring_or_unnumbered_recordings_do_not_create_a_series(int type, long number)
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);
        Recording(app, "not-recurring", "Tek toplantı", type.ToString(), number);
        var before = app.WithDb(db => db.MeetingSeries.Count());

        await ImportAsync(app, app.Seed.AliceId, "not-recurring");

        Assert.Null(SeriesOf(app, "not-recurring", app.Seed.WorkspaceA));
        Assert.Equal(before, app.WithDb(db => db.MeetingSeries.Count()));
    }

    [Fact]
    public async Task Meeting_number_stays_out_of_rows_jobs_logs_and_record_text()
    {
        var logs = new CapturingLoggerProvider();
        using var app = await AppAsync(logs);
        Connect(app, app.Seed.AliceId);
        Recording(app, "private-number", "Gizli numaralı toplantı", "8");

        var page = await app.ClientFor(app.Seed.AliceId).GetStringAsync("/Zoom/Recordings");
        Assert.DoesNotContain(Number.ToString(), page);

        using (var scope = app.Services.CreateScope())
        {
            var recording = await scope.ServiceProvider.GetRequiredService<ZoomApiClient>()
                .GetRecordingAsync(StubZoomHandler.AccessToken, "private-number");
            Assert.NotNull(recording);
            Assert.DoesNotContain(Number.ToString(), recording.ToString());
        }
        await ImportAsync(app, app.Seed.AliceId, "private-number");

        var rows = app.WithDb(db =>
        {
            using var command = db.Database.GetDbConnection().CreateCommand();
            var values = new StringBuilder();
            foreach (var table in new[] { "MeetingSeries", "Meetings" })
            {
                command.CommandText = $"SELECT * FROM \"{table}\"";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    for (var i = 0; i < reader.FieldCount; i++) values.Append(reader.GetValue(i));
            }
            return values.ToString();
        });
        Assert.DoesNotContain(Number.ToString(), rows);
        Assert.DoesNotContain(app.Jobs.Created.SelectMany(j => j.Args), arg => arg?.ToString()?.Contains(Number.ToString()) == true);
        Assert.DoesNotContain(logs.Lines, line => line.Contains(Number.ToString()));
    }
}
