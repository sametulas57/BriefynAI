using System.Net;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features
{
    public class MyActionsFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();

        public async Task InitializeAsync()
        {
            await Factory.InitializeAsync();
            Factory.WithDb(db =>
            {
                db.UserClaims.Add(new Microsoft.AspNetCore.Identity.IdentityUserClaim<string>
                    { UserId = Factory.Seed.CarolId, ClaimType = "FullName", ClaimValue = "Carol Demir" });
                return db.SaveChanges();
            });
        }

        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // Ulaş's plain request: assign an action to a teammate with a date; "Bana atananlar" shows which team, which
    // meeting and which task; marking it done shows, in the archived summary, that it is done and who did it.
    public class MyActionsTests : IClassFixture<MyActionsFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public MyActionsTests(MyActionsFixture fixture) => _app = fixture.Factory;

        private int NewMeetingWithTask(string taskText, string? assignee = null, DateOnly? due = null) => _app.WithDb(db =>
        {
            var m = new MeetingSummary
            {
                WorkspaceId = S.WorkspaceA, ToplantiKonusu = "Sprint planlama", Status = MeetingStatus.Completed,
                AksiyonMaddeleri = new List<ActionItem> { new() { Text = taskText, AssigneeUserId = assignee, DueDate = due } }
            };
            db.MeetingSummaries.Add(m);
            db.SaveChanges();
            return m.AksiyonMaddeleri[0].Id;
        });

        private ActionItem Task(int id) => _app.WithDb(db => db.Set<ActionItem>().AsNoTracking().Single(a => a.Id == id));

        private static async Task<string> TextAsync(HttpClient client, string url) => WebUtility.HtmlDecode(await client.GetStringAsync(url));

        private async Task AssignAsync(HttpClient client, int taskId, string meetingUrl, string? assignee, string? due)
        {
            var token = await GetAntiforgeryTokenAsync(client, meetingUrl);
            await PostFormAsync(client, $"/Details?handler=AssignAction", token,
                ("taskId", $"{taskId}"), ("assigneeUserId", assignee ?? ""), ("dueDate", due ?? ""));
        }

        [Fact]
        public async Task Task_can_be_assigned_to_a_teammate_with_a_date()
        {
            var id = NewMeetingWithTask("Sunucu maliyetini çıkar");
            var meetingId = Task(id).MeetingSummaryId;

            await AssignAsync(_app.ClientFor(S.AliceId), id, $"/Details?id={meetingId}", S.CarolId, "2026-11-20");

            Assert.Equal(S.CarolId, Task(id).AssigneeUserId);
            Assert.Equal(new DateOnly(2026, 11, 20), Task(id).DueDate);
        }

        [Fact]
        public async Task Only_teammates_can_be_assigned_and_outsiders_cannot_assign()
        {
            var id = NewMeetingWithTask("Ekip dışına verilmemeli");

            await AssignAsync(_app.ClientFor(S.AliceId), id, $"/Details?id={Task(id).MeetingSummaryId}", S.BobId, "2026-11-20");
            await AssignAsync(_app.ClientFor(S.BobId), id, $"/Details?id={S.MeetingB}", S.BobId, "2026-11-20");

            Assert.Null(Task(id).AssigneeUserId);
        }

        [Fact]
        public async Task My_actions_page_shows_team_meeting_and_task_only_to_the_assignee()
        {
            NewMeetingWithTask("Tasarım dosyalarını paylaş", S.CarolId, new DateOnly(2026, 11, 21));

            var carol = await TextAsync(_app.ClientFor(S.CarolId), "/MyActions");
            var bob = await TextAsync(_app.ClientFor(S.BobId), "/MyActions");

            Assert.Contains("Tasarım dosyalarını paylaş", carol);
            Assert.Contains("Sprint planlama", carol);   // meeting
            Assert.Contains("A Şirketi", carol);         // team
            Assert.DoesNotContain("Tasarım dosyalarını paylaş", bob);
        }

        [Fact]
        public async Task Completing_shows_done_and_who_did_it_in_the_meeting_summary()
        {
            var id = NewMeetingWithTask("Raporu gönder", S.CarolId, new DateOnly(2026, 11, 22));
            var carol = _app.ClientFor(S.CarolId);
            var token = await GetAntiforgeryTokenAsync(carol, "/MyActions");

            await PostFormAsync(carol, "/MyActions?handler=Complete", token, ("taskId", $"{id}"));

            var task = Task(id);
            Assert.True(task.IsDone);
            Assert.Equal(S.CarolId, task.CompletedByUserId);
            var summary = await TextAsync(_app.ClientFor(S.AliceId), $"/Details?id={task.MeetingSummaryId}");
            Assert.Contains("Tamamlayan: Carol Demir", summary);
        }

        [Fact]
        public async Task Ticking_the_box_in_the_summary_also_records_who_completed_it()
        {
            var id = NewMeetingWithTask("Kutudan işaretlenen iş");
            var carol = _app.ClientFor(S.CarolId);
            var token = await GetAntiforgeryTokenAsync(carol, $"/Details?id={Task(id).MeetingSummaryId}");

            await PostFormAsync(carol, $"/Details?handler=ToggleTask&taskId={id}", token);

            Assert.True(Task(id).IsDone);
            Assert.Equal(S.CarolId, Task(id).CompletedByUserId);
        }

        [Fact]
        public async Task Outsider_cannot_complete_someone_elses_task()
        {
            var id = NewMeetingWithTask("Başkasının işi", S.CarolId, new DateOnly(2026, 11, 23));
            var bob = _app.ClientFor(S.BobId);
            var token = await GetAntiforgeryTokenAsync(bob, $"/Details?id={S.MeetingB}");

            await PostFormAsync(bob, "/MyActions?handler=Complete", token, ("taskId", $"{id}"));

            Assert.False(Task(id).IsDone);
        }

        [Fact]
        public async Task Regenerated_summary_does_not_drop_assigned_tasks()
        {
            var id = NewMeetingWithTask("Atanmış yapay zekâ işi", S.CarolId, new DateOnly(2026, 11, 24));
            var meetingId = Task(id).MeetingSummaryId;
            _app.WithDb(db => { db.MeetingSummaries.Single(m => m.Id == meetingId).Transkript = "döküm"; return db.SaveChanges(); });

            using (var scope = _app.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<IMeetingProcessor>().ResummarizeMeetingJob(meetingId);

            Assert.Equal(S.CarolId, Task(id).AssigneeUserId);
        }

        // Found in review (2026-10-18): an unassigned, undated task that was ticked done (e.g. before
        // "who completed it" was recorded) vanished when the summary was regenerated, losing the done state.
        [Fact]
        public async Task Regenerated_summary_does_not_drop_completed_unassigned_tasks()
        {
            var id = NewMeetingWithTask("Bitmiş ama atanmamış iş");
            var meetingId = Task(id).MeetingSummaryId;
            _app.WithDb(db =>
            {
                db.MeetingSummaries.Single(m => m.Id == meetingId).Transkript = "döküm";
                db.Set<ActionItem>().Single(a => a.Id == id).IsDone = true;
                return db.SaveChanges();
            });

            using (var scope = _app.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<IMeetingProcessor>().ResummarizeMeetingJob(meetingId);

            var task = _app.WithDb(db => db.Set<ActionItem>().AsNoTracking().SingleOrDefault(a => a.Id == id));
            Assert.NotNull(task);
            Assert.True(task!.IsDone);
        }
    }
}
