using System.Net;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features;

public class OverdueReminderTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 6, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1, true)] [InlineData(3, true)] [InlineData(7, true)]
    [InlineData(2, false)] [InlineData(4, false)] [InlineData(8, false)]
    public async Task Sends_only_on_configured_overdue_days(int days, bool expected)
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        AddTask(app, app.Seed.CarolId, DateOnly.FromDateTime(Now).AddDays(-days), "Deneme");
        await RunAsync(app);
        Assert.Equal(expected ? 1 : 0, mail.Sent.Count);
        if (expected) Assert.Equal(SeedData.CarolEmail, mail.Sent[0].Address);
    }

    [Fact]
    public async Task Sends_one_email_with_all_late_tasks_sorted_most_overdue_first()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        AddTask(app, app.Seed.CarolId, new(2026, 10, 22), "Yarın geciken");
        AddTask(app, app.Seed.CarolId, new(2026, 10, 20), "Üç gün geciken");
        AddTask(app, app.Seed.CarolId, new(2026, 10, 18), "Beş gün geciken");
        await RunAsync(app);
        var sent = Assert.Single(mail.Sent);
        Assert.Equal(new[] { "Beş gün geciken", "Üç gün geciken", "Yarın geciken" }, sent.Tasks.Select(t => t.Text));
    }

    [Fact]
    public async Task Sends_only_to_task_assignee_and_never_includes_other_assignees_data()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        app.WithDb(db => { db.Users.Single(u => u.Id == app.Seed.CarolId).FullName = "Carol"; db.Users.Single(u => u.Id == app.Seed.AliceId).FullName = "Alice private"; db.SaveChanges(); return 0; });
        AddTask(app, app.Seed.CarolId, new(2026, 10, 22), "Carol task");
        AddTask(app, app.Seed.AliceId, new(2026, 10, 22), "Alice private task");
        await RunAsync(app);
        Assert.Equal(2, mail.Sent.Count);
        var sent = Assert.Single(mail.Sent, m => m.Address == SeedData.CarolEmail);
        Assert.Equal("Carol", sent.Name); Assert.Equal(new[] { "Carol task" }, sent.Tasks.Select(t => t.Text));
        var alice = Assert.Single(mail.Sent, m => m.Address == SeedData.AliceEmail);
        Assert.Equal("Alice private", alice.Name); Assert.Equal(new[] { "Alice private task" }, alice.Tasks.Select(t => t.Text));
    }

    [Fact]
    public async Task Ignores_done_unassigned_and_undated_tasks()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        app.WithDb(db =>
        {
            var done = db.ActionItems.Single(a => a.Id == app.Seed.TaskA); done.AssigneeUserId = app.Seed.CarolId; done.DueDate = new(2026, 10, 22); done.CompletedAtUtc = Now;
            db.ActionItems.Add(new ActionItem { MeetingId = app.Seed.MeetingA, Text = "Atanmamış", DueDate = new(2026, 10, 22) });
            db.ActionItems.Add(new ActionItem { MeetingId = app.Seed.MeetingA, Text = "Tarihsiz", AssigneeUserId = app.Seed.CarolId });
            db.SaveChanges(); return 0;
        });
        await RunAsync(app); Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task Excludes_disabled_deletion_pending_and_former_members_but_only_filters_left_team_tasks()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        app.WithDb(db =>
        {
            db.Users.Single(u => u.Id == app.Seed.AliceId).OverdueRemindersEnabled = false;
            db.ActionItems.Add(new ActionItem { MeetingId = app.Seed.MeetingA, Text = "Kapatanın görevi", AssigneeUserId = app.Seed.AliceId, DueDate = new(2026, 10, 22) });
            db.Users.Single(u => u.Id == app.Seed.BobId).DeletionRequestedAtUtc = Now;
            db.WorkspaceMembers.Remove(db.WorkspaceMembers.Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.CarolId));
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = app.Seed.WorkspaceB, UserId = app.Seed.CarolId, Role = WorkspaceRole.Member });
            db.Meetings.Add(new Meeting { WorkspaceId = app.Seed.WorkspaceB, Title = "Kalan ekip", AudioFileName = "x", ActionItems = [new ActionItem { Text = "Kalan görev", AssigneeUserId = app.Seed.CarolId, DueDate = new(2026, 10, 22) }] });
            var former = db.ActionItems.Single(a => a.Id == app.Seed.TaskA); former.AssigneeUserId = app.Seed.CarolId; former.DueDate = new(2026, 10, 22); former.Text = "Çıkılan ekip gizli görev";
            var pending = db.ActionItems.Single(a => a.Id == app.Seed.TaskB); pending.AssigneeUserId = app.Seed.BobId; pending.DueDate = new(2026, 10, 22);
            db.SaveChanges(); return 0;
        });
        await RunAsync(app);
        var sent = Assert.Single(mail.Sent); Assert.Equal(SeedData.CarolEmail, sent.Address);
        Assert.Equal(new[] { "Kalan görev" }, sent.Tasks.Select(t => t.Text));
        Assert.DoesNotContain("Çıkılan ekip", string.Join(" ", sent.Tasks.Select(t => t.Text + t.MeetingTitle)));
    }

    [Fact]
    public async Task Uses_Turkey_calendar_day_at_utc_2230()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail, new DateTime(2026, 10, 22, 22, 30, 0, DateTimeKind.Utc));
        AddTask(app, app.Seed.CarolId, new(2026, 10, 22), "Dün sona eren");
        await RunAsync(app); Assert.Single(mail.Sent);
        Assert.Equal(1, mail.Sent[0].Tasks[0].DaysOverdue);
    }

    [Fact]
    public async Task Failure_for_one_recipient_does_not_stop_others_or_log_personal_data()
    {
        var logs = new CapturingLoggerProvider(); var mail = new RecordingEmail(SeedData.CarolEmail);
        using var app = await ReadyAsync(mail, log: logs);
        app.WithDb(db => { db.Users.Single(u => u.Id == app.Seed.CarolId).FullName = "Carol Secret"; db.SaveChanges(); return 0; });
        AddTask(app, app.Seed.CarolId, new(2026, 10, 22), "A"); AddTask(app, app.Seed.BobId, new(2026, 10, 22), "B", app.Seed.MeetingB);
        await RunAsync(app);
        Assert.Contains(mail.Sent, m => m.Address == SeedData.BobEmail);
        var log = string.Join(" ", logs.Lines); Assert.DoesNotContain(SeedData.CarolEmail, log); Assert.DoesNotContain("Carol Secret", log);
    }

    [Fact]
    public async Task Membership_is_checked_again_right_before_each_person_is_emailed()
    {
        // While the first mail is being sent, the other person is removed from team A; their mail must not carry A's tasks.
        BriefynFactory? running = null;
        var mail = new RecordingEmail(onSend: address => running!.WithDb(db =>
        {
            var otherId = address == SeedData.CarolEmail ? running!.Seed.AliceId : running!.Seed.CarolId;
            db.WorkspaceMembers.Remove(db.WorkspaceMembers.Single(m => m.WorkspaceId == running.Seed.WorkspaceA && m.UserId == otherId));
            db.SaveChanges(); return 0;
        }));
        using var app = await ReadyAsync(mail); running = app;
        AddTask(app, app.Seed.CarolId, new(2026, 10, 22), "Carol A görevi");
        AddTask(app, app.Seed.AliceId, new(2026, 10, 22), "Alice A görevi");
        await RunAsync(app);
        Assert.Single(mail.Sent);
    }

    [Fact]
    public async Task Profile_handler_changes_only_own_setting_and_requires_antiforgery()
    {
        using var app = await ReadyAsync(new RecordingEmail()); var client = app.ClientFor(app.Seed.CarolId);
        var noToken = await client.PostAsync("/Profile?handler=OverdueReminders", new FormUrlEncodedContent([new("enabled", "false")]));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        var token = await GetAntiforgeryTokenAsync(client, "/Profile");
        await PostFormAsync(client, "/Profile?handler=OverdueReminders", token, ("enabled", "false"));
        Assert.False(app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.CarolId).OverdueRemindersEnabled));
        token = await GetAntiforgeryTokenAsync(client, "/Profile");
        await PostFormAsync(client, "/Profile?handler=OverdueReminders", token, ("enabled", "true"));
        Assert.True(app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.CarolId).OverdueRemindersEnabled));
        Assert.True(app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.AliceId).OverdueRemindersEnabled));
    }

    [Fact]
    public void Reminder_days_config_replaces_empty_default()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Reminders:Days:0"] = "2" }).Build();
        var options = config.GetSection("Reminders").Get<ReminderOptions>();
        Assert.Equal(new[] { 2 }, options!.Days);
    }

    [Fact]
    public void Reminder_html_encodes_user_text_and_name()
    {
        var html = EmailService.BuildOverdueReminderHtml("<script>Ad</script>", [new OverdueTask("<script>x</script>", "<b>Toplantı</b>", Now, 1)]);
        Assert.DoesNotContain("<script>", html); Assert.Contains("&lt;script&gt;", html);
    }

    private static async Task<BriefynFactory> ReadyAsync(RecordingEmail mail, DateTime? now = null, CapturingLoggerProvider? log = null)
    {
        var app = new BriefynFactory { EmailServiceOverride = mail, TimeProviderOverride = new FixedTimeProvider(now ?? Now), LogCapture = log };
        await app.InitializeAsync(); return app;
    }
    private static async Task RunAsync(BriefynFactory app) { using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<IOverdueReminderService>().RunAsync(); }
    private static void AddTask(BriefynFactory app, string assignee, DateOnly due, string text, int? meetingId = null) => app.WithDb(db => { db.ActionItems.Add(new ActionItem { MeetingId = meetingId ?? app.Seed.MeetingA, AssigneeUserId = assignee, DueDate = due, Text = text }); db.SaveChanges(); return 0; });

    private sealed class FixedTimeProvider(DateTime utc) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc)); }
    private sealed class RecordingEmail(string? failAddress = null, Action<string>? onSend = null) : IEmailService
    {
        public List<(string Address, string Name, IReadOnlyList<OverdueTask> Tasks)> Sent { get; } = [];
        public Task SendMeetingSummaryAsync(int meetingId, string protectedRecipient) => Task.CompletedTask;
        public Task SendOverdueReminderAsync(string toEmail, string fullName, IReadOnlyList<OverdueTask> tasks)
        {
            Sent.Add((toEmail, fullName, tasks));
            if (Sent.Count == 1) onSend?.Invoke(toEmail);
            if (toEmail == failAddress) throw new InvalidOperationException("private failure detail");
            return Task.CompletedTask;
        }
    }
}
