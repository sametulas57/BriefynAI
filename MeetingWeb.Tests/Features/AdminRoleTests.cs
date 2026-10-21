using System.Net;
using MeetingWeb.Models;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features
{
    public class AdminRoleFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // PRD iç proje notu, criteria 1–3 and the admin's share of title management.
    // Each test builds its own team, so tests do not depend on each other's changes.
    public class AdminRoleTests : IClassFixture<AdminRoleFixture>
    {
        private readonly BriefynFactory _app;

        public AdminRoleTests(AdminRoleFixture fixture) => _app = fixture.Factory;

        private record Team(int Id, string Owner, string Admin, string Admin2, string Member);

        private Team NewTeam() => _app.WithDb(db =>
        {
            ApplicationUser User(string name)
            {
                var email = $"{name}-{Guid.NewGuid():N}@t.test";
                var u = new ApplicationUser { FullName = name, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString() };
                db.Users.Add(u);
                return u;
            }
            var (owner, admin, admin2, member) = (User("Kurucu"), User("Yönetici"), User("Yönetici2"), User("Üye"));
            var team = new Workspace { Name = "Rol ekibi", CreatedByUserId = owner.Id };
            team.Members.Add(new WorkspaceMember { UserId = owner.Id, Role = WorkspaceRole.Owner });
            team.Members.Add(new WorkspaceMember { UserId = admin.Id, Role = WorkspaceRole.Admin });
            team.Members.Add(new WorkspaceMember { UserId = admin2.Id, Role = WorkspaceRole.Admin });
            team.Members.Add(new WorkspaceMember { UserId = member.Id, Role = WorkspaceRole.Member });
            db.Workspaces.Add(team);
            db.SaveChanges();
            return new Team(team.Id, owner.Id, admin.Id, admin2.Id, member.Id);
        });

        // Puts the team into the user's session, like opening it from the team switcher.
        private async Task<(HttpClient Client, string Token)> InTeamAsync(string userId, int teamId)
        {
            var client = _app.ClientFor(userId);
            await client.GetAsync($"/Workspace/Switch?workspaceId={teamId}&returnUrl=/Workspace/Members");
            return (client, await GetAntiforgeryTokenAsync(client, "/Profile"));
        }

        private Task<HttpResponseMessage> MembersPostAsync(HttpClient client, string token, string handler, params (string, string)[] fields) =>
            PostFormAsync(client, $"/Workspace/Members?handler={handler}", token, fields);

        private WorkspaceRole? RoleOf(int teamId, string userId) => _app.WithDb(db =>
            db.WorkspaceMembers.AsNoTracking().Where(m => m.WorkspaceId == teamId && m.UserId == userId).Select(m => (WorkspaceRole?)m.Role).SingleOrDefault());

        private int InvitationCount(int teamId) => _app.WithDb(db => db.WorkspaceInvitations.Count(i => i.WorkspaceId == teamId));

        // Criterion 1
        [Fact]
        public async Task Admin_can_create_an_invitation_but_a_member_cannot()
        {
            var t = NewTeam();
            var (admin, adminToken) = await InTeamAsync(t.Admin, t.Id);
            var (member, memberToken) = await InTeamAsync(t.Member, t.Id);

            await PostFormAsync(member, "/Workspace/Invite", memberToken);
            Assert.Equal(0, InvitationCount(t.Id));

            var page = WebUtility.HtmlDecode(await (await PostFormAsync(admin, "/Workspace/Invite", adminToken)).Content.ReadAsStringAsync());
            Assert.Equal(1, InvitationCount(t.Id));
            Assert.Contains("/Workspace/Join?token=", page);
        }

        // Found in review: an invitation stays usable only while its creator still runs the team.
        [Fact]
        public async Task Invitation_of_an_admin_who_was_demoted_no_longer_works()
        {
            var t = NewTeam();
            var (admin, adminToken) = await InTeamAsync(t.Admin, t.Id);
            var page = WebUtility.HtmlDecode(await (await PostFormAsync(admin, "/Workspace/Invite", adminToken)).Content.ReadAsStringAsync());
            var inviteToken = System.Text.RegularExpressions.Regex.Match(page, "Join\\?token=([A-Za-z0-9_-]+)").Groups[1].Value;
            Assert.NotEmpty(inviteToken);

            var (owner, ownerToken) = await InTeamAsync(t.Owner, t.Id);
            await MembersPostAsync(owner, ownerToken, "SetRole", ("targetUserId", t.Admin), ("makeAdmin", "false"));

            var bob = _app.ClientFor(_app.Seed.BobId);
            var bobToken = await GetAntiforgeryTokenAsync(bob, "/Profile");
            await PostFormAsync(bob, "/Workspace/Join", bobToken, ("Token", inviteToken));

            Assert.Null(RoleOf(t.Id, _app.Seed.BobId));
        }

        // Criterion 2
        [Fact]
        public async Task Admin_removes_members_but_not_the_founder_or_another_admin()
        {
            var t = NewTeam();
            var (admin, token) = await InTeamAsync(t.Admin, t.Id);

            await MembersPostAsync(admin, token, "RemoveMember", ("targetUserId", t.Owner));
            await MembersPostAsync(admin, token, "RemoveMember", ("targetUserId", t.Admin2));
            Assert.Equal(WorkspaceRole.Owner, RoleOf(t.Id, t.Owner));
            Assert.Equal(WorkspaceRole.Admin, RoleOf(t.Id, t.Admin2));

            await MembersPostAsync(admin, token, "RemoveMember", ("targetUserId", t.Member));
            Assert.Null(RoleOf(t.Id, t.Member));
        }

        [Fact]
        public async Task Founder_can_remove_an_admin()
        {
            var t = NewTeam();
            var (owner, token) = await InTeamAsync(t.Owner, t.Id);

            await MembersPostAsync(owner, token, "RemoveMember", ("targetUserId", t.Admin));

            Assert.Null(RoleOf(t.Id, t.Admin));
        }

        // Criterion 3
        [Fact]
        public async Task Only_the_founder_makes_or_unmakes_admins()
        {
            var t = NewTeam();
            var (admin, adminToken) = await InTeamAsync(t.Admin, t.Id);
            var (member, memberToken) = await InTeamAsync(t.Member, t.Id);
            var (owner, ownerToken) = await InTeamAsync(t.Owner, t.Id);

            await MembersPostAsync(admin, adminToken, "SetRole", ("targetUserId", t.Member), ("makeAdmin", "true"));
            await MembersPostAsync(member, memberToken, "SetRole", ("targetUserId", t.Member), ("makeAdmin", "true"));
            await MembersPostAsync(admin, adminToken, "SetRole", ("targetUserId", t.Admin2), ("makeAdmin", "false"));
            Assert.Equal(WorkspaceRole.Member, RoleOf(t.Id, t.Member));
            Assert.Equal(WorkspaceRole.Admin, RoleOf(t.Id, t.Admin2));

            await MembersPostAsync(owner, ownerToken, "SetRole", ("targetUserId", t.Member), ("makeAdmin", "true"));
            await MembersPostAsync(owner, ownerToken, "SetRole", ("targetUserId", t.Admin2), ("makeAdmin", "false"));
            Assert.Equal(WorkspaceRole.Admin, RoleOf(t.Id, t.Member));
            Assert.Equal(WorkspaceRole.Member, RoleOf(t.Id, t.Admin2));

            // The founder's own role is never changed this way.
            await MembersPostAsync(owner, ownerToken, "SetRole", ("targetUserId", t.Owner), ("makeAdmin", "false"));
            Assert.Equal(WorkspaceRole.Owner, RoleOf(t.Id, t.Owner));
        }

        // Criterion 4, admin side
        [Fact]
        public async Task Admin_manages_and_assigns_titles()
        {
            var t = NewTeam();
            var (admin, token) = await InTeamAsync(t.Admin, t.Id);

            var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/Workspace/Members"));
            Assert.Contains("Yeni unvan", page);

            await MembersPostAsync(admin, token, "CreateTitle", ("titleName", "Operasyon"), ("titleIcon", "gear"));
            var titleId = _app.WithDb(db => db.WorkspaceTitles.Single(x => x.WorkspaceId == t.Id && x.Name == "Operasyon").Id);
            await MembersPostAsync(admin, token, "AssignTitle", ("targetUserId", t.Member), ("titleId", $"{titleId}"));

            Assert.Equal(titleId, _app.WithDb(db => db.WorkspaceMembers.Single(m => m.WorkspaceId == t.Id && m.UserId == t.Member).TitleId));
        }

        [Fact]
        public async Task Admin_can_leave_the_team()
        {
            var t = NewTeam();
            var (admin, token) = await InTeamAsync(t.Admin, t.Id);

            await MembersPostAsync(admin, token, "LeaveWorkspace");

            Assert.Null(RoleOf(t.Id, t.Admin));
        }

        [Fact]
        public async Task Members_page_shows_the_admin_badge_and_founder_controls()
        {
            var t = NewTeam();
            var (owner, _) = await InTeamAsync(t.Owner, t.Id);

            var page = WebUtility.HtmlDecode(await owner.GetStringAsync("/Workspace/Members"));

            Assert.Contains("Yönetici", page);
            Assert.Contains("Yönetici Yap", page);
            Assert.Contains("Üye Yap", page);
        }
    }
}
