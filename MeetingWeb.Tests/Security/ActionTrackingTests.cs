using System.Net;
using System.Security.Claims;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Security
{
    public class ActionTrackingFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();

        public async Task InitializeAsync()
        {
            await Factory.InitializeAsync();
            // Carol's display name lets the page suggest her for AI lines such as "Carol: ...".
            Factory.WithDb(db =>
            {
                db.UserClaims.Add(new Microsoft.AspNetCore.Identity.IdentityUserClaim<string>
                    { UserId = Factory.Seed.CarolId, ClaimType = "FullName", ClaimValue = "Carol Demir" });
                return db.SaveChanges();
            });
        }

        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // Aksiyon takibi: A (Alice) assigns a task to B (Carol, same team) → Carol sees it under "Bana atananlar" →
    // a reminder goes out the day before the due date (Turkish time) → Carol marks it done → it leaves her list.
    // C (Bob, another team) can do none of this.
    public class ActionTrackingTests : IClassFixture<ActionTrackingFixture>
    {
        private static readonly TimeZoneInfo Turkey = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public ActionTrackingTests(ActionTrackingFixture fixture) => _app = fixture.Factory;

        private static DateOnly TurkeyToday() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Turkey));

        private int NewTask(string text = "Sözleşmeyi netleştir", string? assignee = null, DateOnly? due = null, bool done = false) => _app.WithDb(db =>
        {
            var task = new ActionItem { MeetingSummaryId = S.MeetingA, Text = text, AssigneeUserId = assignee, DueDate = due, IsDone = done };
            db.Set<ActionItem>().Add(task);
            db.SaveChanges();
            return task.Id;
        });

        private ActionItem Task(int id) => _app.WithDb(db => db.Set<ActionItem>().AsNoTracking().Single(a => a.Id == id));

        private async Task<HttpResponseMessage> AssignAsync(string asUser, int taskId, string? assignee, DateOnly? due, HttpClient? client = null)
        {
            // The confirmation travels in a one-time cookie, so callers that read the next page pass their own client.
            client ??= _app.ClientFor(asUser);
            var token = await GetAntiforgeryTokenAsync(client, asUser == S.BobId ? $"/Details?id={S.MeetingB}" : $"/Details?id={S.MeetingA}");
            return await PostFormAsync(client, $"/Details?id={S.MeetingA}&handler=AssignAction", token,
                ("taskId", $"{taskId}"), ("assigneeUserId", assignee ?? ""), ("dueDate", due?.ToString("yyyy-MM-dd") ?? ""));
        }

        private static async Task<string> TextAsync(HttpClient client, string url) => WebUtility.HtmlDecode(await client.GetStringAsync(url));

        private int Reminders(int taskId) => _app.Jobs.CountFor("SendActionReminderAsync", a => (int)a[0]! == taskId);

        private async Task<int> RunRemindersAsync(DateTimeOffset nowUtc)
        {
            using var scope = _app.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IActionReminderService>().SendDueRemindersAsync(nowUtc);
        }

        // --- Assigning ---

        [Fact]
        public async Task Teammate_can_assign_a_task_with_a_due_date()
        {
            var id = NewTask();
            var due = TurkeyToday().AddDays(3);

            await AssignAsync(S.AliceId, id, S.CarolId, due);

            Assert.Equal(S.CarolId, Task(id).AssigneeUserId);
            Assert.Equal(due, Task(id).DueDate);
        }

        [Fact]
        public async Task Assignee_must_belong_to_the_meetings_team()
        {
            var id = NewTask();
            var client = _app.ClientFor(S.AliceId);

            var res = await AssignAsync(S.AliceId, id, S.BobId, TurkeyToday().AddDays(3), client);

            Assert.Null(Task(id).AssigneeUserId);
            var page = await TextAsync(client, res.Headers.Location!.ToString());
            Assert.Contains("yalnız bu ekibin üyelerinden", page);
        }

        [Fact]
        public async Task Due_date_cannot_be_in_the_past()
        {
            var id = NewTask();
            var client = _app.ClientFor(S.AliceId);

            var res = await AssignAsync(S.AliceId, id, S.CarolId, TurkeyToday().AddDays(-1), client);

            Assert.Null(Task(id).DueDate);
            Assert.Contains("bugünden önce olamaz", await TextAsync(client, res.Headers.Location!.ToString()));
        }

        [Fact]
        public async Task Outsider_cannot_assign_another_teams_task()
        {
            var id = NewTask();

            var res = await AssignAsync(S.BobId, id, S.BobId, TurkeyToday().AddDays(3));

            Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
            Assert.Equal("/History", res.Headers.Location!.OriginalString);
            Assert.Null(Task(id).AssigneeUserId);
        }

        [Fact]
        public async Task Ai_line_naming_a_teammate_suggests_that_teammate()
        {
            // The action list is shown for analysed (completed) meetings.
            var meetingId = _app.WithDb(db =>
            {
                var m = new MeetingSummary
                {
                    WorkspaceId = S.WorkspaceA, ToplantiKonusu = "öneri testi", Status = MeetingStatus.Completed,
                    AksiyonMaddeleri = new List<ActionItem> { new() { Text = "Carol: tasarım dosyalarını paylaş (Cuma)" } }
                };
                db.MeetingSummaries.Add(m);
                db.SaveChanges();
                return m.Id;
            });

            var page = await TextAsync(_app.ClientFor(S.AliceId), $"/Details?id={meetingId}");

            Assert.Contains("Carol Demir (önerilen)", page);
        }

        // --- "Bana atananlar" ---

        [Fact]
        public async Task Assignee_sees_open_tasks_from_all_teams_and_others_do_not()
        {
            var id = NewTask("Bütçe tablosunu güncelle", S.CarolId, TurkeyToday().AddDays(2));

            var carol = await TextAsync(_app.ClientFor(S.CarolId), "/AssignedToMe");
            var bob = await TextAsync(_app.ClientFor(S.BobId), "/AssignedToMe");
            var alice = await TextAsync(_app.ClientFor(S.AliceId), "/AssignedToMe");

            Assert.Contains("Bütçe tablosunu güncelle", carol);
            Assert.Contains("A gizli toplantı", carol);       // where the task comes from
            Assert.DoesNotContain("Bütçe tablosunu güncelle", bob);
            Assert.DoesNotContain("Bütçe tablosunu güncelle", alice); // not assigned to her
        }

        [Fact]
        public async Task Completing_from_the_list_removes_it_and_outsiders_cannot_complete()
        {
            var id = NewTask("Raporu gönder", S.CarolId, TurkeyToday().AddDays(1));
            var bob = _app.ClientFor(S.BobId);
            var bobToken = await GetAntiforgeryTokenAsync(bob, $"/Details?id={S.MeetingB}"); // his list is empty, so no form there
            await PostFormAsync(bob, "/AssignedToMe?handler=Complete", bobToken, ("taskId", $"{id}"));
            Assert.False(Task(id).IsDone);

            var carol = _app.ClientFor(S.CarolId);
            var token = await GetAntiforgeryTokenAsync(carol, "/AssignedToMe");
            await PostFormAsync(carol, "/AssignedToMe?handler=Complete", token, ("taskId", $"{id}"));

            Assert.True(Task(id).IsDone);
            Assert.NotNull(Task(id).CompletedAtUtc);
            Assert.DoesNotContain("Raporu gönder", await TextAsync(carol, "/AssignedToMe"));
        }

        [Fact]
        public async Task Removed_member_no_longer_sees_the_teams_tasks()
        {
            // Dave is in team A only for this test.
            var daveId = _app.WithDb(db =>
            {
                var dave = new Microsoft.AspNetCore.Identity.IdentityUser { UserName = "dave@a.test", Email = "dave@a.test", SecurityStamp = "s" };
                db.Users.Add(dave);
                db.WorkspaceUsers.Add(new WorkspaceUser { WorkspaceId = S.WorkspaceA, UserId = dave.Id, Role = "Üye" });
                db.SaveChanges();
                return dave.Id;
            });
            NewTask("Dave'in işi", daveId, TurkeyToday().AddDays(2));
            Assert.Contains("Dave'in işi", await TextAsync(_app.ClientFor(daveId), "/AssignedToMe"));

            _app.WithDb(db => { db.WorkspaceUsers.Remove(db.WorkspaceUsers.Single(w => w.UserId == daveId)); return db.SaveChanges(); });

            Assert.DoesNotContain("Dave'in işi", await TextAsync(_app.ClientFor(daveId), "/AssignedToMe"));
        }

        // --- Reminders ---

        [Fact]
        public async Task Reminder_goes_once_the_day_before_in_turkish_time()
        {
            // 21:30 UTC on 17 Oct is already 18 Oct in Türkiye, so "tomorrow" there is 19 Oct.
            var now = new DateTimeOffset(2026, 10, 17, 21, 30, 0, TimeSpan.Zero);
            var dueTomorrow = NewTask("Yarın teslim", S.CarolId, new DateOnly(2026, 10, 19));
            var dueLater = NewTask("Sonra teslim", S.CarolId, new DateOnly(2026, 10, 25));

            await RunRemindersAsync(now);
            await RunRemindersAsync(now.AddHours(1)); // the job runs hourly; it must not send twice

            Assert.Equal(1, Reminders(dueTomorrow));
            Assert.Equal(0, Reminders(dueLater));
        }

        [Fact]
        public async Task Task_assigned_late_in_the_day_still_gets_its_reminder()
        {
            var now = new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeSpan.Zero); // 18:00 in Türkiye
            var id = NewTask("Akşam atanan", S.CarolId, new DateOnly(2026, 11, 3));

            await RunRemindersAsync(now);

            Assert.Equal(1, Reminders(id));
        }

        [Fact]
        public async Task Changing_the_due_date_or_assignee_earns_a_new_reminder()
        {
            var now = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
            var id = NewTask("Tarihi kayan iş", S.CarolId, new DateOnly(2026, 12, 2));
            await RunRemindersAsync(now);

            _app.WithDb(db => { db.Set<ActionItem>().Single(a => a.Id == id).DueDate = new DateOnly(2026, 12, 3); return db.SaveChanges(); });
            await RunRemindersAsync(now.AddDays(1));

            Assert.Equal(2, Reminders(id));
        }

        [Fact]
        public async Task No_reminder_for_done_tasks_or_assignees_who_left_the_team()
        {
            var now = new DateTimeOffset(2027, 1, 5, 9, 0, 0, TimeSpan.Zero);
            var done = NewTask("Bitmiş iş", S.CarolId, new DateOnly(2027, 1, 6), done: true);
            var outsider = NewTask("Ekip dışına atanmış", S.BobId, new DateOnly(2027, 1, 6)); // Bob is not in team A

            await RunRemindersAsync(now);

            Assert.Equal(0, Reminders(done));
            Assert.Equal(0, Reminders(outsider));
        }

        // --- Regenerated summary ---

        [Fact]
        public async Task Regenerated_summary_keeps_assigned_ai_tasks()
        {
            // Its own meeting: regenerating rewrites the title, which other tests in this class rely on.
            var (meetingId, id) = _app.WithDb(db =>
            {
                var m = new MeetingSummary
                {
                    WorkspaceId = S.WorkspaceA, ToplantiKonusu = "yenilenecek", Transkript = "kayıtlı döküm",
                    AksiyonMaddeleri = new List<ActionItem>
                    {
                        new() { Text = "Yapay zekânın bulduğu iş", AssigneeUserId = S.CarolId, DueDate = TurkeyToday().AddDays(4) },
                        new() { Text = "Atanmamış eski iş" }
                    }
                };
                db.MeetingSummaries.Add(m);
                db.SaveChanges();
                return (m.Id, m.AksiyonMaddeleri[0].Id);
            });

            using (var scope = _app.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<IMeetingProcessor>().ResummarizeMeetingJob(meetingId);

            Assert.False(_app.WithDb(db => db.Set<ActionItem>().AsEnumerable().Any(a => a.MeetingSummaryId == meetingId && a.Text == "Atanmamış eski iş")));

            var kept = _app.WithDb(db => db.Set<ActionItem>().AsNoTracking().SingleOrDefault(a => a.Id == id));
            Assert.NotNull(kept);
            Assert.Equal(S.CarolId, kept!.AssigneeUserId);
        }
    }
}
