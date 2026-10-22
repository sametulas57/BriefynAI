using System.Net;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features
{
    public class PendingTopicsFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    public class PendingTopicsTests : IClassFixture<PendingTopicsFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;
        public PendingTopicsTests(PendingTopicsFixture fixture) => _app = fixture.Factory;

        // Re-summarising needs a stored transcript; the seed meeting has none.
        private void ReadyToSummarise() => _app.WithDb(db =>
        {
            var meeting = db.Meetings.Include(m => m.Transcript).Single(m => m.Id == S.MeetingA);
            meeting.Status = MeetingStatus.Processing;
            meeting.Transcript ??= new MeetingTranscript { Text = "Ali: Bütçeye sonra bakarız." };
            return db.SaveChanges();
        });

        private async Task Process(int meetingId)
        {
            using var scope = _app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IMeetingProcessor>().ResummarizeMeetingJob(meetingId);
        }

        [Fact]
        public async Task Summary_pending_topics_are_saved_and_regeneration_preserves_manual_rows()
        {
            ReadyToSummarise();
            _app.WithDb(db =>
            {
                db.MeetingPoints.AddRange(
                    new MeetingPoint { MeetingId = S.MeetingA, Kind = MeetingPointKind.Pending, Text = "Eski AI", Source = EntrySource.Ai },
                    new MeetingPoint { MeetingId = S.MeetingA, Kind = MeetingPointKind.Pending, Text = "Elle", Source = EntrySource.Manual, CreatedByUserId = S.AliceId });
                db.SaveChanges(); return 0;
            });
            _app.PythonApi.Body = """{"success":true,"transkript":"test","summary":{"toplanti_konusu":"Konu","gorusulen_konular":[],"alinan_kararlar":[],"aksiyon_maddeleri":[],"bekleyen_konular":["Yeni bekleyen"]}}""";
            await Process(S.MeetingA);

            var rows = _app.WithDb(db => db.MeetingPoints.AsNoTracking().Where(p => p.MeetingId == S.MeetingA && p.Kind == MeetingPointKind.Pending).ToList());
            Assert.Contains(rows, p => p.Text == "Yeni bekleyen" && p.Source == EntrySource.Ai);
            Assert.Contains(rows, p => p.Text == "Elle" && p.Source == EntrySource.Manual);
            Assert.DoesNotContain(rows, p => p.Text == "Eski AI");
        }

        [Theory]
        [InlineData("\"bekleyen_konular\":null")]
        [InlineData("")]
        public async Task Missing_or_null_pending_topics_do_not_fail_processing(string field)
        {
            ReadyToSummarise();
            var prop = field.Length == 0 ? "" : "," + field;
            _app.PythonApi.Body = "{\"success\":true,\"transkript\":\"test\",\"summary\":{\"toplanti_konusu\":\"Konu\",\"gorusulen_konular\":[],\"alinan_kararlar\":[],\"aksiyon_maddeleri\":[]" + prop + "}}";
            await Process(S.MeetingA);
            Assert.Equal(MeetingStatus.Completed, _app.WithDb(db => db.Meetings.AsNoTracking().Single(m => m.Id == S.MeetingA).Status));
        }

        [Fact]
        public async Task Completed_meeting_shows_closed_pending_section_and_can_add_manual_pending_topic()
        {
            _app.WithDb(db => { db.Meetings.Single(m => m.Id == S.MeetingA).Status = MeetingStatus.Completed; return db.SaveChanges(); });
            var client = _app.ClientFor(S.AliceId);
            var token = await GetAntiforgeryTokenAsync(client, $"/Details?id={S.MeetingA}");
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/Details?id={S.MeetingA}"));
            Assert.Contains("Bekleyen konular (", page);
            var tag = page.LastIndexOf("<details", page.IndexOf("Bekleyen konular (", StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.True(tag >= 0);
            Assert.DoesNotContain("open", page.Substring(tag, page.IndexOf('>', tag) - tag));
            await PostFormAsync(client, $"/Details?id={S.MeetingA}&handler=AddPending", token,
                ("meetingId", $"{S.MeetingA}"), ("pending", "Elle bekleyen"));
            Assert.Contains(_app.WithDb(db => db.MeetingPoints.AsNoTracking().ToList()), p => p.MeetingId == S.MeetingA && p.Text == "Elle bekleyen" && p.Kind == MeetingPointKind.Pending && p.Source == EntrySource.Manual);
        }

        [Fact]
        public async Task Other_workspace_user_cannot_add_pending_topic()
        {
            var client = _app.ClientFor(S.BobId);
            var token = await GetAntiforgeryTokenAsync(client, "/Profile");
            await PostFormAsync(client, $"/Details?id={S.MeetingA}&handler=AddPending", token,
                ("meetingId", $"{S.MeetingA}"), ("pending", "Yasak"));
            Assert.DoesNotContain(_app.WithDb(db => db.MeetingPoints.AsNoTracking().ToList()), p => p.Text == "Yasak");
        }
    }
}
