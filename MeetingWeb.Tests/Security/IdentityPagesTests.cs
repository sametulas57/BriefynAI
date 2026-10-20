using System.Net;
using MeetingWeb.Tests.Infrastructure;

namespace MeetingWeb.Tests.Security
{
    public class IdentityPagesFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // Found in review (2026-10-21): the framework's built-in account pages were reachable next to our own. Their sign-up
    // skips the consent and the personal space, and their account deletion breaks on the v2 schema. Only logout is used.
    public class IdentityPagesTests : IClassFixture<IdentityPagesFixture>
    {
        private readonly BriefynFactory _app;

        public IdentityPagesTests(IdentityPagesFixture fixture) => _app = fixture.Factory;

        [Theory]
        [InlineData("/Identity/Account/Register")]
        [InlineData("/Identity/Account/Manage")]
        [InlineData("/Identity/Account/Manage/DeletePersonalData")]
        [InlineData("/Identity/Account/Login")]
        public async Task Built_in_account_pages_are_closed(string url)
        {
            var res = await _app.ClientFor(_app.Seed.AliceId).GetAsync(url);

            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        }

        [Fact]
        public async Task Logout_still_works()
        {
            var alice = _app.ClientFor(_app.Seed.AliceId);
            var token = await FormHelpers.GetAntiforgeryTokenAsync(alice, "/Profile");

            var res = await FormHelpers.PostFormAsync(alice, "/Identity/Account/Logout?returnUrl=%2F", token);

            Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        }
    }
}
