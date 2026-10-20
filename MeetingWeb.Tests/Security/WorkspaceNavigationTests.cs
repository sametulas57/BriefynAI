using System.Net;
using MeetingWeb.Models;
using MeetingWeb.Tests.Infrastructure;

namespace MeetingWeb.Tests.Security
{
    public class WorkspaceNavigationFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();

        public async Task InitializeAsync()
        {
            await Factory.InitializeAsync();
            // Alice also belongs to workspace B, so she can switch between two companies.
            Factory.WithDb(db =>
            {
                db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = Factory.Seed.WorkspaceB, UserId = Factory.Seed.AliceId, Role = WorkspaceRole.Member });
                return db.SaveChanges();
            });
        }

        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // Ulaş's browser check: switching company while viewing a meeting summary ended on "page not found".
    // Legitimate navigation must land somewhere useful; outsiders must still learn nothing about the meeting.
    public class WorkspaceNavigationTests : IClassFixture<WorkspaceNavigationFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public WorkspaceNavigationTests(WorkspaceNavigationFixture fixture) => _app = fixture.Factory;

        private static async Task<string> PageTextAsync(HttpClient client, string url) =>
            WebUtility.HtmlDecode(await client.GetStringAsync(url));

        [Fact]
        public async Task Switching_company_from_a_meeting_summary_opens_that_companys_history()
        {
            var alice = _app.ClientFor(S.AliceId);
            await alice.GetAsync($"/Details?id={S.MeetingA}");

            // The menu sends only the path as returnUrl (the meeting id is dropped).
            var res = await alice.GetAsync($"/Workspace/Switch?workspaceId={S.WorkspaceB}&returnUrl=/Details");

            Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
            Assert.Equal("/History", res.Headers.Location!.OriginalString);
            var history = await PageTextAsync(alice, "/History");
            Assert.Contains("B gizli toplantı", history);
            Assert.DoesNotContain("A gizli toplantı", history);
        }

        [Fact]
        public async Task Switching_company_from_a_company_wide_page_stays_on_that_page()
        {
            var res = await _app.ClientFor(S.AliceId).GetAsync($"/Workspace/Switch?workspaceId={S.WorkspaceB}&returnUrl=/Workspace/Members");

            Assert.Equal("/Workspace/Members", res.Headers.Location!.OriginalString);
        }

        [Fact]
        public async Task Meeting_page_without_an_id_goes_to_history_instead_of_not_found()
        {
            var res = await _app.ClientFor(S.AliceId).GetAsync("/Details");

            Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
            Assert.Equal("/History", res.Headers.Location!.OriginalString);
        }

        [Fact]
        public async Task Unavailable_meeting_explains_and_reveals_nothing()
        {
            // Carol is only in workspace A; Bob's meeting is in B.
            var carol = _app.ClientFor(S.CarolId);

            var res = await carol.GetAsync($"/Details?id={S.MeetingB}");
            var missing = await carol.GetAsync("/Details?id=999999");

            Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
            Assert.Equal(res.Headers.Location, missing.Headers.Location); // same answer as for an id that does not exist
            var page = await PageTextAsync(carol, res.Headers.Location!.OriginalString);
            Assert.Contains("Bu özet seçili ekipte bulunamadı", page);
            Assert.DoesNotContain("B gizli toplantı", page);
        }
    }
}
