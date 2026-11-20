using System.Net.Mail;
using System.Text;
using System.Data.Common;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingWeb.Tests.Features;

public class CalendarSyncTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 6, 0, 0, DateTimeKind.Utc);

    #region Message

    [Fact]
    public void Message_request_has_matching_calendar_method_and_plain_Turkish_body()
    {
        var invite = Invite();
        using var message = EmailService.BuildCalendarMessage(invite, CalendarMethod.Request, "sender@briefyn.test", "Briefyn AI");
        AssertCalendarPart(message, "REQUEST");
        Assert.Equal("sender@briefyn.test", message.From!.Address);
        Assert.Equal("Briefyn AI", message.From.DisplayName);
        Assert.Equal(invite.AttendeeEmail, Assert.Single(message.To).Address);
        Assert.False(message.IsBodyHtml);
        Assert.Equal(Encoding.UTF8, message.SubjectEncoding);
        Assert.Equal(Encoding.UTF8, message.HeadersEncoding);
        Assert.Equal(Encoding.UTF8, message.BodyEncoding);
        Assert.Equal("Görev: Gizli görev", message.Subject);
        Assert.Equal("Gizli görev\nSon tarih: 22 Kasım 2026\n\nBu görevi takviminize eklemek için daveti kabul edin.\n\nBriefyn AI", message.Body);
    }

    [Fact]
    public void Message_cancel_contains_no_task_description_or_url()
    {
        var invite = Invite();
        using var message = EmailService.BuildCalendarMessage(invite, CalendarMethod.Cancel, "sender@briefyn.test", "Briefyn AI");
        AssertCalendarPart(message, "CANCEL");
        Assert.Equal("Takviminizden kaldırılan görev", message.Subject);
        Assert.Equal("Briefyn'deki bir görev takviminizden kaldırıldı. Görev bitmiş, başkasına verilmiş ya da son tarihi kaldırılmış olabilir.\n\nBriefyn AI", message.Body);
        var content = message.Body + message.Subject + CalendarText(message);
        Assert.DoesNotContain(invite.TaskText, content);
        Assert.DoesNotContain(invite.Description, content);
        Assert.DoesNotContain(invite.Url!, content);
    }

    [Fact]
    public void Message_subject_collapses_header_injection_and_tabs_to_one_line()
    {
        using var message = EmailService.BuildCalendarMessage(Invite() with { TaskText = " \tGörev\r\n\tBcc: x@y.z\r\n " }, CalendarMethod.Request, "sender@briefyn.test", "Briefyn AI");
        Assert.Equal("Görev: Görev Bcc: x@y.z", message.Subject);
        Assert.DoesNotContain('\r', message.Subject);
        Assert.DoesNotContain('\n', message.Subject);
        Assert.DoesNotContain('\t', message.Subject);
    }

    [Fact]
    public void Message_subject_cuts_task_text_at_80_characters_with_ellipsis()
    {
        using var message = EmailService.BuildCalendarMessage(Invite() with { TaskText = new string('a', 81) }, CalendarMethod.Request, "sender@briefyn.test", "Briefyn AI");
        Assert.Equal("Görev: " + new string('a', 80) + "…", message.Subject);
    }

    // Outlook files a message with the same subject into the conversation the person deleted (seen 2026-11-20 on
    // Outlook mobile), so the "Takvimime gönder" invite gets a subject of its own.
    [Fact]
    public void Message_subject_of_a_resent_invite_differs_from_the_first_invite()
    {
        using var message = EmailService.BuildCalendarMessage(Invite() with { Resent = true }, CalendarMethod.Request, "sender@briefyn.test", "Briefyn AI");
        Assert.Equal("Takviminiz için yeniden: Gizli görev", message.Subject);
    }

    [Fact]
    public void Message_subject_keeps_exactly_80_characters_without_ellipsis()
    {
        using var message = EmailService.BuildCalendarMessage(Invite() with { TaskText = new string('a', 80) }, CalendarMethod.Request, "sender@briefyn.test", "Briefyn AI");
        Assert.Equal("Görev: " + new string('a', 80), message.Subject);
    }

    private static void AssertCalendarPart(MailMessage message, string method)
    {
        var view = Assert.Single(message.AlternateViews);
        Assert.Equal("text/calendar", view.ContentType.MediaType);
        Assert.Equal(method, view.ContentType.Parameters["method"]);
        Assert.Equal("utf-8", view.ContentType.CharSet, ignoreCase: true);
        Assert.Contains("METHOD:" + method + "\r\n", CalendarText(message));
        Assert.Empty(message.Attachments);
    }

    private static string CalendarText(MailMessage message)
    {
        var stream = Assert.Single(message.AlternateViews).ContentStream;
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static CalendarInviteData Invite() => new("test@briefyn.ai", 0, new(2026, 11, 22), "Gizli görev", "Gizli toplantı", "https://briefyn.example/MyActions", "Briefyn AI", "sender@briefyn.test", "Carol", "carol@a.test", Now);

    #endregion

    [Fact]
    public async Task Dated_assignment_sends_once_and_persists_active_sequence_zero()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        await RunAsync(app);
        var sent = Assert.Single(mail.Sent);
        Assert.Equal(CalendarMethod.Request, sent.Method);
        Assert.Equal(SeedData.CarolEmail, sent.Invite.AttendeeEmail);
        Assert.Equal(UserDisplay.UnknownName, sent.Invite.AttendeeName);
        Assert.Equal("sender@briefyn.test", sent.Invite.OrganizerEmail);
        Assert.Equal("Briefyn AI", sent.Invite.OrganizerName);
        Assert.Equal(Now, sent.Invite.StampUtc);
        var record = Record(app);
        Assert.Equal(CalendarInviteStatus.Active, record.Status);
        Assert.Equal(0, record.Sequence);
        Assert.Equal(sent.Invite.Uid, record.Uid);
        Assert.EndsWith("@briefyn.ai", record.Uid);
        Assert.False(record.SendPending);
        await RunAsync(app);
        Assert.Single(mail.Sent);
    }

    [Fact]
    public async Task Changed_date_updates_the_same_uid_with_next_sequence()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        await RunAsync(app);
        ChangeTask(app, task => task.DueDate = new(2026, 11, 25));
        await RunAsync(app);
        Assert.Equal(2, mail.Sent.Count);
        var updated = mail.Sent[1];
        Assert.Equal(CalendarMethod.Request, updated.Method);
        Assert.Equal(mail.Sent[0].Invite.Uid, updated.Invite.Uid);
        Assert.Equal(1, updated.Invite.Sequence);
        Assert.Contains("DTSTART;VALUE=DATE:20261125", CalendarInviteBuilder.Build(updated.Invite, updated.Method));
        Assert.Equal(new DateOnly(2026, 11, 25), Record(app).DueDate);
        await RunAsync(app);
        Assert.Equal(2, mail.Sent.Count);
    }

    [Fact]
    public async Task Past_due_date_without_a_record_sends_nothing()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        ChangeTask(app, task => task.DueDate = DisplayTime.TodayIn(Now).AddDays(-1));
        await RunAsync(app);
        Assert.Empty(mail.Sent);
        Assert.Empty(Records(app));
    }

    [Fact]
    public async Task Removed_assignee_cancels_once() => await AssertCancellationAsync(task => task.AssigneeUserId = null);

    [Fact]
    public async Task Removed_due_date_cancels_once() => await AssertCancellationAsync(task => task.DueDate = null);

    [Fact]
    public async Task Completed_task_cancels_once() => await AssertCancellationAsync(task => task.CompletedAtUtc = Now);

    private static async Task AssertCancellationAsync(Action<ActionItem> change)
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        await RunAsync(app);
        ChangeTask(app, change);
        await RunAsync(app);
        Assert.Equal(2, mail.Sent.Count);
        var cancel = mail.Sent[1];
        Assert.Equal(CalendarMethod.Cancel, cancel.Method);
        Assert.Equal(mail.Sent[0].Invite.Uid, cancel.Invite.Uid);
        Assert.Equal(1, cancel.Invite.Sequence);
        Assert.Equal(mail.Sent[0].Invite.DueDate, cancel.Invite.DueDate);
        Assert.Equal("", cancel.Invite.TaskText);
        Assert.Equal("", cancel.Invite.Description);
        Assert.Null(cancel.Invite.Url);
        Assert.Equal(CalendarInviteStatus.Cancelled, Record(app).Status);
        Assert.False(Record(app).SendPending);
        await RunAsync(app);
        Assert.Equal(2, mail.Sent.Count);
    }

    [Fact]
    public async Task Reopened_task_reuses_uid_with_higher_sequence()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        await RunAsync(app);
        ChangeTask(app, task => task.CompletedAtUtc = Now);
        await RunAsync(app);
        ChangeTask(app, task => task.CompletedAtUtc = null);
        await RunAsync(app);
        Assert.Equal(new[] { CalendarMethod.Request, CalendarMethod.Cancel, CalendarMethod.Request }, mail.Sent.Select(m => m.Method));
        Assert.All(mail.Sent, m => Assert.Equal(mail.Sent[0].Invite.Uid, m.Invite.Uid));
        Assert.Equal(new[] { 0, 1, 2 }, mail.Sent.Select(m => m.Invite.Sequence));
        Assert.Equal(CalendarInviteStatus.Active, Record(app).Status);
    }

    [Fact]
    public async Task Reassignment_cancels_old_recipient_before_inviting_new_recipient()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        await RunAsync(app);
        ChangeTask(app, task => task.AssigneeUserId = app.Seed.AliceId);
        await RunAsync(app);
        Assert.Equal(3, mail.Sent.Count);
        Assert.Equal((SeedData.CarolEmail, CalendarMethod.Cancel), (mail.Sent[1].Invite.AttendeeEmail, mail.Sent[1].Method));
        Assert.Equal((SeedData.AliceEmail, CalendarMethod.Request), (mail.Sent[2].Invite.AttendeeEmail, mail.Sent[2].Method));
        Assert.Equal(mail.Sent[0].Invite.Uid, mail.Sent[1].Invite.Uid);
        Assert.NotEqual(mail.Sent[0].Invite.Uid, mail.Sent[2].Invite.Uid);
        Assert.Equal(2, Records(app).Count);
        await RunAsync(app);
        Assert.Equal(3, mail.Sent.Count);
    }

    // A failed invite may still have reached the old assignee, so a reassignment cancels it there too.
    [Fact]
    public async Task Reassignment_cancels_old_recipient_whose_invite_send_had_failed()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        AddRecord(app, pending: true);
        ChangeTask(app, task => task.AssigneeUserId = app.Seed.AliceId);
        await RunAsync(app);
        Assert.Equal(2, mail.Sent.Count);
        Assert.Equal((SeedData.CarolEmail, CalendarMethod.Cancel, 1), (mail.Sent[0].Invite.AttendeeEmail, mail.Sent[0].Method, mail.Sent[0].Invite.Sequence));
        Assert.Equal((SeedData.AliceEmail, CalendarMethod.Request), (mail.Sent[1].Invite.AttendeeEmail, mail.Sent[1].Method));
        var carol = Assert.Single(Records(app), r => r.UserId == app.Seed.CarolId);
        Assert.Equal((CalendarInviteStatus.Cancelled, false), (carol.Status, carol.SendPending));
    }

    [Fact]
    public async Task Deleted_task_cancels_and_removes_record() => await AssertDeletionAsync(deleteMeeting: false);

    [Fact]
    public async Task Deleted_meeting_cancels_and_removes_record() => await AssertDeletionAsync(deleteMeeting: true);

    private static async Task AssertDeletionAsync(bool deleteMeeting)
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        await RunAsync(app);
        app.WithDb(db =>
        {
            if (deleteMeeting) db.Meetings.Remove(db.Meetings.Single(m => m.Id == app.Seed.MeetingA));
            else db.ActionItems.Remove(db.ActionItems.Single(a => a.Id == app.Seed.TaskA));
            db.SaveChanges(); return 0;
        });
        await RunAsync(app);
        Assert.Equal(2, mail.Sent.Count);
        Assert.Equal(CalendarMethod.Cancel, mail.Sent[1].Method);
        Assert.Equal(SeedData.CarolEmail, mail.Sent[1].Invite.AttendeeEmail);
        Assert.Equal(mail.Sent[0].Invite.Uid, mail.Sent[1].Invite.Uid);
        Assert.Empty(Records(app));
        await RunAsync(app);
        Assert.Equal(2, mail.Sent.Count);
    }

    [Fact]
    public async Task Former_member_gets_only_a_content_free_cancellation()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        await RunAsync(app);
        app.WithDb(db =>
        {
            db.WorkspaceMembers.Remove(db.WorkspaceMembers.Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.CarolId));
            db.SaveChanges(); return 0;
        });
        await RunAsync(app);
        Assert.Equal(2, mail.Sent.Count);
        var cancel = mail.Sent[1];
        Assert.Equal(CalendarMethod.Cancel, cancel.Method);
        Assert.Equal("", cancel.Invite.TaskText);
        Assert.Equal("", cancel.Invite.Description);
        Assert.Null(cancel.Invite.Url);
        using var message = EmailService.BuildCalendarMessage(cancel.Invite, cancel.Method, "sender@briefyn.test", "Briefyn AI");
        var content = message.Subject + message.Body + CalendarText(message);
        Assert.DoesNotContain("Gizli görev metni", content);
        Assert.DoesNotContain("A gizli toplantı", content);
        Assert.Equal(CalendarInviteStatus.Cancelled, Record(app).Status);
    }

    [Fact]
    public async Task Disabled_preference_blocks_new_invites_but_keeps_existing_entries_updated()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        ChangeCarol(app, user => user.CalendarInvitesEnabled = false);
        await RunAsync(app);
        Assert.Empty(mail.Sent);
        Assert.Empty(Records(app));
        AddRecord(app);
        ChangeTask(app, task => task.DueDate = DisplayTime.TodayIn(Now).AddDays(-1));
        await RunAsync(app);
        var update = Assert.Single(mail.Sent);
        Assert.Equal(CalendarMethod.Request, update.Method);
        Assert.Equal(1, update.Invite.Sequence);
        Assert.Equal(DisplayTime.TodayIn(Now).AddDays(-1), update.Invite.DueDate);
    }

    [Fact]
    public async Task Deletion_pending_assignee_without_record_gets_no_invite()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        ChangeCarol(app, user => user.DeletionRequestedAtUtc = Now);
        await RunAsync(app);
        Assert.Empty(mail.Sent);
        Assert.Empty(Records(app));
    }

    [Fact]
    public async Task Deletion_pending_assignee_with_active_record_gets_one_cancellation()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        AddRecord(app);
        ChangeCarol(app, user => user.DeletionRequestedAtUtc = Now);
        await RunAsync(app);
        Assert.Equal(CalendarMethod.Cancel, Assert.Single(mail.Sent).Method);
        Assert.Equal(CalendarInviteStatus.Cancelled, Record(app).Status);
        await RunAsync(app);
        Assert.Single(mail.Sent);
    }

    [Fact]
    public async Task Failed_request_releases_claim_and_retries_the_same_sequence()
    {
        var mail = new RecordingEmail { FailNext = 1 }; using var app = await ReadyAsync(mail);
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(app));
        var pending = Record(app);
        Assert.True(pending.SendPending);
        Assert.Equal(DateTime.MinValue, pending.UpdatedAtUtc);
        await RunAsync(app);
        Assert.Equal(2, mail.Sent.Count);
        Assert.Equal(mail.Sent[0].Invite.Uid, mail.Sent[1].Invite.Uid);
        Assert.Equal(mail.Sent[0].Invite.Sequence, mail.Sent[1].Invite.Sequence);
        Assert.False(Record(app).SendPending);
    }

    [Fact]
    public async Task In_flight_request_is_skipped_by_nested_sync_on_another_scope()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        mail.OnSend = () =>
        {
            mail.OnSend = null;
            ChangeTask(app, task => task.DueDate = new(2026, 11, 25));
            // SQLite shares one connection: finish the nested scope synchronously, on this thread.
            RunAsync(app).GetAwaiter().GetResult();
        };
        await RunAsync(app);
        Assert.Single(mail.Sent);
        Assert.False(Record(app).SendPending);
    }

    [Fact]
    public async Task Stale_pending_request_is_retried_without_increasing_sequence()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        AddRecord(app, pending: true);
        await RunAsync(app);
        var sent = Assert.Single(mail.Sent);
        Assert.Equal(0, sent.Invite.Sequence);
        Assert.False(Record(app).SendPending);
    }

    [Fact]
    public async Task Failed_cancellation_is_retried_without_increasing_sequence()
    {
        var mail = new RecordingEmail { FailNext = 1 }; using var app = await ReadyAsync(mail);
        AddRecord(app);
        ChangeTask(app, task => task.CompletedAtUtc = Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(app));
        Assert.Equal(CalendarInviteStatus.Cancelled, Record(app).Status);
        Assert.True(Record(app).SendPending);
        Assert.Equal(DateTime.MinValue, Record(app).UpdatedAtUtc);
        await RunAsync(app);
        Assert.Equal(2, mail.Sent.Count);
        Assert.All(mail.Sent, m => Assert.Equal(CalendarMethod.Cancel, m.Method));
        Assert.Equal(mail.Sent[0].Invite.Sequence, mail.Sent[1].Invite.Sequence);
        Assert.False(Record(app).SendPending);
    }

    [Fact]
    public async Task Request_description_includes_configured_public_url()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail, publicUrl: "https://briefyn.example/");
        app.WithDb(db => { db.Meetings.Single(m => m.Id == app.Seed.MeetingA).CreatedAtUtc = Now; db.SaveChanges(); return 0; });
        await RunAsync(app);
        var invite = Assert.Single(mail.Sent).Invite;
        Assert.Equal("https://briefyn.example/MyActions", invite.Url);
        Assert.Equal("Toplantı: A gizli toplantı (20 Kasım 2026)\nGörevlerinizi görmek için: https://briefyn.example/MyActions", invite.Description);
    }

    [Fact]
    public async Task Request_without_public_url_has_no_link()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        await RunAsync(app);
        var invite = Assert.Single(mail.Sent).Invite;
        Assert.Null(invite.Url);
        Assert.StartsWith("Toplantı: A gizli toplantı (", invite.Description);
        Assert.DoesNotContain("Görevlerinizi görmek için", invite.Description);
        Assert.DoesNotContain("http", invite.Description);
    }

    [Fact]
    public async Task Unique_insert_race_reloads_winning_record_without_sending_a_twin()
    {
        var writes = new CalendarWriteInterceptor();
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail, interceptor: writes);
        writes.BeforeWrite = () => { writes.BeforeWrite = null; AddRecord(app); };
        await RunAsync(app);
        Assert.Equal(1, writes.Attempts);
        Assert.Empty(mail.Sent);
        Assert.Equal("existing@briefyn.ai", Record(app).Uid);
    }

    [Fact]
    public async Task Concurrent_claim_update_is_reloaded_and_in_flight_winner_is_skipped()
    {
        var writes = new CalendarWriteInterceptor();
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail, interceptor: writes);
        AddRecord(app);
        ChangeTask(app, task => task.DueDate = new(2026, 11, 25));
        writes.BeforeWrite = () =>
        {
            writes.BeforeWrite = null;
            app.WithDb(db =>
            {
                var winner = db.CalendarInvites.Single();
                winner.DueDate = new(2026, 11, 25); winner.SendPending = true; winner.UpdatedAtUtc = Now;
                db.SaveChanges(); return 0;
            });
        };
        await RunAsync(app);
        Assert.Equal(1, writes.Attempts);
        Assert.Empty(mail.Sent);
        Assert.True(Record(app).SendPending);
        Assert.Equal(Now, Record(app).UpdatedAtUtc);
    }

    [Fact]
    public async Task Repeated_database_failure_is_rethrown_after_three_attempts()
    {
        var writes = new CalendarWriteInterceptor { BeforeWrite = () => throw new DbUpdateException("Retryable write failure") };
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail, interceptor: writes);
        await Assert.ThrowsAsync<DbUpdateException>(() => RunAsync(app));
        Assert.Equal(3, writes.Attempts);
        Assert.Empty(mail.Sent);
        Assert.Empty(Records(app));
    }

    [Fact]
    public async Task Repeated_concurrency_failure_is_rethrown_after_three_attempts()
    {
        var writes = new CalendarWriteInterceptor();
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail, interceptor: writes);
        AddRecord(app);
        ChangeTask(app, task => task.DueDate = new(2026, 11, 25));
        writes.BeforeWrite = () => app.WithDb(db =>
        {
            db.CalendarInvites.Single().UpdatedAtUtc = Now.AddMinutes(writes.Attempts);
            db.SaveChanges(); return 0;
        });
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => RunAsync(app));
        Assert.Equal(3, writes.Attempts);
        Assert.Empty(mail.Sent);
        Assert.False(Record(app).SendPending);
    }

    [Fact]
    public async Task Request_and_idle_run_log_only_one_summary_without_personal_data()
    {
        var logs = new CapturingLoggerProvider();
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail, logs);
        ChangeCarol(app, user => user.FullName = "Carol Secret");
        await RunAsync(app);
        await RunAsync(app);
        Assert.Single(logs.Lines, line => line.Contains("Takvim eşitlendi.", StringComparison.Ordinal));
        Assert.Contains(logs.Lines, line => line.Contains($"Görev={app.Seed.TaskA}, Davet=1, İptal=0", StringComparison.Ordinal));
        AssertNoPersonalLogs(logs);
    }

    [Fact]
    public async Task Former_member_cancellation_logs_no_personal_or_task_data()
    {
        var logs = new CapturingLoggerProvider();
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail, logs);
        ChangeCarol(app, user => user.FullName = "Carol Secret");
        AddRecord(app);
        app.WithDb(db =>
        {
            db.WorkspaceMembers.Remove(db.WorkspaceMembers.Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.CarolId));
            db.SaveChanges(); return 0;
        });
        await RunAsync(app);
        Assert.Contains(logs.Lines, line => line.Contains($"Görev={app.Seed.TaskA}, Davet=0, İptal=1", StringComparison.Ordinal));
        AssertNoPersonalLogs(logs);
    }

    [Fact]
    public async Task Send_failure_logs_only_id_and_exception_type_without_exception_details()
    {
        var logs = new CapturingLoggerProvider();
        var mail = new RecordingEmail { FailNext = 1 }; using var app = await ReadyAsync(mail, logs);
        ChangeCarol(app, user => user.FullName = "Carol Secret");
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(app));
        Assert.Contains(logs.Lines, line => line.Trim() == $"Takvim daveti gönderilemedi. Görev={app.Seed.TaskA}, HataTürü=InvalidOperationException");
        Assert.DoesNotContain(logs.Lines, line => line.Contains("Takvim eşitlendi.", StringComparison.Ordinal));
        await RunAsync(app);
        Assert.Single(logs.Lines, line => line.Contains("Takvim eşitlendi.", StringComparison.Ordinal));
        AssertNoPersonalLogs(logs);
    }

    private static void AssertNoPersonalLogs(CapturingLoggerProvider logs)
    {
        var text = string.Join("\n", logs.Lines);
        foreach (var secret in new[] { SeedData.CarolEmail, SeedData.AliceEmail, SeedData.BobEmail, "Carol Secret", "Gizli görev metni", "A gizli toplantı", "Private mail failure" })
            Assert.DoesNotContain(secret, text);
    }

    private sealed class CalendarWriteInterceptor : DbCommandInterceptor
    {
        public Action? BeforeWrite { get; set; }
        public int Attempts { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO \"CalendarInvites\"", StringComparison.Ordinal)
                || command.CommandText.StartsWith("UPDATE \"CalendarInvites\"", StringComparison.Ordinal))
            {
                Attempts++;
                BeforeWrite?.Invoke();
            }
            return ValueTask.FromResult(result);
        }
    }

    private static void ChangeCarol(BriefynFactory app, Action<ApplicationUser> change) => app.WithDb(db =>
    {
        change(db.Users.Single(u => u.Id == app.Seed.CarolId));
        db.SaveChanges(); return 0;
    });

    private static void AddRecord(BriefynFactory app, bool pending = false) => app.WithDb(db =>
    {
        db.CalendarInvites.Add(new CalendarInvite
        {
            ActionItemId = app.Seed.TaskA,
            UserId = app.Seed.CarolId,
            Uid = "existing@briefyn.ai",
            DueDate = new(2026, 11, 22),
            SendPending = pending,
            UpdatedAtUtc = Now.AddMinutes(-11)
        });
        db.SaveChanges(); return 0;
    });

    private static async Task<BriefynFactory> ReadyAsync(RecordingEmail mail, CapturingLoggerProvider? logs = null, string publicUrl = "", DbCommandInterceptor? interceptor = null)
    {
        var app = new BriefynFactory { EmailServiceOverride = mail, TimeProviderOverride = new FixedTimeProvider(), LogCapture = logs, CommandInterceptor = interceptor };
        app.ExtraSettings["App:PublicUrl"] = publicUrl;
        app.ExtraSettings["EmailSettings:SenderEmail"] = "sender@briefyn.test";
        app.ExtraSettings["EmailSettings:SenderName"] = "Briefyn AI";
        await app.InitializeAsync();
        ChangeTask(app, task => { task.AssigneeUserId = app.Seed.CarolId; task.DueDate = new(2026, 11, 22); task.Text = "Gizli görev metni"; });
        return app;
    }

    private static async Task RunAsync(BriefynFactory app)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICalendarSyncService>().SyncAsync(app.Seed.TaskA);
    }

    private static void ChangeTask(BriefynFactory app, Action<ActionItem> change) => app.WithDb(db =>
    {
        change(db.ActionItems.Single(a => a.Id == app.Seed.TaskA));
        db.SaveChanges(); return 0;
    });

    private static List<CalendarInvite> Records(BriefynFactory app) => app.WithDb(db => db.CalendarInvites.AsNoTracking().Where(i => i.ActionItemId == app.Seed.TaskA).ToList());
    private static CalendarInvite Record(BriefynFactory app) => Assert.Single(Records(app));

    private sealed class FixedTimeProvider : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }

    private sealed class RecordingEmail : IEmailService
    {
        public List<(CalendarInviteData Invite, CalendarMethod Method)> Sent { get; } = [];
        public int FailNext { get; set; }
        public Action? OnSend { get; set; }
        public Task SendMeetingSummaryAsync(int meetingId, string protectedRecipient) => Task.CompletedTask;
        public Task SendSeriesAgendaAsync(int seriesId, string protectedRecipient) => Task.CompletedTask;
        public Task SendOverdueReminderAsync(string toEmail, string fullName, IReadOnlyList<OverdueTask> tasks) => Task.CompletedTask;
        public Task SendCalendarInviteAsync(CalendarInviteData invite, CalendarMethod method)
        {
            Sent.Add((invite, method));
            OnSend?.Invoke();
            if (FailNext > 0) { FailNext--; throw new InvalidOperationException("Private mail failure: " + invite.AttendeeEmail + " " + invite.AttendeeName + " " + invite.TaskText); }
            return Task.CompletedTask;
        }
    }
}

