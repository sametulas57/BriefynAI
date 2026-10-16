using System.Net;
using MeetingWeb.Models;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Security
{
    public class MeetingAccessFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // S1 (IDOR): a user may read or change a meeting only if they belong to the meeting's workspace.
    // Bob (workspace B) attacks Alice's meeting (workspace A) by guessing ids.
    public class MeetingAccessTests : IClassFixture<MeetingAccessFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public MeetingAccessTests(MeetingAccessFixture fixture) => _app = fixture.Factory;

        private async Task<(HttpClient Client, string Token)> BobAsync()
        {
            var bob = _app.ClientFor(S.BobId);
            return (bob, await GetAntiforgeryTokenAsync(bob, $"/Details?id={S.MeetingB}"));
        }

        private MeetingSummary MeetingA() => _app.WithDb(db => db.MeetingSummaries
            .Include(m => m.AksiyonMaddeleri).AsNoTracking().Single(m => m.Id == S.MeetingA));

        // --- Reading ---

        [Fact]
        public async Task Owner_can_open_own_meeting()
        {
            var res = await _app.ClientFor(S.AliceId).GetAsync($"/Details?id={S.MeetingA}");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        [Fact]
        public async Task Workspace_member_can_open_teammates_meeting()
        {
            var res = await _app.ClientFor(S.CarolId).GetAsync($"/Details?id={S.MeetingA}");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        [Fact]
        public async Task Outsider_gets_404_for_another_workspaces_meeting()
        {
            var res = await _app.ClientFor(S.BobId).GetAsync($"/Details?id={S.MeetingA}");
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.DoesNotContain("A gizli toplantı", await res.Content.ReadAsStringAsync());
        }

        // --- Writing ---

        [Fact]
        public async Task Outsider_cannot_add_topic_decision_or_action_item()
        {
            var (bob, token) = await BobAsync();
            var before = MeetingA();

            var url = $"/Details?id={S.MeetingA}";
            var topic = await PostFormAsync(bob, url + "&handler=AddTopic", token, ("meetingId", $"{S.MeetingA}"), ("topic", "bob-konu"));
            var decision = await PostFormAsync(bob, url + "&handler=AddDecision", token, ("meetingId", $"{S.MeetingA}"), ("decision", "bob-karar"));
            var action = await PostFormAsync(bob, url + "&handler=AddActionItem", token, ("meetingId", $"{S.MeetingA}"), ("actionText", "bob-gorev"));

            Assert.All(new[] { topic, decision, action }, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
            var after = MeetingA();
            Assert.Equal(before.GorusulenKonular, after.GorusulenKonular);
            Assert.Equal(before.AlinanKararlar, after.AlinanKararlar);
            Assert.Equal(before.AksiyonMaddeleri.Count, after.AksiyonMaddeleri.Count);
        }

        [Fact]
        public async Task Outsider_cannot_toggle_someone_elses_task()
        {
            var (bob, token) = await BobAsync();

            var res = await PostFormAsync(bob, $"/Details?handler=ToggleTask&taskId={S.TaskA}", token);

            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.False(_app.WithDb(db => db.Set<ActionItem>().AsNoTracking().Single(a => a.Id == S.TaskA).IsDone));
        }

        [Fact]
        public async Task Outsider_cannot_comment_or_react_on_another_workspaces_meeting()
        {
            var (bob, token) = await BobAsync();

            var comment = await PostFormAsync(bob, $"/Details?id={S.MeetingA}&handler=AddComment", token,
                ("meetingId", $"{S.MeetingA}"), ("content", "bob-yorum"));
            var reaction = await PostFormAsync(bob, $"/Details?handler=ToggleReaction", token,
                ("commentId", $"{S.CommentA}"), ("isLike", "true"));

            Assert.Equal(HttpStatusCode.NotFound, comment.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, reaction.StatusCode);
            Assert.False(_app.WithDb(db => db.MeetingComments.Any(c => c.Content == "bob-yorum")));
            Assert.False(_app.WithDb(db => db.CommentReactions.Any(r => r.CommentId == S.CommentA && r.UserId == S.BobId)));
        }

        [Fact]
        public async Task Reply_cannot_point_at_a_comment_from_another_meeting()
        {
            // Bob comments on his own meeting but sets parentCommentId to Alice's comment.
            var (bob, token) = await BobAsync();

            var res = await PostFormAsync(bob, $"/Details?id={S.MeetingB}&handler=AddComment", token,
                ("meetingId", $"{S.MeetingB}"), ("content", "bob-sizma-yaniti"), ("parentCommentId", $"{S.CommentA}"));

            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.False(_app.WithDb(db => db.MeetingComments.Any(c => c.Content == "bob-sizma-yaniti")));
        }

        [Fact]
        public async Task Outsider_cannot_retry_processing_of_another_workspaces_meeting()
        {
            var (bob, token) = await BobAsync();

            var res = await PostFormAsync(bob, $"/Details?id={S.MeetingA}&handler=RetryProcessing", token, ("id", $"{S.MeetingA}"));

            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.Equal(MeetingStatus.Failed, MeetingA().Status);
            Assert.Equal(0, _app.Jobs.CountFor("ProcessMeetingJob", a => (int)a[0]! == S.MeetingA));
        }

        [Fact]
        public async Task Outsider_cannot_email_another_workspaces_meeting()
        {
            var (bob, token) = await BobAsync();

            var res = await PostFormAsync(bob, $"/Details?id={S.MeetingA}&handler=Share", token,
                ("meetingId", $"{S.MeetingA}"), ("selectedEmails", "attacker@evil.test"));

            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            // The job queue is shared with the other tests in this class, so match on the attacker's address.
            Assert.Equal(0, _app.Jobs.CountFor("SendMeetingSummaryAsync", a => (string)a[1]! == "attacker@evil.test"));
        }

        [Fact]
        public async Task Share_sends_only_to_members_of_the_meetings_workspace()
        {
            var alice = _app.ClientFor(S.AliceId);
            var token = await GetAntiforgeryTokenAsync(alice, $"/Details?id={S.MeetingA}");

            await PostFormAsync(alice, $"/Details?id={S.MeetingA}&handler=Share", token,
                ("meetingId", $"{S.MeetingA}"), ("selectedEmails", SeedData.CarolEmail), ("selectedEmails", "outsider@evil.test"));

            Assert.Equal(1, _app.Jobs.CountFor("SendMeetingSummaryAsync", a => (int)a[0]! == S.MeetingA && (string)a[1]! == SeedData.CarolEmail));
            Assert.Equal(0, _app.Jobs.CountFor("SendMeetingSummaryAsync", a => (string)a[1]! == "outsider@evil.test"));
        }
    }
}
