using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.AspNetCore.Identity;

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

            db.WorkspaceUsers.AddRange(
                new WorkspaceUser { WorkspaceId = wsA.Id, UserId = alice.Id, Role = "Sahip" },
                new WorkspaceUser { WorkspaceId = wsA.Id, UserId = carol.Id, Role = "Üye" },
                new WorkspaceUser { WorkspaceId = wsB.Id, UserId = bob.Id, Role = "Sahip" });

            var meetingA = Meeting(wsA.Id, "A gizli toplantı");
            var meetingB = Meeting(wsB.Id, "B gizli toplantı");
            db.MeetingSummaries.AddRange(meetingA, meetingB);
            await db.SaveChangesAsync();

            var commentA = new MeetingComment { MeetingId = meetingA.Id, UserId = alice.Id, Content = "A yorumu" };
            var commentB = new MeetingComment { MeetingId = meetingB.Id, UserId = bob.Id, Content = "B yorumu" };
            db.MeetingComments.AddRange(commentA, commentB);
            await db.SaveChangesAsync();

            return new SeedData(alice.Id, bob.Id, carol.Id, wsA.Id, wsB.Id, meetingA.Id, meetingB.Id,
                meetingA.AksiyonMaddeleri[0].Id, meetingB.AksiyonMaddeleri[0].Id, commentA.Id, commentB.Id);
        }

        private static IdentityUser User(string email) => new()
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString()
        };

        private static MeetingSummary Meeting(int workspaceId, string topic) => new()
        {
            WorkspaceId = workspaceId,
            ToplantiKonusu = topic,
            Status = MeetingStatus.Failed,
            AudioFilePath = "/uploads/test.m4a",
            GorusulenKonular = new List<string> { "Konu" },
            AlinanKararlar = new List<string> { "Karar" },
            AksiyonMaddeleri = new List<ActionItem> { new() { Text = "Görev", IsDone = false } }
        };
    }
}
