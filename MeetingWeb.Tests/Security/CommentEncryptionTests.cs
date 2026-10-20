using System.Net;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Security
{
    public class CommentEncryptionFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // Comments talk about the meeting, so they are encrypted at rest like the summary itself.
    public class CommentEncryptionTests : IClassFixture<CommentEncryptionFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public CommentEncryptionTests(CommentEncryptionFixture fixture) => _app = fixture.Factory;

        private string RawContent(int id) => _app.WithDb(db => db.Database
            .SqlQueryRaw<string>("SELECT \"Content\" AS \"Value\" FROM \"Comments\" WHERE \"Id\" = {0}", id).AsEnumerable().Single());

        private async Task<string> PageAsync() =>
            WebUtility.HtmlDecode(await _app.ClientFor(S.AliceId).GetStringAsync($"/Details?id={S.MeetingA}"));

        [Fact]
        public async Task New_comments_are_stored_encrypted_and_shown_as_text()
        {
            var alice = _app.ClientFor(S.AliceId);
            var token = await GetAntiforgeryTokenAsync(alice, $"/Details?id={S.MeetingA}");
            await PostFormAsync(alice, $"/Details?id={S.MeetingA}&handler=AddComment", token,
                ("meetingId", $"{S.MeetingA}"), ("content", "Fiyat 1499 TL kalsın"));

            var id = _app.WithDb(db => db.Comments.AsNoTracking().AsEnumerable().Single(c => c.Content == "Fiyat 1499 TL kalsın").Id);
            Assert.StartsWith("v3:1:", RawContent(id));
            Assert.Contains("Fiyat 1499 TL kalsın", await PageAsync());
        }

        [Fact]
        public async Task Too_long_comment_is_refused_with_a_message()
        {
            var alice = _app.ClientFor(S.AliceId);
            var token = await GetAntiforgeryTokenAsync(alice, $"/Details?id={S.MeetingA}");
            var res = await PostFormAsync(alice, $"/Details?id={S.MeetingA}&handler=AddComment", token,
                ("meetingId", $"{S.MeetingA}"), ("content", new string('a', 2001)));

            var page = WebUtility.HtmlDecode(await alice.GetStringAsync(res.Headers.Location!.ToString()));
            Assert.Contains("en fazla 2000 karakter", page);
            Assert.False(_app.WithDb(db => db.Comments.AsEnumerable().Any(c => c.Content.Length > 2000)));
        }
    }
}
