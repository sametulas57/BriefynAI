using System.Net;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features;

public class SeriesAgendaTests : IAsyncLifetime
{
    private readonly BriefynFactory _app = new();
    private SeedData S => _app.Seed;
    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private int Series(int workspaceId, string name) => _app.WithDb(db =>
    {
        var series = new MeetingSeries { WorkspaceId = workspaceId, Name = name };
        db.MeetingSeries.Add(series); db.SaveChanges(); return series.Id;
    });
    private async Task<SeriesAgenda> Build(int id, DateOnly today)
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISeriesAgendaService>().BuildAsync(id, today);
    }

    [Fact]
    public async Task Builds_ordered_open_tasks_and_only_latest_completed_points()
    {
        var id = Series(S.WorkspaceA, "Haftalık");
        var other = Series(S.WorkspaceA, "Diğer");
        var today = new DateOnly(2026, 10, 22);
        _app.WithDb(db =>
        {
            var old = db.Meetings.Single(m => m.Id == S.MeetingA);
            old.SeriesId = id; old.Status = MeetingStatus.Completed; old.CreatedAtUtc = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
            old.Points.Add(new MeetingPoint { Kind = MeetingPointKind.Pending, Text = "Eski bekleyen" });
            old.ActionItems.Add(new ActionItem { Text = "Çok geciken", DueDate = today.AddDays(-5) });
            old.ActionItems.Add(new ActionItem { Text = "Yarın", DueDate = today.AddDays(1) });
            old.ActionItems.Add(new ActionItem { Text = "Eski tarihsiz", SortOrder = 2 });
            old.ActionItems.Add(new ActionItem { Text = "Bitmiş", CompletedAtUtc = DateTime.UtcNow });
            var latest = new Meeting { WorkspaceId = S.WorkspaceA, SeriesId = id, Title = "Son", Status = MeetingStatus.Completed, CreatedAtUtc = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc),
                Points = new() { new() { Kind = MeetingPointKind.Pending, Text = "Son bekleyen" }, new() { Kind = MeetingPointKind.Decision, Text = "Son karar" } },
                ActionItems = new() { new() { Text = "Az geciken", DueDate = today.AddDays(-1) }, new() { Text = "Bugün", DueDate = today }, new() { Text = "Yeni tarihsiz", SortOrder = 1 } } };
            db.Meetings.Add(latest);
            db.Meetings.Add(new Meeting { WorkspaceId = S.WorkspaceA, SeriesId = id, Title = "İşleniyor", Status = MeetingStatus.Processing, CreatedAtUtc = DateTime.UtcNow,
                Points = new() { new() { Kind = MeetingPointKind.Pending, Text = "İşlenen bekleyen" } } });
            db.Meetings.Add(new Meeting { WorkspaceId = S.WorkspaceA, SeriesId = id, Title = "Başarısız", Status = MeetingStatus.Failed, CreatedAtUtc = DateTime.UtcNow,
                Points = new() { new() { Kind = MeetingPointKind.Decision, Text = "Başarısız karar" } } });
            db.Meetings.Add(new Meeting { WorkspaceId = S.WorkspaceA, SeriesId = other, Title = "Başka seri", ActionItems = new() { new() { Text = "Başka görev" } } });
            db.Meetings.Add(new Meeting { WorkspaceId = S.WorkspaceA, Title = "Serisiz", ActionItems = new() { new() { Text = "Serisiz görev" } } });
            return db.SaveChanges();
        });
        var result = await Build(id, today);
        Assert.Equal(new[] { "Çok geciken", "Az geciken", "Bugün", "Yarın", "Yeni tarihsiz", "Görev", "Eski tarihsiz" }, result.OpenTasks.Select(t => t.Text));
        Assert.Equal(5, result.OpenTasks[0].OverdueDays);
        Assert.Equal(new[] { "Son bekleyen" }, result.Pending.Select(p => p.Text));
        Assert.Equal(new[] { "Son karar" }, result.Decisions.Select(p => p.Text));
        Assert.True(result.HasCompletedMeeting);
    }

    [Fact]
    public async Task Empty_series_has_no_agenda()
    {
        var result = await Build(Series(S.WorkspaceA, "Boş"), new DateOnly(2026, 10, 22));
        Assert.True(result.IsEmpty);
        Assert.False(result.HasCompletedMeeting);
    }

    [Fact]
    public async Task Toggle_from_agenda_updates_details_and_removes_row()
    {
        var id = Series(S.WorkspaceA, "Gündem");
        _app.WithDb(db => { db.Meetings.Single(m => m.Id == S.MeetingA).SeriesId = id; return db.SaveChanges(); });
        using var alice = _app.ClientFor(S.AliceId);
        var token = await GetAntiforgeryTokenAsync(alice, "/Profile");
        var before = WebUtility.HtmlDecode(await alice.GetStringAsync($"/Series/{id}"));
        Assert.Contains("Görev", before);
        var result = await PostFormForJsonAsync(alice, $"/Details?handler=ToggleTask&taskId={S.TaskA}&done=true", token);
        Assert.True(result.GetProperty("isDone").GetBoolean());
        // Someone else closed it already while the agenda was open: closing again must not reopen it (review round 1).
        var again = await PostFormForJsonAsync(alice, $"/Details?handler=ToggleTask&taskId={S.TaskA}&done=true", token);
        Assert.True(again.GetProperty("isDone").GetBoolean());
        Assert.DoesNotContain("Görev", WebUtility.HtmlDecode(await alice.GetStringAsync($"/Series/{id}")));
        Assert.True(_app.WithDb(db => db.ActionItems.Single(a => a.Id == S.TaskA).IsDone));
    }

    [Fact]
    public async Task Send_agenda_only_queues_allowed_recipients()
    {
        var id = Series(S.WorkspaceA, "Gündem");
        using var alice = _app.ClientFor(S.AliceId);
        var token = await GetAntiforgeryTokenAsync(alice, "/Profile");
        await PostFormAsync(alice, $"/Series/{id}?handler=SendAgenda", token,
            ("selectedEmails", SeedData.CarolEmail), ("selectedEmails", SeedData.BobEmail), ("extraEmails", "extra@example.com"));
        Assert.Equal(2, _app.Jobs.CountFor(nameof(IEmailService.SendSeriesAgendaAsync)));
        var queued = _app.Jobs.Created.Where(j => j.Method.Name == nameof(IEmailService.SendSeriesAgendaAsync)).ToList();
        Assert.Equal(new[] { SeedData.CarolEmail, "extra@example.com" }, queued.Select(j => EmailService.UnprotectRecipient((string)j.Args[1]!)).OrderBy(e => e));
        Assert.All(queued, j => Assert.DoesNotContain("@", (string)j.Args[1]!));
        await PostFormAsync(alice, $"/Series/{id}?handler=SendAgenda", token, ("extraEmails", "a@x.com,b@x.com,c@x.com,d@x.com"));
        Assert.Equal(2, _app.Jobs.CountFor(nameof(IEmailService.SendSeriesAgendaAsync)));
        using var bob = _app.ClientFor(S.BobId);
        var bobToken = await GetAntiforgeryTokenAsync(bob, "/Profile");
        Assert.Equal(HttpStatusCode.NotFound, (await PostFormAsync(bob, $"/Series/{id}?handler=SendAgenda", bobToken, ("extraEmails", "bob@b.test"))).StatusCode);
        Assert.Equal(2, _app.Jobs.CountFor(nameof(IEmailService.SendSeriesAgendaAsync)));
    }

    [Fact]
    public void Agenda_email_contains_sections_and_escapes_html()
    {
        var agenda = new SeriesAgenda(new() { new(1, "<görev>", null, null, null, 1, "Toplantı", DateTime.UtcNow) },
            new() { new("<bekleyen>") }, new() { new("<karar>") }, 1, DateTime.UtcNow);
        var text = EmailService.BuildSeriesAgendaText("Seri", agenda);
        var html = EmailService.BuildSeriesAgendaHtml("Seri", agenda);
        Assert.Contains("<görev>", text);
        Assert.Contains("<bekleyen>", text);
        Assert.Contains("<karar>", text);
        Assert.Contains("&lt;görev&gt;", html);
        Assert.Contains("&lt;bekleyen&gt;", html);
        Assert.Contains("&lt;karar&gt;", html);
        Assert.DoesNotContain("<görev>", html);
    }
}