public class CalendarSyncTriggerTests : IAsyncLifetime
{
    private readonly BriefynFactory _app = new();
    private SeedData S => _app.Seed;

    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Assigning_with_a_date_queues_only_the_task_id()
    {
        using var alice = _app.ClientFor(S.AliceId);
        var token = await FormHelpers.GetAntiforgeryTokenAsync(alice, $"/Details?id={S.MeetingA}");

        var json = await FormHelpers.PostFormForJsonAsync(alice, "/Details?handler=AssignAction", token,
            ("taskId", $"{S.TaskA}"), ("assigneeUserId", S.CarolId), ("dueDate", "2026-11-22"));

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(1, _app.Jobs.CountFor(nameof(ICalendarSyncService.SyncAsync),
            args => args.Count == 1 && args[0] is int id && id == S.TaskA));
        var job = Assert.Single(_app.Jobs.Created);
        Assert.Equal(typeof(ICalendarSyncService), job.Type);
        Assert.Equal(S.TaskA, Assert.IsType<int>(Assert.Single(job.Args)));
        Assert.DoesNotContain(_app.Jobs.Created.SelectMany(j => j.Args), arg => arg is string);
    }

    [Fact]
    public async Task Clearing_an_assignment_queues_one_sync_job()
    {
        _app.WithDb(db =>
        {
            var task = db.ActionItems.Single(a => a.Id == S.TaskA);
            task.AssigneeUserId = S.CarolId;
            task.DueDate = new DateOnly(2026, 11, 22);
            return db.SaveChanges();
        });
        using var alice = _app.ClientFor(S.AliceId);
        var token = await FormHelpers.GetAntiforgeryTokenAsync(alice, $"/Details?id={S.MeetingA}");

        var json = await FormHelpers.PostFormForJsonAsync(alice, "/Details?handler=AssignAction", token,
            ("taskId", $"{S.TaskA}"), ("assigneeUserId", ""));

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(1, _app.Jobs.CountFor(nameof(ICalendarSyncService.SyncAsync),
            args => args.Count == 1 && args[0] is int id && id == S.TaskA));
        Assert.Null(_app.WithDb(db => db.ActionItems.Single(a => a.Id == S.TaskA).AssigneeUserId));
    }

