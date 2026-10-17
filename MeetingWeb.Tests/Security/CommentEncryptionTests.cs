using System.Net;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            .SqlQueryRaw<string>("SELECT \"Content\" AS \"Value\" FROM \"MeetingComments\" WHERE \"Id\" = {0}", id).AsEnumerable().Single());

        private int InsertPlainComment(string text) => _app.WithDb(db =>
        {
            db.Database.ExecuteSqlRaw("INSERT INTO \"MeetingComments\" (\"MeetingId\", \"UserId\", \"Content\", \"CreatedAt\") VALUES ({0}, {1}, {2}, {3})",
                S.MeetingA, S.AliceId, text, DateTime.UtcNow);
            return db.Database.SqlQueryRaw<int>("SELECT MAX(\"Id\") AS \"Value\" FROM \"MeetingComments\"").AsEnumerable().Single();
        });

        private async Task<string> PageAsync() =>
            WebUtility.HtmlDecode(await _app.ClientFor(S.AliceId).GetStringAsync($"/Details?id={S.MeetingA}"));

        [Fact]
        public async Task New_comments_are_stored_encrypted_and_shown_as_text()
        {
            var alice = _app.ClientFor(S.AliceId);
            var token = await GetAntiforgeryTokenAsync(alice, $"/Details?id={S.MeetingA}");
            await PostFormAsync(alice, $"/Details?id={S.MeetingA}&handler=AddComment", token,
                ("meetingId", $"{S.MeetingA}"), ("content", "Fiyat 1499 TL kalsın"));

            var id = _app.WithDb(db => db.MeetingComments.AsNoTracking().AsEnumerable().Single(c => c.Content == "Fiyat 1499 TL kalsın").Id);
            Assert.StartsWith("v2:", RawContent(id));
            Assert.Contains("Fiyat 1499 TL kalsın", await PageAsync());
        }

        [Fact]
        public async Task Comments_written_before_encryption_stay_readable()
        {
            InsertPlainComment("eski düz yorum");

            Assert.Contains("eski düz yorum", await PageAsync());
        }

        [Fact]
        public async Task Reencryption_encrypts_old_plain_comments()
        {
            var id = InsertPlainComment("taşınacak yorum");

            using (var scope = _app.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<IReencryptionService>().RunAsync(apply: true);

            Assert.StartsWith("v2:", RawContent(id));
            Assert.Equal("taşınacak yorum", _app.WithDb(db => db.MeetingComments.AsNoTracking().Single(c => c.Id == id).Content));
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
            Assert.False(_app.WithDb(db => db.MeetingComments.AsEnumerable().Any(c => c.Content.Length > 2000)));
        }
    }
}
