using System.Text.Json;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingWeb.Tests.Security
{
    public class EncryptionFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // S4: at-rest encryption must be randomized and tamper-evident (AES-GCM, "v2:" format),
    // old CBC data must stay readable until it is re-encrypted, and the migration must never lose a value.
    public class EncryptionTests : IClassFixture<EncryptionFixture>
    {
        private readonly BriefynFactory _app;

        public EncryptionTests(EncryptionFixture fixture) => _app = fixture.Factory; // also initializes EncryptionHelper

        // --- Format ---

        [Fact]
        public void New_values_are_randomized_and_versioned()
        {
            var first = EncryptionHelper.Encrypt("gizli karar");
            var second = EncryptionHelper.Encrypt("gizli karar");

            Assert.StartsWith("v2:", first);
            Assert.NotEqual(first, second); // a static IV made equal plaintexts look equal
            Assert.Equal("gizli karar", EncryptionHelper.Decrypt(first));
        }

        [Fact]
        public void Tampered_values_are_detected()
        {
            var value = EncryptionHelper.Encrypt("1499 TL");
            var chars = value.ToCharArray();
            var i = chars.Length / 2;
            chars[i] = chars[i] == 'A' ? 'B' : 'A';
            var tampered = new string(chars);

            Assert.False(EncryptionHelper.TryDecrypt(tampered, out _));
            Assert.Equal(EncryptionHelper.DecryptionFailedMarker, EncryptionHelper.Decrypt(tampered));
        }

        [Fact]
        public void Legacy_values_remain_readable()
        {
            Assert.Equal("eski toplantı", EncryptionHelper.Decrypt(TestKeys.LegacyEncrypt("eski toplantı")));
        }

        [Fact]
        public void Unreadable_values_are_flagged_instead_of_shown_raw()
        {
            Assert.Equal(EncryptionHelper.DecryptionFailedMarker, EncryptionHelper.Decrypt("bozuk-veri-bozuk-veri"));
        }

        [Fact]
        public void Encrypted_columns_have_no_length_limit()
        {
            // Ciphertext is longer than the plaintext; a 255-char column made long titles fail to save.
            var model = _app.WithDb(db => db.Model);
            Assert.Null(model.FindEntityType(typeof(MeetingSummary))!.FindProperty(nameof(MeetingSummary.ToplantiKonusu))!.GetMaxLength());
            Assert.Null(model.FindEntityType(typeof(ActionItem))!.FindProperty(nameof(ActionItem.Text))!.GetMaxLength());
        }

        // --- Migration ---

        private sealed record RawMeeting(int Id, string ToplantiKonusu, string GorusulenKonular, string AlinanKararlar);

        private RawMeeting Raw(int id) => _app.WithDb(db => db.Database
            .SqlQueryRaw<RawMeeting>("SELECT \"Id\", \"ToplantiKonusu\", \"GorusulenKonular\", \"AlinanKararlar\" FROM \"MeetingSummaries\" WHERE \"Id\" = {0}", id)
            .AsEnumerable().Single());

        private string RawTask(int id) => _app.WithDb(db => db.Database
            .SqlQueryRaw<string>("SELECT \"Text\" AS \"Value\" FROM \"ActionItem\" WHERE \"Id\" = {0}", id)
            .AsEnumerable().Single());

        // Writes rows exactly as the old code stored them, bypassing the EF converters.
        private (int MeetingId, int TaskId) InsertLegacyMeeting(string topic, string[] topics, string taskText) => _app.WithDb(db =>
        {
            db.Database.ExecuteSqlRaw(
                "INSERT INTO \"MeetingSummaries\" (\"CreatedDate\", \"Status\", \"ToplantiKonusu\", \"GorusulenKonular\", \"AlinanKararlar\", \"WorkspaceId\") VALUES ({0}, 1, {1}, {2}, {3}, {4})",
                DateTime.UtcNow, TestKeys.LegacyEncrypt(topic), TestKeys.LegacyEncrypt(JsonSerializer.Serialize(topics)), "", _app.Seed.WorkspaceA);
            var meetingId = db.Database.SqlQueryRaw<int>("SELECT MAX(\"Id\") AS \"Value\" FROM \"MeetingSummaries\"").AsEnumerable().Single();
            db.Database.ExecuteSqlRaw("INSERT INTO \"ActionItem\" (\"Text\", \"IsDone\", \"MeetingSummaryId\") VALUES ({0}, 0, {1})",
                TestKeys.LegacyEncrypt(taskText), meetingId);
            var taskId = db.Database.SqlQueryRaw<int>("SELECT MAX(\"Id\") AS \"Value\" FROM \"ActionItem\"").AsEnumerable().Single();
            return (meetingId, taskId);
        });

        private Task<ReencryptionReport> RunAsync(bool apply)
        {
            var scope = _app.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IReencryptionService>().RunAsync(apply);
        }

        [Fact]
        public async Task Reencryption_dry_run_then_apply_converts_everything_without_losing_a_value()
        {
            var (meetingId, taskId) = InsertLegacyMeeting("Lansman kararı", new[] { "Fiyat 1499 TL", "Tedarikçi değişti" }, "Murat: sözleşme");
            var (brokenId, _) = InsertLegacyMeeting("bozuk", new[] { "x" }, "y");
            _app.WithDb(db => db.Database.ExecuteSqlRaw("UPDATE \"MeetingSummaries\" SET \"ToplantiKonusu\" = 'bozuk-veri-bozuk-veri' WHERE \"Id\" = {0}", brokenId));
            var before = Raw(meetingId);

            // 1. Dry run: reports, changes nothing.
            var dry = await RunAsync(apply: false);
            Assert.True(dry.Legacy >= 2);
            Assert.Contains(dry.Undecryptable, u => u.Contains($"MeetingSummaries#{brokenId}"));
            Assert.Equal(before, Raw(meetingId));

            // 2. Apply: legacy values become v2, plaintext is identical, the broken row is left untouched.
            var applied = await RunAsync(apply: true);
            Assert.True(applied.Converted >= 1);
            var after = Raw(meetingId);
            Assert.StartsWith("v2:", after.ToplantiKonusu);
            Assert.StartsWith("v2:", after.GorusulenKonular);
            Assert.Equal("", after.AlinanKararlar); // empty values stay empty
            Assert.StartsWith("v2:", RawTask(taskId));
            Assert.Equal("bozuk-veri-bozuk-veri", Raw(brokenId).ToplantiKonusu);

            var meeting = _app.WithDb(db => db.MeetingSummaries.Include(m => m.AksiyonMaddeleri).AsNoTracking().Single(m => m.Id == meetingId));
            Assert.Equal("Lansman kararı", meeting.ToplantiKonusu);
            Assert.Equal(new[] { "Fiyat 1499 TL", "Tedarikçi değişti" }, meeting.GorusulenKonular);
            Assert.Equal("Murat: sözleşme", meeting.AksiyonMaddeleri.Single().Text);

            // 3. Running again is safe: nothing left to convert.
            var again = await RunAsync(apply: true);
            Assert.Equal(0, again.Converted);
        }
    }
}