    [Fact]
    public async Task Completing_queues_one_job_and_repeating_the_same_state_queues_none()
    {
        using var alice = _app.ClientFor(S.AliceId);
        var token = await FormHelpers.GetAntiforgeryTokenAsync(alice, $"/Details?id={S.MeetingA}");
        var url = $"/Details?handler=ToggleTask&taskId={S.TaskA}";

        var first = await FormHelpers.PostFormForJsonAsync(alice, url, token, ("done", "true"));

        Assert.True(first.GetProperty("success").GetBoolean());
        Assert.True(first.GetProperty("isDone").GetBoolean());
        Assert.Equal(1, _app.Jobs.CountFor(nameof(ICalendarSyncService.SyncAsync),
            args => args.Count == 1 && args[0] is int id && id == S.TaskA));

        var second = await FormHelpers.PostFormForJsonAsync(alice, url, token, ("done", "true"));

        Assert.True(second.GetProperty("success").GetBoolean());
        Assert.True(second.GetProperty("isDone").GetBoolean());
        Assert.Equal(1, _app.Jobs.CountFor(nameof(ICalendarSyncService.SyncAsync)));
    }

    [Fact]
    public async Task Assigning_to_a_non_teammate_queues_no_job()
    {
        using var alice = _app.ClientFor(S.AliceId);
        var token = await FormHelpers.GetAntiforgeryTokenAsync(alice, $"/Details?id={S.MeetingA}");

        var json = await FormHelpers.PostFormForJsonAsync(alice, "/Details?handler=AssignAction", token,
            ("taskId", $"{S.TaskA}"), ("assigneeUserId", S.BobId), ("dueDate", "2026-11-22"));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Empty(_app.Jobs.Created);
    }

