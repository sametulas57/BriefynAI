using System.Net;
using System.Text.RegularExpressions;
using MeetingWeb.Models;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features
{
    public class TitlesFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // PRD iç proje notu (titles and grouped sharing; the admin role comes later, so for now
    // only the founder manages titles). Titles give no permissions; they label and group people.
    public class TitlesTests : IClassFixture<TitlesFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public TitlesTests(TitlesFixture fixture) => _app = fixture.Factory;

        // Opening the team page puts the user's team in the session, like a real visit before a form post. The token comes
        // from the profile page, which always has a form (a member's team page may have none); tokens are not page-bound.
        private async Task<(HttpClient Client, string Token)> OnTeamPageAsync(string userId)
        {
            var client = _app.ClientFor(userId);
            await client.GetAsync("/Workspace/Members");
            return (client, await GetAntiforgeryTokenAsync(client, "/Profile"));
        }

        private Task<HttpResponseMessage> PostAsync(HttpClient client, string token, string handler, params (string, string)[] fields) =>
            PostFormAsync(client, $"/Workspace/Members?handler={handler}", token, fields);

        private List<WorkspaceTitle> TitlesOf(int workspaceId) =>
            _app.WithDb(db => db.WorkspaceTitles.AsNoTracking().Where(t => t.WorkspaceId == workspaceId).ToList());

        private int? TitleOf(int workspaceId, string userId) =>
            _app.WithDb(db => db.WorkspaceMembers.AsNoTracking().Single(m => m.WorkspaceId == workspaceId && m.UserId == userId).TitleId);

        private int CreateTitle(int workspaceId, string name) => _app.WithDb(db =>
        {
            var title = new WorkspaceTitle { WorkspaceId = workspaceId, Name = name };
            db.WorkspaceTitles.Add(title);
            db.SaveChanges();
            return title.Id;
        });

        // Criterion 4 (founder side)
        [Fact]
        public async Task Founder_creates_renames_and_deletes_a_title()
        {
            var (alice, token) = await OnTeamPageAsync(S.AliceId);

            await PostAsync(alice, token, "CreateTitle", ("titleName", "Yazılım"), ("titleIcon", "laptop"));
            var title = Assert.Single(TitlesOf(S.WorkspaceA), t => t.Name == "Yazılım");
            Assert.Equal("laptop", title.Icon);

            await PostAsync(alice, token, "UpdateTitle", ("titleId", $"{title.Id}"), ("titleName", "Yazılım Ekibi"), ("titleIcon", "cpu"));
            var renamed = Assert.Single(TitlesOf(S.WorkspaceA), t => t.Id == title.Id);
            Assert.Equal(("Yazılım Ekibi", "cpu"), (renamed.Name, renamed.Icon));

            await PostAsync(alice, token, "DeleteTitle", ("titleId", $"{title.Id}"));
            Assert.DoesNotContain(TitlesOf(S.WorkspaceA), t => t.Id == title.Id);
        }

        // Criterion 4 (member side)
        [Fact]
        public async Task Member_cannot_manage_titles()
        {
            var existing = CreateTitle(S.WorkspaceA, "Pazarlama");
            var (carol, token) = await OnTeamPageAsync(S.CarolId);

            var page = WebUtility.HtmlDecode(await carol.GetStringAsync("/Workspace/Members"));
            Assert.DoesNotContain("Yeni unvan", page);

            await PostAsync(carol, token, "CreateTitle", ("titleName", "Carol unvanı"), ("titleIcon", ""));
            await PostAsync(carol, token, "UpdateTitle", ("titleId", $"{existing}"), ("titleName", "Değişti"), ("titleIcon", ""));
            await PostAsync(carol, token, "AssignTitle", ("targetUserId", S.CarolId), ("titleId", $"{existing}"));
            await PostAsync(carol, token, "DeleteTitle", ("titleId", $"{existing}"));

            Assert.DoesNotContain(TitlesOf(S.WorkspaceA), t => t.Name == "Carol unvanı");
            Assert.Contains(TitlesOf(S.WorkspaceA), t => t.Id == existing && t.Name == "Pazarlama");
            Assert.NotEqual(existing, TitleOf(S.WorkspaceA, S.CarolId));
        }

        [Fact]
        public async Task Member_actions_are_inside_the_dropdown_for_founder_and_hidden_for_member()
        {
            var founderPage = WebUtility.HtmlDecode(await _app.ClientFor(S.AliceId).GetStringAsync("/Workspace/Members"));
            var menuStart = founderPage.IndexOf("member-actions-menu", StringComparison.Ordinal);
            Assert.True(menuStart >= 0, "founder has an actions menu");
            var menu = founderPage[menuStart..];
            Assert.Contains("TransferOwnership", menu);
            Assert.Contains("SetRole", menu);
            Assert.Contains("RemoveMember", menu);
            Assert.Contains("Diğer işlemler", founderPage);

            var memberPage = WebUtility.HtmlDecode(await _app.ClientFor(S.CarolId).GetStringAsync("/Workspace/Members"));
            Assert.DoesNotContain("aria-label=\"Diğer işlemler\"", memberPage);
        }

        // Criterion 5
        [Fact]
        public async Task Title_names_are_unique_within_a_team_only()
        {
            var (alice, aliceToken) = await OnTeamPageAsync(S.AliceId);
            var (bob, bobToken) = await OnTeamPageAsync(S.BobId);

            await PostAsync(alice, aliceToken, "CreateTitle", ("titleName", "Satış"), ("titleIcon", ""));
            var second = await PostAsync(alice, aliceToken, "CreateTitle", ("titleName", "Satış"), ("titleIcon", ""));
            await PostAsync(bob, bobToken, "CreateTitle", ("titleName", "Satış"), ("titleIcon", ""));

            Assert.Single(TitlesOf(S.WorkspaceA), t => t.Name == "Satış");
            Assert.Single(TitlesOf(S.WorkspaceB), t => t.Name == "Satış");
            var page = WebUtility.HtmlDecode(await alice.GetStringAsync(second.Headers.Location!.ToString()));
            Assert.Contains("Bu adda bir unvan zaten var", page);
        }

        // Criterion 6
        [Fact]
        public async Task Deleting_a_title_clears_it_from_its_people_who_stay_in_the_team()
        {
            var title = CreateTitle(S.WorkspaceA, "Hukuk");
            var (alice, token) = await OnTeamPageAsync(S.AliceId);
            await PostAsync(alice, token, "AssignTitle", ("targetUserId", S.CarolId), ("titleId", $"{title}"));
            Assert.Equal(title, TitleOf(S.WorkspaceA, S.CarolId));

            await PostAsync(alice, token, "DeleteTitle", ("titleId", $"{title}"));

            Assert.Null(TitleOf(S.WorkspaceA, S.CarolId));
        }

        // Criterion 7
        [Fact]
        public async Task Another_teams_title_and_unknown_icons_are_refused()
        {
            var foreign = CreateTitle(S.WorkspaceB, "B'nin unvanı");
            var (alice, token) = await OnTeamPageAsync(S.AliceId);

            await PostAsync(alice, token, "AssignTitle", ("targetUserId", S.CarolId), ("titleId", $"{foreign}"));
            await PostAsync(alice, token, "CreateTitle", ("titleName", "Garip ikon"), ("titleIcon", "javascript:alert(1)"));

            Assert.NotEqual(foreign, TitleOf(S.WorkspaceA, S.CarolId));
            Assert.DoesNotContain(TitlesOf(S.WorkspaceA), t => t.Name == "Garip ikon");
        }

        // A founder cannot reach into another team either (ADR-0005): Bob's session is team B.
        [Fact]
        public async Task Founder_cannot_change_titles_of_another_team()
        {
            var title = CreateTitle(S.WorkspaceA, "A'nın unvanı");
            var (bob, token) = await OnTeamPageAsync(S.BobId);

            await PostAsync(bob, token, "UpdateTitle", ("titleId", $"{title}"), ("titleName", "Bob değiştirdi"), ("titleIcon", ""));
            await PostAsync(bob, token, "DeleteTitle", ("titleId", $"{title}"));
            await PostAsync(bob, token, "AssignTitle", ("targetUserId", S.CarolId), ("titleId", $"{title}"));

            Assert.Contains(TitlesOf(S.WorkspaceA), t => t.Id == title && t.Name == "A'nın unvanı");
            Assert.NotEqual(title, TitleOf(S.WorkspaceA, S.CarolId));
        }

        // Criterion 8
        [Fact]
        public async Task Share_dialog_groups_people_by_title_and_selects_nobody()
        {
            var title = CreateTitle(S.WorkspaceA, "Tasarım");
            _app.WithDb(db =>
            {
                db.WorkspaceMembers.Single(m => m.WorkspaceId == S.WorkspaceA && m.UserId == S.CarolId).TitleId = title;
                return db.SaveChanges();
            });

            var page = WebUtility.HtmlDecode(await _app.ClientFor(S.AliceId).GetStringAsync($"/Details?id={S.MeetingA}"));

            var tasarim = page.IndexOf("Tasarım", StringComparison.Ordinal);
            var unvansiz = page.IndexOf("Unvansız", StringComparison.Ordinal);
            Assert.True(tasarim >= 0 && unvansiz > tasarim, "groups: titled first, then 'Unvansız'");
            Assert.True(page.IndexOf(SeedData.CarolEmail, tasarim, StringComparison.Ordinal) < unvansiz, "Carol is under her title");
            Assert.True(page.IndexOf(SeedData.AliceEmail, unvansiz, StringComparison.Ordinal) > unvansiz, "Alice has no title");
            Assert.Contains("Tümünü seç", page);
            var recipientBoxes = Regex.Matches(page, "<input[^>]*name=\"selectedEmails\"[^>]*>");
            Assert.Equal(2, recipientBoxes.Count);
            Assert.All(recipientBoxes, m => Assert.DoesNotContain("checked", m.Value));
        }

        // Criterion 10
        // PRD 2026-10-21-ikon-secici-atama-onay-paneli: icons are picked by their shape, not from a list of names.
        [Fact]
        public async Task Icons_are_picked_by_their_shape()
        {
            CreateTitle(S.WorkspaceA, "Şekil testi");
            var page = WebUtility.HtmlDecode(await _app.ClientFor(S.AliceId).GetStringAsync("/Workspace/Members"));

            Assert.DoesNotContain("<option value=\"laptop\"", page);
            foreach (var (icon, label) in TitleIcons.All)
                Assert.Matches($"data-icon=\"{icon}\"[^>]*title=\"{Regex.Escape(label)}\"[^>]*>\\s*<i class=\"bi bi-{icon}\"", page);
        }

        // Saving one person's title must not reset a choice made in another row.
        [Fact]
        public async Task Assigning_a_title_from_the_page_answers_without_a_reload()
        {
            var titleId = CreateTitle(S.WorkspaceA, "Yenilemesiz");
            _app.WithDb(db => { db.WorkspaceTitles.Single(t => t.Id == titleId).Icon = "star"; return db.SaveChanges(); });
            var (alice, token) = await OnTeamPageAsync(S.AliceId);

            var json = await PostFormForJsonAsync(alice, "/Workspace/Members?handler=AssignTitle", token,
                ("targetUserId", S.CarolId), ("titleId", $"{titleId}"));

            Assert.True(json.GetProperty("success").GetBoolean());
            Assert.Equal("Yenilemesiz", json.GetProperty("name").GetString());
            Assert.Equal("star", json.GetProperty("icon").GetString());
            Assert.Equal(titleId, TitleOf(S.WorkspaceA, S.CarolId));

            var page = WebUtility.HtmlDecode(await alice.GetStringAsync("/Workspace/Members"));
            var forms = Regex.Matches(page, "<form[^>]*handler=AssignTitle[^>]*>");
            Assert.NotEmpty(forms);
            Assert.All(forms, f => Assert.Contains("autocomplete=\"off\"", f.Value));
        }

        [Fact]
        public async Task Personal_space_has_no_title_management()
        {
            var personal = _app.WithDb(db =>
            {
                var ws = new Workspace { Name = "Kişisel Alanım", IsPersonal = true, CreatedByUserId = S.BobId };
                ws.Members.Add(new WorkspaceMember { UserId = S.BobId, Role = WorkspaceRole.Owner });
                db.Workspaces.Add(ws);
                db.SaveChanges();
                return ws.Id;
            });
            var bob = _app.ClientFor(S.BobId);
            await bob.GetAsync($"/Workspace/Switch?workspaceId={personal}&returnUrl=/Workspace/Members");
            var token = await GetAntiforgeryTokenAsync(bob, "/Profile");

            var page = WebUtility.HtmlDecode(await bob.GetStringAsync("/Workspace/Members"));
            await PostAsync(bob, token, "CreateTitle", ("titleName", "Kişisel unvan"), ("titleIcon", ""));

            Assert.DoesNotContain("Yeni unvan", page);
            Assert.Empty(TitlesOf(personal));
        }

        // Deleting a team removes its titles along with the members that carry them.
        [Fact]
        public void Deleting_a_team_with_titles_works()
        {
            var teamId = _app.WithDb(db =>
            {
                var team = new Workspace { Name = "Unvanlı ekip", CreatedByUserId = S.BobId };
                var title = new WorkspaceTitle { Workspace = team, Name = "Destek" };
                team.Members.Add(new WorkspaceMember { UserId = S.BobId, Role = WorkspaceRole.Owner, Title = title });
                db.Workspaces.Add(team);
                db.SaveChanges();
                return team.Id;
            });

            _app.WithDb(db => db.Database.ExecuteSqlRaw("DELETE FROM \"Workspaces\" WHERE \"Id\" = {0}", teamId));

            Assert.Empty(TitlesOf(teamId));
            Assert.False(_app.WithDb(db => db.WorkspaceMembers.Any(m => m.WorkspaceId == teamId)));
        }
    }
}
