using System.Net;
using System.Text.RegularExpressions;
using MeetingWeb.Models;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Security
{
    public class PersonalWorkspaceFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public int CarolPersonal { get; private set; }

        public async Task InitializeAsync()
        {
            await Factory.InitializeAsync();
            CarolPersonal = Factory.WithDb(db =>
            {
                var personal = new Workspace { Name = "Kişisel Alanım", IsPersonal = true };
                db.Workspaces.Add(personal);
                db.SaveChanges();
                db.WorkspaceUsers.Add(new WorkspaceUser { WorkspaceId = personal.Id, UserId = Factory.Seed.CarolId, Role = "Sahip" });
                db.SaveChanges();
                return personal.Id;
            });
        }

        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // Ulaş: inviting people into a personal space should be possible but gently discouraged in favour of a team.
    public class PersonalWorkspaceTests : IClassFixture<PersonalWorkspaceFixture>
    {
        private readonly BriefynFactory _app;
        private readonly PersonalWorkspaceFixture _fx;

        public PersonalWorkspaceTests(PersonalWorkspaceFixture fixture) { _fx = fixture; _app = fixture.Factory; }

        private static async Task<string> PageTextAsync(HttpClient client, string url) =>
            WebUtility.HtmlDecode(await client.GetStringAsync(url));

        [Fact]
        public async Task Invite_page_in_a_personal_space_suggests_a_team_but_still_allows_inviting()
        {
            var carol = _app.ClientFor(_app.Seed.CarolId);
            await carol.GetAsync($"/Workspace/Switch?workspaceId={_fx.CarolPersonal}&returnUrl=/Workspace/Invite");

            var page = await PageTextAsync(carol, "/Workspace/Invite");

            Assert.Contains("Burası kişisel alanınız", page);
            Assert.Contains("/Workspace/Create", page);
            Assert.Contains("Yeni Davet Linki Üret", page); // still possible
        }

        [Fact]
        public async Task Invite_page_in_a_team_shows_no_personal_notice()
        {
            var page = await PageTextAsync(_app.ClientFor(_app.Seed.AliceId), "/Workspace/Invite");

            Assert.DoesNotContain("Burası kişisel alanınız", page);
        }

        [Fact]
        public async Task Registration_creates_a_personal_space_marked_as_personal()
        {
            var visitor = _app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
                { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            var token = await GetAntiforgeryTokenAsync(visitor, "/Auth/Register");

            await PostFormAsync(visitor, "/Auth/Register", token,
                ("Input.FullName", "Yeni Kullanıcı"), ("Input.Email", "yeni@kisi.test"),
                ("Input.Password", "Guclu-Parola-123"), ("Input.ConfirmPassword", "Guclu-Parola-123"),
                ("Input.IsKvkkAccepted", "true"));

            var personal = _app.WithDb(db => db.WorkspaceUsers.Include(wu => wu.Workspace).AsNoTracking()
                .Single(wu => wu.User.Email == "yeni@kisi.test").Workspace);
            Assert.True(personal.IsPersonal);
        }

        [Fact]
        public void A_team_whose_name_contains_kisisel_is_not_treated_as_personal()
        {
            var team = new Workspace { Name = "Kişisel Gelişim Ekibi" };
            Assert.False(team.IsPersonal);
        }
    }
}
