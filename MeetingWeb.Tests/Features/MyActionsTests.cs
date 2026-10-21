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
                db.Users.Single(u => u.Id == Factory.Seed.CarolId).FullName = "Carol Demir";
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
            var m = new Meeting
            {
                WorkspaceId = S.WorkspaceA, Title = "Sprint planlama", Status = MeetingStatus.Completed,
                ActionItems = new List<ActionItem> { new() { Text = taskText, AssigneeUserId = assignee, DueDate = due } }
            };
            db.Meetings.Add(m);
            db.SaveChanges();
            return m.ActionItems[0].Id;
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
            var meetingId = Task(id).MeetingId;

            await AssignAsync(_app.ClientFor(S.AliceId), id, $"/Details?id={meetingId}", S.CarolId, "2026-11-20");

            Assert.Equal(S.CarolId, Task(id).AssigneeUserId);
            Assert.Equal(new DateOnly(2026, 11, 20), Task(id).DueDate);
        }

        [Fact]
        public async Task Only_teammates_can_be_assigned_and_outsiders_cannot_assign()
        {
            var id = NewMeetingWithTask("Ekip dışına verilmemeli");

            await AssignAsync(_app.ClientFor(S.AliceId), id, $"/Details?id={Task(id).MeetingId}", S.BobId, "2026-11-20");
            await AssignAsync(_app.ClientFor(S.BobId), id, $"/Details?id={S.MeetingB}", S.BobId, "2026-11-20");

            Assert.Null(Task(id).AssigneeUserId);
        }

        // PRD 2026-10-21-ikon-secici-atama-onay-paneli: saving one task's assignment must not reload the page, so a choice
        // made in another row stays. The page's script asks for a short answer; a plain form post still redirects.
        [Fact]
        public async Task Assigning_from_the_page_answers_without_a_reload()
        {
            var id = NewMeetingWithTask("Sayfa yenilenmeden atanan iş");
            var url = $"/Details?id={Task(id).MeetingId}";
            var alice = _app.ClientFor(S.AliceId);
            var token = await GetAntiforgeryTokenAsync(alice, url);

            var json = await PostFormForJsonAsync(alice, "/Details?handler=AssignAction", token,
                ("taskId", $"{id}"), ("assigneeUserId", S.CarolId), ("dueDate", "2026-11-20"));

            Assert.True(json.GetProperty("success").GetBoolean());
            Assert.Equal("Carol Demir", json.GetProperty("assignee").GetString());
            Assert.Equal("20 Kasım 2026", json.GetProperty("dueDate").GetString());
            Assert.Equal(S.CarolId, Task(id).AssigneeUserId);
        }

        [Fact]
        public async Task Assigning_an_outsider_from_the_page_is_refused_with_a_plain_message()
        {
            var id = NewMeetingWithTask("Ekip dışına sayfa içinden verilmemeli");
            var url = $"/Details?id={Task(id).MeetingId}";
            var alice = _app.ClientFor(S.AliceId);
            var token = await GetAntiforgeryTokenAsync(alice, url);

            var json = await PostFormForJsonAsync(alice, "/Details?handler=AssignAction", token,
                ("taskId", $"{id}"), ("assigneeUserId", S.BobId), ("dueDate", ""));

            Assert.False(json.GetProperty("success").GetBoolean());
            Assert.Contains("ekibindeki", json.GetProperty("message").GetString());
            Assert.Null(Task(id).AssigneeUserId);
        }

        // A choice not yet saved must not come back after a reload (some browsers refill forms).
        [Fact]
        public async Task Assignment_forms_are_not_refilled_by_the_browser()
        {
            var id = NewMeetingWithTask("Taslak tutulmayan iş");
            var page = await TextAsync(_app.ClientFor(S.AliceId), $"/Details?id={Task(id).MeetingId}");

            var forms = System.Text.RegularExpressions.Regex.Matches(page, "<form[^>]*handler=AssignAction[^>]*>");
            Assert.NotEmpty(forms);
            Assert.All(forms, f => Assert.Contains("autocomplete=\"off\"", f.Value));
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
            var summary = await TextAsync(_app.ClientFor(S.AliceId), $"/Details?id={task.MeetingId}");
            Assert.Contains("Tamamlayan: Carol Demir", summary);
        }

        [Fact]
        public async Task Ticking_the_box_in_the_summary_also_records_who_completed_it()
        {
            var id = NewMeetingWithTask("Kutudan işaretlenen iş");
            var carol = _app.ClientFor(S.CarolId);
            var token = await GetAntiforgeryTokenAsync(carol, $"/Details?id={Task(id).MeetingId}");

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
            var meetingId = Task(id).MeetingId;
            _app.WithDb(db => { db.MeetingTranscripts.Add(new MeetingTranscript { MeetingId = meetingId, Text = "döküm" }); return db.SaveChanges(); });

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
            var meetingId = Task(id).MeetingId;
            _app.WithDb(db =>
            {
                db.MeetingTranscripts.Add(new MeetingTranscript { MeetingId = meetingId, Text = "döküm" });
                db.Set<ActionItem>().Single(a => a.Id == id).CompletedAtUtc = DateTime.UtcNow;
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
