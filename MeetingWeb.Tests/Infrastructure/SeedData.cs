using MeetingWeb.Data;
using MeetingWeb.Models;

namespace MeetingWeb.Tests.Infrastructure
{
    // Two tenants: Alice owns workspace A (Carol is a member), Bob owns workspace B.
    // Each workspace has one meeting with one action item and one comment.
    public record SeedData(
        string AliceId, string BobId, string CarolId,
        int WorkspaceA, int WorkspaceB,
        int MeetingA, int MeetingB,
        int TaskA, int TaskB,
        int CommentA, int CommentB)
    {
        public const string AliceEmail = "alice@a.test";
        public const string BobEmail = "bob@b.test";
        public const string CarolEmail = "carol@a.test";

        public static async Task<SeedData> CreateAsync(ApplicationDbContext db)
        {
            var alice = User(AliceEmail);
            var bob = User(BobEmail);
            var carol = User(CarolEmail);
            db.Users.AddRange(alice, bob, carol);

            var wsA = new Workspace { Name = "A Şirketi" };
            var wsB = new Workspace { Name = "B Şirketi" };
            db.Workspaces.AddRange(wsA, wsB);
            await db.SaveChangesAsync();

            db.WorkspaceMembers.AddRange(
                new WorkspaceMember { WorkspaceId = wsA.Id, UserId = alice.Id, Role = WorkspaceRole.Owner },
                new WorkspaceMember { WorkspaceId = wsA.Id, UserId = carol.Id, Role = WorkspaceRole.Member },
                new WorkspaceMember { WorkspaceId = wsB.Id, UserId = bob.Id, Role = WorkspaceRole.Owner });

            var meetingA = Meeting(wsA.Id, "A gizli toplantı");
            var meetingB = Meeting(wsB.Id, "B gizli toplantı");
            db.Meetings.AddRange(meetingA, meetingB);
            await db.SaveChangesAsync();

            var commentA = new Comment { MeetingId = meetingA.Id, UserId = alice.Id, Content = "A yorumu" };
            var commentB = new Comment { MeetingId = meetingB.Id, UserId = bob.Id, Content = "B yorumu" };
            db.Comments.AddRange(commentA, commentB);
            await db.SaveChangesAsync();

            return new SeedData(alice.Id, bob.Id, carol.Id, wsA.Id, wsB.Id, meetingA.Id, meetingB.Id,
                meetingA.ActionItems[0].Id, meetingB.ActionItems[0].Id, commentA.Id, commentB.Id);
        }

        private static ApplicationUser User(string email) => new()
        {
            FullName = "", // nameless, like accounts before profiles had names; tests set one when they need it
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString()
        };

        private static Meeting Meeting(int workspaceId, string topic) => new()
        {
            WorkspaceId = workspaceId,
            Title = topic,
            Status = MeetingStatus.Failed,
            AudioFileName = "test.m4a",
            Points = new List<MeetingPoint>
            {
                new() { Kind = MeetingPointKind.Topic, Text = "Konu" },
                new() { Kind = MeetingPointKind.Decision, Text = "Karar" }
            },
            ActionItems = new List<ActionItem> { new() { Text = "Görev" } }
        };
    }
}