    [Fact]
    public async Task Toggling_without_meeting_access_queues_no_job()
    {
        using var bob = _app.ClientFor(S.BobId);
        var token = await FormHelpers.GetAntiforgeryTokenAsync(bob, $"/Details?id={S.MeetingB}");

        using var response = await FormHelpers.PostFormAsync(bob,
            $"/Details?handler=ToggleTask&taskId={S.TaskA}", token, ("done", "true"));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_app.Jobs.Created);
        Assert.False(_app.WithDb(db => db.ActionItems.Single(a => a.Id == S.TaskA).IsDone));
    }

    [Fact]
    public async Task Queue_failure_keeps_the_assignment_saved_and_returns_success()
    {
        using var alice = _app.ClientFor(S.AliceId);
        var token = await FormHelpers.GetAntiforgeryTokenAsync(alice, $"/Details?id={S.MeetingA}");
        _app.Jobs.FailNext = true;

        var json = await FormHelpers.PostFormForJsonAsync(alice, "/Details?handler=AssignAction", token,
            ("taskId", $"{S.TaskA}"), ("assigneeUserId", S.CarolId), ("dueDate", "2026-11-22"));

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(S.CarolId, _app.WithDb(db => db.ActionItems.Single(a => a.Id == S.TaskA).AssigneeUserId));
        Assert.False(_app.Jobs.FailNext);
        Assert.Empty(_app.Jobs.Created);
    }
}

