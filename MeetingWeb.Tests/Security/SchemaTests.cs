using System.Net;
using System.Text.RegularExpressions;
using MeetingWeb.Models;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Security
{
    public class SchemaFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // ADR-0009: rules that must hold even when two requests race live in the database, invitation tokens are stored
    // only as a hash, and deleting a team removes everything under it.
    public class SchemaTests : IClassFixture<SchemaFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public SchemaTests(SchemaFixture fixture) => _app = fixture.Factory;

        private void SaveFails(Action<MeetingWeb.Data.ApplicationDbContext> change) =>
            Assert.Throws<DbUpdateException>(() => _app.WithDb(db => { change(db); return db.SaveChanges(); }));

        [Fact]
        public async Task Invitation_token_is_stored_only_as_a_hash_and_works_once()
        {
            var alice = _app.ClientFor(S.AliceId);
            var token = await GetAntiforgeryTokenAsync(alice, "/Workspace/Invite");
            var page = WebUtility.HtmlDecode(await (await PostFormAsync(alice, "/Workspace/Invite", token)).Content.ReadAsStringAsync());
            var inviteToken = Regex.Match(page, "Join\\?token=([A-Za-z0-9_-]+)").Groups[1].Value;

            Assert.True(inviteToken.Length >= 40);
            var stored = _app.WithDb(db => db.WorkspaceInvitations.AsNoTracking().Single(i => i.TokenHash == WorkspaceInvitation.HashOf(inviteToken)));
            Assert.NotEqual(inviteToken, stored.TokenHash);
            Assert.Equal(S.AliceId, stored.CreatedByUserId);

            var bob = _app.ClientFor(S.BobId);
            var bobToken = await GetAntiforgeryTokenAsync(bob, $"/Workspace/Join?token={inviteToken}");
            await PostFormAsync(bob, "/Workspace/Join", bobToken, ("Token", inviteToken));

            Assert.True(_app.WithDb(db => db.WorkspaceMembers.Any(m => m.WorkspaceId == S.WorkspaceA && m.UserId == S.BobId && m.Role == WorkspaceRole.Member)));
            var used = _app.WithDb(db => db.WorkspaceInvitations.AsNoTracking().Single(i => i.Id == stored.Id));
            Assert.Equal(S.BobId, used.UsedByUserId);
            Assert.NotNull(used.UsedAtUtc);

            var again = WebUtility.HtmlDecode(await bob.GetStringAsync($"/Workspace/Join?token={inviteToken}"));
            Assert.Contains("daha önce kullanılmış", again);
        }

        [Fact]
        public void One_reaction_per_user_per_comment()
        {
            _app.WithDb(db => { db.CommentReactions.Add(new CommentReaction { CommentId = S.CommentB, UserId = S.BobId, IsLike = true }); return db.SaveChanges(); });

            SaveFails(db => db.CommentReactions.Add(new CommentReaction { CommentId = S.CommentB, UserId = S.BobId, IsLike = false }));
        }

        [Fact]
        public void One_personal_space_per_user()
        {
            _app.WithDb(db => { db.Workspaces.Add(new Workspace { Name = "Kişisel Alanım", IsPersonal = true, CreatedByUserId = S.CarolId }); return db.SaveChanges(); });

            SaveFails(db => db.Workspaces.Add(new Workspace { Name = "İkinci kişisel", IsPersonal = true, CreatedByUserId = S.CarolId }));
            // Teams are not limited.
            _app.WithDb(db => { db.Workspaces.Add(new Workspace { Name = "Ekip", CreatedByUserId = S.CarolId }); return db.SaveChanges(); });
        }

        [Fact]
        public void A_phone_number_belongs_to_one_account()
        {
            _app.WithDb(db => { db.Users.Single(u => u.Id == S.AliceId).PhoneNumber = "5550000001"; return db.SaveChanges(); });

            SaveFails(db => db.Users.Single(u => u.Id == S.CarolId).PhoneNumber = "5550000001");
        }

        [Fact]
        public void Role_must_be_a_known_value()
        {
            SaveFails(db => db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = S.WorkspaceB, UserId = S.CarolId, Role = (WorkspaceRole)7 }));
        }

        [Fact]
        public void Deleting_a_team_removes_everything_under_it()
        {
            var (teamId, meetingId, commentId) = _app.WithDb(db =>
            {
                var team = new Workspace { Name = "Silinecek ekip", CreatedByUserId = S.BobId };
                team.Members.Add(new WorkspaceMember { UserId = S.BobId, Role = WorkspaceRole.Owner });
                var meeting = new Meeting
                {
                    Workspace = team, Title = "silinecek", Status = MeetingStatus.Completed,
                    Transcript = new MeetingTranscript { Text = "döküm" },
                    Points = new List<MeetingPoint> { new() { Kind = MeetingPointKind.Topic, Text = "konu" } },
                    ActionItems = new List<ActionItem> { new() { Text = "görev" } }
                };
                db.Meetings.Add(meeting);
                db.SaveChanges();
                var comment = new Comment { MeetingId = meeting.Id, UserId = S.BobId, Content = "yorum" };
                comment.Replies.Add(new Comment { MeetingId = meeting.Id, UserId = S.BobId, Content = "yanıt" });
                comment.Reactions.Add(new CommentReaction { UserId = S.BobId, IsLike = true });
                db.Comments.Add(comment);
                db.SaveChanges();
                return (team.Id, meeting.Id, comment.Id);
            });

            _app.WithDb(db => db.Database.ExecuteSqlRaw("DELETE FROM \"Workspaces\" WHERE \"Id\" = {0}", teamId));

            _app.WithDb(db =>
            {
                Assert.False(db.Meetings.Any(m => m.Id == meetingId));
                Assert.False(db.MeetingTranscripts.Any(t => t.MeetingId == meetingId));
                Assert.False(db.MeetingPoints.Any(p => p.MeetingId == meetingId));
                Assert.False(db.ActionItems.Any(a => a.MeetingId == meetingId));
                Assert.False(db.Comments.Any(c => c.MeetingId == meetingId));
                Assert.False(db.CommentReactions.Any(r => r.CommentId == commentId));
                Assert.False(db.WorkspaceMembers.Any(m => m.WorkspaceId == teamId));
                return true;
            });
        }
    }
}
