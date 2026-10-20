using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Tests.Security
{
    public class EncryptionFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // ADR-0010: at-rest encryption is randomized, tamper-evident, carries its key id and is bound to its column.
    public class EncryptionTests : IClassFixture<EncryptionFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public EncryptionTests(EncryptionFixture fixture) => _app = fixture.Factory; // also initializes EncryptionHelper

        [Fact]
        public void New_values_are_randomized_and_carry_the_key_id()
        {
            var first = EncryptionHelper.Encrypt("gizli karar", "Meetings.Title");
            var second = EncryptionHelper.Encrypt("gizli karar", "Meetings.Title");

            Assert.StartsWith("v3:1:", first);
            Assert.NotEqual(first, second);
            Assert.Equal("gizli karar", EncryptionHelper.Decrypt(first, "Meetings.Title"));
        }

        [Fact]
        public void Tampered_values_are_detected()
        {
            var value = EncryptionHelper.Encrypt("1499 TL", "Meetings.Title");
            var chars = value.ToCharArray();
            var i = chars.Length / 2;
            chars[i] = chars[i] == 'A' ? 'B' : 'A';
            var tampered = new string(chars);

            Assert.False(EncryptionHelper.TryDecrypt(tampered, "Meetings.Title", out _));
            Assert.Equal(EncryptionHelper.DecryptionFailedMarker, EncryptionHelper.Decrypt(tampered, "Meetings.Title"));
        }

        [Fact]
        public void A_value_does_not_open_in_another_column()
        {
            var title = EncryptionHelper.Encrypt("gizli başlık", "Meetings.Title");

            Assert.False(EncryptionHelper.TryDecrypt(title, "Comments.Content", out _));
        }

        [Fact]
        public void Unreadable_values_are_flagged_instead_of_shown_raw()
        {
            Assert.Equal(EncryptionHelper.DecryptionFailedMarker, EncryptionHelper.Decrypt("bozuk-veri-bozuk-veri", "Meetings.Title"));
        }

        [Fact]
        public void Encrypted_columns_have_no_length_limit()
        {
            // Ciphertext is longer than the plaintext; a 255-char column made long titles fail to save.
            var model = _app.WithDb(db => db.Model);
            Assert.Null(model.FindEntityType(typeof(Meeting))!.FindProperty(nameof(Meeting.Title))!.GetMaxLength());
            Assert.Null(model.FindEntityType(typeof(MeetingPoint))!.FindProperty(nameof(MeetingPoint.Text))!.GetMaxLength());
            Assert.Null(model.FindEntityType(typeof(ActionItem))!.FindProperty(nameof(ActionItem.Text))!.GetMaxLength());
        }

        private string Raw(string table, string column, int id) => _app.WithDb(db => db.Database
            .SqlQueryRaw<string>($"SELECT \"{column}\" AS \"Value\" FROM \"{table}\" WHERE \"Id\" = {{0}}", id).AsEnumerable().Single());

        [Fact]
        public void Meeting_content_is_stored_encrypted()
        {
            Assert.StartsWith("v3:1:", Raw("Meetings", "Title", S.MeetingA));
            var pointId = _app.WithDb(db => db.MeetingPoints.First(p => p.MeetingId == S.MeetingA).Id);
            Assert.StartsWith("v3:1:", Raw("MeetingPoints", "Text", pointId));
            Assert.StartsWith("v3:1:", Raw("ActionItems", "Text", S.TaskA));
        }

        [Fact]
        public void Ciphertext_copied_into_another_column_shows_the_marker()
        {
            // Someone with database access copies the meeting's encrypted title into a comment.
            var id = _app.WithDb(db =>
            {
                var meeting = new Meeting { WorkspaceId = S.WorkspaceA, Title = "kopyalanacak", Status = MeetingStatus.Completed };
                db.Meetings.Add(meeting);
                db.SaveChanges();
                return meeting.Id;
            });
            var title = Raw("Meetings", "Title", id);
            _app.WithDb(db => db.Database.ExecuteSqlRaw("UPDATE \"Comments\" SET \"Content\" = {0} WHERE \"Id\" = {1}", title, S.CommentA));

            Assert.Equal(EncryptionHelper.DecryptionFailedMarker, _app.WithDb(db => db.Comments.AsNoTracking().Single(c => c.Id == S.CommentA).Content));
        }
    }
}