public class CalendarSelfServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Profile_calendar_handler_changes_only_callers_setting_and_requires_antiforgery()
    {
        using var app = await ReadyAsync(new RecordingEmail());
        using var client = app.ClientFor(app.Seed.CarolId);
        using var noToken = await client.PostAsync("/Profile?handler=CalendarInvites",
            new FormUrlEncodedContent([new("enabled", "false")]));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, noToken.StatusCode);
        foreach (var enabled in new[] { false, true })
        {
            var token = await FormHelpers.GetAntiforgeryTokenAsync(client, "/Profile");
            using var response = await FormHelpers.PostFormAsync(client, "/Profile?handler=CalendarInvites", token,
                ("enabled", enabled.ToString()), ("userId", app.Seed.AliceId));
            Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(enabled, app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.CarolId).CalendarInvitesEnabled));
            Assert.True(app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.AliceId).CalendarInvitesEnabled));
            var page = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/Profile"));
            Assert.Contains(enabled ? "Görevleriniz için takvim daveti gönderimi açıldı." : "Takviminizde zaten duran görevler yine güncel tutulur.", page);
        }
    }

    [Fact]
    public async Task Resend_for_unchanged_active_record_increments_sequence_and_sync_then_sends_nothing()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        await RunAsync(app, force: false);
        await RunAsync(app, force: true);
        Assert.Equal(2, mail.Sent.Count);
        Assert.All(mail.Sent, sent => Assert.Equal(CalendarMethod.Request, sent.Method));
        Assert.Equal(mail.Sent[0].Invite.Uid, mail.Sent[1].Invite.Uid);
        Assert.Equal(1, mail.Sent[1].Invite.Sequence);
        // Only the button's invite is marked as sent again (it gets its own subject, see the message test).
        Assert.Equal((false, true), (mail.Sent[0].Invite.Resent, mail.Sent[1].Invite.Resent));
        await RunAsync(app, force: false);
        Assert.Equal(2, mail.Sent.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Resend_bypasses_preference_and_past_due_date(bool enabled, bool past)
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        app.WithDb(db => { db.Users.Single(u => u.Id == app.Seed.CarolId).CalendarInvitesEnabled = enabled; return db.SaveChanges(); });
        if (past) ChangeTask(app, task => task.DueDate = new(2026, 11, 19));
        await RunAsync(app, force: false);
        Assert.Empty(mail.Sent);
        await RunAsync(app, force: true);
        Assert.Equal(CalendarMethod.Request, Assert.Single(mail.Sent).Method);
    }

    [Fact]
    public async Task Download_creates_stable_active_record_without_email_and_date_update_reuses_it()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        using var scope = app.Services.CreateScope();
        var calendar = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();
        var output = await calendar.BuildDownloadAsync(app.Seed.TaskA, app.Seed.CarolId);
        var record = app.WithDb(db => db.CalendarInvites.AsNoTracking().Single());
        Assert.NotNull(output);
        Assert.Contains("METHOD:PUBLISH\r\n", output);
        Assert.Contains("UID:" + record.Uid + "\r\n", output);
        Assert.DoesNotContain(output.Split("\r\n"), line => line.StartsWith("ATTENDEE", StringComparison.Ordinal));
        Assert.Equal((CalendarInviteStatus.Active, 0, false, Now), (record.Status, record.Sequence, record.SendPending, record.UpdatedAtUtc));
        Assert.Equal(output, await calendar.BuildDownloadAsync(app.Seed.TaskA, app.Seed.CarolId));
        Assert.Empty(mail.Sent);
        ChangeTask(app, task => task.DueDate = new(2026, 11, 25));
        await RunAsync(app, force: false);
        var update = Assert.Single(mail.Sent);
        Assert.Equal(CalendarMethod.Request, update.Method);
        Assert.Equal(record.Uid, update.Invite.Uid);
        Assert.Equal(1, update.Invite.Sequence);
        Assert.Equal(new DateOnly(2026, 11, 25), update.Invite.DueDate);
        Assert.Contains("https://briefyn.example/MyActions", update.Invite.Description);
    }

    [Theory]
    [InlineData("Complete")]
    [InlineData("Reopen")]
    public async Task My_actions_completion_changes_queue_exactly_one_sync_with_only_id(string handler)
    {
        using var app = await ReadyAsync(new RecordingEmail());
        if (handler == "Reopen") ChangeTask(app, task => task.SetDone(true, app.Seed.CarolId));
        using var client = app.ClientFor(app.Seed.CarolId);
        var token = await FormHelpers.GetAntiforgeryTokenAsync(client, "/Profile");
        using var response = await FormHelpers.PostFormAsync(client, "/MyActions?handler=" + handler, token, ("taskId", $"{app.Seed.TaskA}"));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        AssertOnlyJob(app, nameof(ICalendarSyncService.SyncAsync));
        Assert.Equal(handler == "Complete", app.WithDb(db => db.ActionItems.Single(a => a.Id == app.Seed.TaskA).IsDone));
    }

    [Fact]
    public async Task Send_to_calendar_queues_only_resend_id_then_sends_unchanged_invite()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        await RunAsync(app, force: false);
        using var client = app.ClientFor(app.Seed.CarolId);
        var token = await FormHelpers.GetAntiforgeryTokenAsync(client, "/Profile");
        using var response = await FormHelpers.PostFormAsync(client, "/MyActions?handler=SendToCalendar", token, ("taskId", $"{app.Seed.TaskA}"));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        AssertOnlyJob(app, nameof(ICalendarSyncService.ResendAsync));
        Assert.Contains("Takvim daveti e-postanıza gönderiliyor.", System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/MyActions")));
        await RunAsync(app, force: true);
        Assert.Equal(2, mail.Sent.Count);
        Assert.Equal(1, mail.Sent[1].Invite.Sequence);
        Assert.Equal(CalendarMethod.Request, mail.Sent[1].Method);
        await RunAsync(app, force: false);
        Assert.Equal(2, mail.Sent.Count);
    }

    [Theory]
    [InlineData("teammate", "SendToCalendar")]
    [InlineData("outsider", "SendToCalendar")]
    [InlineData("undated", "SendToCalendar")]
    [InlineData("done", "SendToCalendar")]
    [InlineData("teammate", "CalendarFile")]
    [InlineData("outsider", "CalendarFile")]
    [InlineData("undated", "CalendarFile")]
    [InlineData("done", "CalendarFile")]
    public async Task Calendar_handlers_reject_tasks_not_owned_open_and_dated(string scenario, string handler)
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        if (scenario == "undated") ChangeTask(app, task => task.DueDate = null);
        if (scenario == "done") ChangeTask(app, task => task.SetDone(true, app.Seed.CarolId));
        var caller = scenario == "teammate" ? app.Seed.AliceId : scenario == "outsider" ? app.Seed.BobId : app.Seed.CarolId;
        using var client = app.ClientFor(caller);
        var token = await FormHelpers.GetAntiforgeryTokenAsync(client, "/Profile");
        using var response = await FormHelpers.PostFormAsync(client, "/MyActions?handler=" + handler, token, ("taskId", $"{app.Seed.TaskA}"));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/MyActions", response.Headers.Location!.OriginalString);
        Assert.Contains("Bu görev size atanmış, açık ve son tarihli görünmüyor.", System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/MyActions")));
        Assert.Empty(app.Jobs.Created);
        Assert.Empty(app.WithDb(db => db.CalendarInvites.AsNoTracking().ToList()));
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task Calendar_file_returns_utf8_publish_with_same_uid_and_no_email()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        using var client = app.ClientFor(app.Seed.CarolId);
        var token = await FormHelpers.GetAntiforgeryTokenAsync(client, "/Profile");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await FormHelpers.PostFormAsync(client, "/MyActions?handler=CalendarFile", token, ("taskId", $"{app.Seed.TaskA}"));
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/calendar", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
            Assert.Equal($"briefyn-gorev-{app.Seed.TaskA}.ics", response.Content.Headers.ContentDisposition!.FileName);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
            var output = Encoding.UTF8.GetString(bytes);
            Assert.Contains("METHOD:PUBLISH\r\n", output);
            Assert.Contains("UID:" + app.WithDb(db => db.CalendarInvites.Single().Uid) + "\r\n", output);
            Assert.DoesNotContain(output.Split("\r\n"), line => line.StartsWith("ATTENDEE", StringComparison.Ordinal));
        }
        Assert.Empty(mail.Sent);
        Assert.Empty(app.Jobs.Created);
    }

    [Theory]
    [InlineData("SendToCalendar")]
    [InlineData("CalendarFile")]
    public async Task Calendar_handlers_require_antiforgery(string handler)
    {
        using var app = await ReadyAsync(new RecordingEmail());
        using var client = app.ClientFor(app.Seed.CarolId);
        using var response = await client.PostAsync("/MyActions?handler=" + handler,
            new FormUrlEncodedContent([new("taskId", $"{app.Seed.TaskA}")]));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(app.Jobs.Created);
        Assert.Empty(app.WithDb(db => db.CalendarInvites.AsNoTracking().ToList()));
    }

    [Fact]
    public async Task Resend_limit_is_per_user_and_does_not_limit_downloads()
    {
        using var app = await ReadyAsync(new RecordingEmail(), resendLimit: 2);
        using var client = app.ClientFor(app.Seed.CarolId);
        client.DefaultRequestHeaders.Referrer = new Uri("https://localhost/MyActions");
        var token = await FormHelpers.GetAntiforgeryTokenAsync(client, "/Profile");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await FormHelpers.PostFormAsync(client, "/MyActions?handler=sEnDtOcAlEnDaR", token, ("taskId", $"{app.Seed.TaskA}"));
            Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/MyActions", response.Headers.Location!.OriginalString);
        }
        Assert.Equal(2, app.Jobs.CountFor(nameof(ICalendarSyncService.ResendAsync)));
        var page = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/MyActions"));
        Assert.Contains("Bu saat içinde çok fazla takvim daveti istediniz. Lütfen biraz sonra tekrar deneyin.", page);
        using var download = await FormHelpers.PostFormAsync(client, "/MyActions?handler=CalendarFile", token, ("taskId", $"{app.Seed.TaskA}"));
        Assert.Equal(System.Net.HttpStatusCode.OK, download.StatusCode);
        ChangeTask(app, task => task.AssigneeUserId = app.Seed.AliceId);
        using var alice = app.ClientFor(app.Seed.AliceId);
        token = await FormHelpers.GetAntiforgeryTokenAsync(alice, "/Profile");
        using var ownBudget = await FormHelpers.PostFormAsync(alice, "/MyActions?handler=SendToCalendar", token, ("taskId", $"{app.Seed.TaskA}"));
        Assert.Equal(3, app.Jobs.CountFor(nameof(ICalendarSyncService.ResendAsync)));
    }

    [Fact]
    public async Task Profile_switch_off_blocks_automatic_invite_but_manual_send_still_works()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        using var client = app.ClientFor(app.Seed.CarolId);
        var token = await FormHelpers.GetAntiforgeryTokenAsync(client, "/Profile");
        using var setting = await FormHelpers.PostFormAsync(client, "/Profile?handler=CalendarInvites", token, ("enabled", "false"));
        await RunAsync(app, force: false);
        Assert.Empty(mail.Sent);
        using var response = await FormHelpers.PostFormAsync(client, "/MyActions?handler=SendToCalendar", token, ("taskId", $"{app.Seed.TaskA}"));
        AssertOnlyJob(app, nameof(ICalendarSyncService.ResendAsync));
        await RunAsync(app, force: true);
        Assert.Equal(CalendarMethod.Request, Assert.Single(mail.Sent).Method);
    }

    [Theory]
    [InlineData("Complete")]
    [InlineData("Reopen")]
    [InlineData("SendToCalendar")]
    public async Task Queue_failure_returns_a_redirect_and_keeps_saved_completion(string handler)
    {
        var logs = new CapturingLoggerProvider();
        using var app = await ReadyAsync(new RecordingEmail(), logs: logs);
        if (handler == "Reopen") ChangeTask(app, task => task.SetDone(true, app.Seed.CarolId));
        using var client = app.ClientFor(app.Seed.CarolId);
        var token = await FormHelpers.GetAntiforgeryTokenAsync(client, "/Profile");
        app.Jobs.FailNext = true;
        using var response = await FormHelpers.PostFormAsync(client, "/MyActions?handler=" + handler, token, ("taskId", $"{app.Seed.TaskA}"));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(app.Jobs.FailNext);
        Assert.Empty(app.Jobs.Created);
        if (handler == "SendToCalendar")
            Assert.Contains("Takvim daveti şu an gönderilemedi. Lütfen biraz sonra tekrar deneyin.", System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/MyActions")));
        else
            Assert.Equal(handler == "Complete", app.WithDb(db => db.ActionItems.Single(a => a.Id == app.Seed.TaskA).IsDone));
        Assert.Contains(logs.Lines, line => line.Contains($"Görev={app.Seed.TaskA}, HataTürü=InvalidOperationException", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains(SeedData.CarolEmail, StringComparison.Ordinal));
    }

    // A cancelled record moves past its cancellation instead (Resend_after_a_failed_cancellation_raises_the_sequence).
    [Theory]
    [InlineData(CalendarInviteStatus.Active)]
    public async Task Resend_retries_stale_pending_record_without_increasing_sequence(CalendarInviteStatus status)
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        AddRecord(app, status, pending: true, updated: Now.AddMinutes(-11));
        await RunAsync(app, force: true);
        var sent = Assert.Single(mail.Sent);
        Assert.Equal(CalendarMethod.Request, sent.Method);
        Assert.Equal(3, sent.Invite.Sequence);
        Assert.False(app.WithDb(db => db.CalendarInvites.Single().SendPending));
    }

    [Fact]
    public async Task Download_and_resend_do_not_modify_an_in_flight_record()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        AddRecord(app, CalendarInviteStatus.Cancelled, pending: true, updated: Now);
        ChangeTask(app, task => task.DueDate = new(2026, 11, 25));
        using var scope = app.Services.CreateScope();
        var calendar = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();
        Assert.NotNull(await calendar.BuildDownloadAsync(app.Seed.TaskA, app.Seed.CarolId));
        await RunAsync(app, force: true);
        var record = app.WithDb(db => db.CalendarInvites.AsNoTracking().Single());
        Assert.Equal((CalendarInviteStatus.Cancelled, true, 3, new DateOnly(2026, 11, 22), Now),
            (record.Status, record.SendPending, record.Sequence, record.DueDate, record.UpdatedAtUtc));
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task Download_reactivates_cancelled_record_with_same_uid_and_current_due_date()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        AddRecord(app, CalendarInviteStatus.Cancelled);
        ChangeTask(app, task => task.DueDate = new(2026, 11, 25));
        using var scope = app.Services.CreateScope();
        var output = await scope.ServiceProvider.GetRequiredService<ICalendarSyncService>().BuildDownloadAsync(app.Seed.TaskA, app.Seed.CarolId);
        Assert.Contains("UID:existing@briefyn.ai\r\n", output);
        var record = app.WithDb(db => db.CalendarInvites.AsNoTracking().Single());
        Assert.Equal(CalendarInviteStatus.Active, record.Status);
        Assert.Equal(new DateOnly(2026, 11, 25), record.DueDate);
        Assert.False(record.SendPending);
        Assert.Empty(mail.Sent);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("leaving")]
    [InlineData("email")]
    [InlineData("missing")]
    public async Task Calendar_eligibility_matches_sync_target_and_download_creates_no_record(string scenario)
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        app.WithDb(db =>
        {
            if (scenario == "left") db.WorkspaceMembers.Remove(db.WorkspaceMembers.Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.CarolId));
            if (scenario == "leaving") db.Users.Single(u => u.Id == app.Seed.CarolId).DeletionRequestedAtUtc = Now;
            if (scenario == "email") db.Users.Single(u => u.Id == app.Seed.CarolId).Email = "";
            if (scenario == "missing") db.ActionItems.Remove(db.ActionItems.Single(a => a.Id == app.Seed.TaskA));
            return db.SaveChanges();
        });
        using var scope = app.Services.CreateScope();
        var calendar = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();
        Assert.False(await calendar.IsOwnDatedTaskAsync(app.Seed.TaskA, app.Seed.CarolId));
        Assert.Null(await calendar.BuildDownloadAsync(app.Seed.TaskA, app.Seed.CarolId));
        await RunAsync(app, force: true);
        Assert.Empty(app.WithDb(db => db.CalendarInvites.AsNoTracking().ToList()));
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task Resend_after_task_becomes_ineligible_cancels_existing_invite()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        AddRecord(app, CalendarInviteStatus.Active);
        ChangeTask(app, task => task.SetDone(true, app.Seed.CarolId));
        await RunAsync(app, force: true);
        var sent = Assert.Single(mail.Sent);
        Assert.Equal(CalendarMethod.Cancel, sent.Method);
        Assert.Equal(4, sent.Invite.Sequence);
        Assert.Equal("", sent.Invite.TaskText);
        Assert.Equal(CalendarInviteStatus.Cancelled, app.WithDb(db => db.CalendarInvites.Single().Status));
    }

    [Fact]
    public async Task Download_unique_insert_race_reloads_winning_uid()
    {
        var writes = new DownloadWriteInterceptor();
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail, interceptor: writes);
        writes.BeforeWrite = () => { writes.BeforeWrite = null; AddRecord(app, CalendarInviteStatus.Active); };
        using var scope = app.Services.CreateScope();
        var output = await scope.ServiceProvider.GetRequiredService<ICalendarSyncService>().BuildDownloadAsync(app.Seed.TaskA, app.Seed.CarolId);
        Assert.Contains("UID:existing@briefyn.ai\r\n", output);
        Assert.Equal(1, writes.Attempts);
        Assert.Equal("existing@briefyn.ai", app.WithDb(db => db.CalendarInvites.Single().Uid));
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task Download_retries_a_database_failure_only_once()
    {
        var writes = new DownloadWriteInterceptor { BeforeWrite = () => throw new DbUpdateException("retryable failure") };
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail, interceptor: writes);
        using var scope = app.Services.CreateScope();
        var calendar = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();
        await Assert.ThrowsAsync<DbUpdateException>(() => calendar.BuildDownloadAsync(app.Seed.TaskA, app.Seed.CarolId));
        Assert.Equal(2, writes.Attempts);
        Assert.Empty(app.WithDb(db => db.CalendarInvites.AsNoTracking().ToList()));
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task Download_concurrent_update_reloads_and_leaves_in_flight_winner_unchanged()
    {
        var writes = new DownloadWriteInterceptor();
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail, interceptor: writes);
        AddRecord(app, CalendarInviteStatus.Active);
        ChangeTask(app, task => task.DueDate = new(2026, 11, 25));
        writes.BeforeWrite = () =>
        {
            writes.BeforeWrite = null;
            app.WithDb(db =>
            {
                var winner = db.CalendarInvites.Single();
                winner.SendPending = true; winner.UpdatedAtUtc = Now;
                return db.SaveChanges();
            });
        };
        using var scope = app.Services.CreateScope();
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<ICalendarSyncService>().BuildDownloadAsync(app.Seed.TaskA, app.Seed.CarolId));
        var record = app.WithDb(db => db.CalendarInvites.AsNoTracking().Single());
        Assert.Equal((true, Now, new DateOnly(2026, 11, 22)), (record.SendPending, record.UpdatedAtUtc, record.DueDate));
        Assert.Equal(1, writes.Attempts);
        Assert.Empty(mail.Sent);
    }

    private sealed class DownloadWriteInterceptor : DbCommandInterceptor
    {
        public Action? BeforeWrite { get; set; }
        public int Attempts { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO \"CalendarInvites\"", StringComparison.Ordinal)
                || command.CommandText.StartsWith("UPDATE \"CalendarInvites\"", StringComparison.Ordinal))
            {
                Attempts++;
                BeforeWrite?.Invoke();
            }
            return ValueTask.FromResult(result);
        }
    }

    private static void AddRecord(BriefynFactory app, CalendarInviteStatus status, bool pending = false, DateTime? updated = null) => app.WithDb(db =>
    {
        db.CalendarInvites.Add(new CalendarInvite
        {
            ActionItemId = app.Seed.TaskA,
            UserId = app.Seed.CarolId,
            Uid = "existing@briefyn.ai",
            Status = status,
            Sequence = 3,
            DueDate = new(2026, 11, 22),
            SendPending = pending,
            UpdatedAtUtc = updated ?? Now
        });
        return db.SaveChanges();
    });

    // A cancellation that may have arrived must be outranked: the resent invite and the downloaded file both carry a
    // higher sequence than the cancellation that was being sent.
    [Fact]
    public async Task Resend_after_a_failed_cancellation_raises_the_sequence()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        AddRecord(app, CalendarInviteStatus.Cancelled, pending: true, updated: DateTime.MinValue);
        await RunAsync(app, force: true);
        var sent = Assert.Single(mail.Sent);
        Assert.Equal((CalendarMethod.Request, 4), (sent.Method, sent.Invite.Sequence));
    }

    [Fact]
    public async Task Download_of_a_cancelled_entry_raises_the_sequence()
    {
        var mail = new RecordingEmail(); using var app = await ReadyAsync(mail);
        AddRecord(app, CalendarInviteStatus.Cancelled);
        using var scope = app.Services.CreateScope();
        var file = await scope.ServiceProvider.GetRequiredService<ICalendarSyncService>().BuildDownloadAsync(app.Seed.TaskA, app.Seed.CarolId);
        Assert.Contains("SEQUENCE:4\r\n", file);
        Assert.Empty(mail.Sent);
    }

    private static void AssertOnlyJob(BriefynFactory app, string method)
    {
        var job = Assert.Single(app.Jobs.Created);
        Assert.Equal(typeof(ICalendarSyncService), job.Type);
        Assert.Equal(method, job.Method.Name);
        Assert.Equal(app.Seed.TaskA, Assert.IsType<int>(Assert.Single(job.Args)));
    }

    private static async Task RunAsync(BriefynFactory app, bool force)
    {
        using var scope = app.Services.CreateScope();
        var calendar = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();
        if (force) await calendar.ResendAsync(app.Seed.TaskA);
        else await calendar.SyncAsync(app.Seed.TaskA);
    }

    private static async Task<BriefynFactory> ReadyAsync(RecordingEmail mail, int resendLimit = 5, CapturingLoggerProvider? logs = null, DbCommandInterceptor? interceptor = null)
    {
        var app = new BriefynFactory { EmailServiceOverride = mail, TimeProviderOverride = new FixedTimeProvider(), LogCapture = logs, CommandInterceptor = interceptor };
        app.ExtraSettings["EmailSettings:CalendarResendLimitPerHour"] = resendLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        app.ExtraSettings["EmailSettings:SenderEmail"] = "sender@briefyn.test";
        app.ExtraSettings["App:PublicUrl"] = "https://briefyn.example/";
        await app.InitializeAsync();
        ChangeTask(app, task => { task.AssigneeUserId = app.Seed.CarolId; task.DueDate = new(2026, 11, 22); });
        return app;
    }

    private static void ChangeTask(BriefynFactory app, Action<ActionItem> change) => app.WithDb(db =>
    {
        change(db.ActionItems.Single(a => a.Id == app.Seed.TaskA));
        return db.SaveChanges();
    });

    private sealed class FixedTimeProvider : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }

    private sealed class RecordingEmail : IEmailService
    {
        public List<(CalendarInviteData Invite, CalendarMethod Method)> Sent { get; } = [];
        public Task SendMeetingSummaryAsync(int meetingId, string protectedRecipient) => Task.CompletedTask;
        public Task SendSeriesAgendaAsync(int seriesId, string protectedRecipient) => Task.CompletedTask;
        public Task SendOverdueReminderAsync(string toEmail, string fullName, IReadOnlyList<OverdueTask> tasks) => Task.CompletedTask;
        public Task SendCalendarInviteAsync(CalendarInviteData invite, CalendarMethod method)
        {
            Sent.Add((invite, method));
            return Task.CompletedTask;
        }
    }
}
