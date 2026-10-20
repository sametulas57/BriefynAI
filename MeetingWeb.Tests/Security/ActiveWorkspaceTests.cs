using MeetingWeb.Models;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Security
{
    public class ActiveWorkspaceFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // S7: the session's ActiveWorkspaceId is only a remembered choice; it must be re-checked against membership on every use.
    public class ActiveWorkspaceTests : IClassFixture<ActiveWorkspaceFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public ActiveWorkspaceTests(ActiveWorkspaceFixture fixture) => _app = fixture.Factory;

        private static async Task UploadAsync(HttpClient client, string fileName)
        {
            var token = await GetAntiforgeryTokenAsync(client, "/Upload");
            using var form = new MultipartFormDataContent
            {
                { new ByteArrayContent(new byte[] { 1, 2, 3 }), "UploadFile", fileName },
                { new StringContent(token), "__RequestVerificationToken" }
            };
            await client.PostAsync("/Upload", form);
        }

        // Title is encrypted with a random nonce (S4), so it cannot be compared in SQL; filter after decryption.
        private int? WorkspaceOfUpload(string fileName) => _app.WithDb(db => db.Meetings.AsNoTracking()
            .AsEnumerable().Where(m => m.Title == fileName).Select(m => (int?)m.WorkspaceId).SingleOrDefault());

        [Fact]
        public async Task Removed_member_cannot_keep_uploading_into_the_old_workspace()
        {
            // Dave joins workspace A, opens a page (session remembers A), then gets removed from the team.
            var daveId = _app.WithDb(db =>
            {
                var dave = new ApplicationUser { FullName = "Dave", UserName = "dave@a.test", Email = "dave@a.test", SecurityStamp = "s" };
                db.Users.Add(dave);
                db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = S.WorkspaceA, UserId = dave.Id, Role = WorkspaceRole.Member });
                db.SaveChanges();
                return dave.Id;
            });
            var dave = _app.ClientFor(daveId);
            await dave.GetAsync("/History");
            var token = await GetAntiforgeryTokenAsync(dave, "/Upload"); // fetched while still a member

            _app.WithDb(db =>
            {
                db.WorkspaceMembers.Remove(db.WorkspaceMembers.Single(wu => wu.UserId == daveId));
                return db.SaveChanges();
            });

            using var form = new MultipartFormDataContent
            {
                { new ByteArrayContent(new byte[] { 1, 2, 3 }), "UploadFile", "dave-sizinti.m4a" },
                { new StringContent(token), "__RequestVerificationToken" }
            };
            await dave.PostAsync("/Upload", form);

            Assert.Null(WorkspaceOfUpload("dave-sizinti.m4a"));
        }

        [Fact]
        public async Task Next_user_on_the_same_browser_does_not_inherit_the_previous_users_workspace()
        {
            // One browser (one cookie jar): Alice leaves workspace A in the session, then Bob signs in.
            var browser = _app.ClientFor(S.AliceId);
            await browser.GetAsync("/History");
            browser.DefaultRequestHeaders.Remove(TestAuthHandler.UserHeader);
            browser.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, S.BobId);

            await UploadAsync(browser, "bob-kaydi.m4a");

            Assert.Equal(S.WorkspaceB, WorkspaceOfUpload("bob-kaydi.m4a"));
        }

        [Fact]
        public async Task History_lists_only_the_validated_workspace()
        {
            var browser = _app.ClientFor(S.AliceId);
            await browser.GetAsync("/History");
            browser.DefaultRequestHeaders.Remove(TestAuthHandler.UserHeader);
            browser.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, S.BobId);

            var html = await browser.GetStringAsync("/History");

            Assert.DoesNotContain("A gizli toplantı", html);
        }
    }
}
