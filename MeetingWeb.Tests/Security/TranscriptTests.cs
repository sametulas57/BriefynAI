using System.Net;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Security
{
    public class TranscriptFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // Faz 2 (K4): the transcript is stored encrypted, a retry without audio only regenerates the summary,
    // and manual additions survive a regenerated summary.
    public class TranscriptTests : IClassFixture<TranscriptFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public TranscriptTests(TranscriptFixture fixture) => _app = fixture.Factory;

        private int NewMeeting(MeetingStatus status, string? audio, string? transcript = null) => _app.WithDb(db =>
        {
            var m = new MeetingSummary
            {
                WorkspaceId = S.WorkspaceA, ToplantiKonusu = "döküm testi", Status = status, AudioFilePath = audio, Transkript = transcript,
                GorusulenKonular = new List<string> { "eski yapay zekâ konusu", "elle eklenen konu|~|" + S.AliceId },
                AksiyonMaddeleri = new List<ActionItem> { new() { Text = "eski görev" }, new() { Text = "elle görev|~|" + S.AliceId } }
            };
            db.MeetingSummaries.Add(m);
            db.SaveChanges();
            return m.Id;
        });

        private MeetingSummary Load(int id) => _app.WithDb(db => db.MeetingSummaries.Include(m => m.AksiyonMaddeleri).AsNoTracking().Single(m => m.Id == id));

        private async Task ProcessAsync(Func<IMeetingProcessor, Task> run)
        {
            using var scope = _app.Services.CreateScope();
            try { await run(scope.ServiceProvider.GetRequiredService<IMeetingProcessor>()); }
            catch { /* failed jobs rethrow for Hangfire */ }
        }

        [Fact]
        public async Task Transcript_is_stored_encrypted_after_analysis()
        {
            var audio = Path.Combine(_app.AudioRoot, "doküm.m4a");
            File.WriteAllBytes(audio, new byte[] { 1 });
            var id = NewMeeting(MeetingStatus.Processing, "doküm.m4a");

            await ProcessAsync(p => p.ProcessMeetingJob(id, audio));

            Assert.Equal("test", Load(id).Transkript); // stub answers transkript = "test"
            var raw = _app.WithDb(db => db.Database
                .SqlQueryRaw<string>("SELECT \"Transkript\" AS \"Value\" FROM \"MeetingSummaries\" WHERE \"Id\" = {0}", id).AsEnumerable().Single());
            Assert.StartsWith("v2:", raw);
        }

        [Fact]
        public async Task Failed_summary_keeps_the_transcript_and_marks_failed()
        {
            var audio = Path.Combine(_app.AudioRoot, "ozetsiz.m4a");
            File.WriteAllBytes(audio, new byte[] { 1 });
            var id = NewMeeting(MeetingStatus.Processing, "ozetsiz.m4a");
            var previous = _app.PythonApi.Body;
            _app.PythonApi.Body = """{"success": false, "transkript": "yalnız döküm", "summary": null}""";
            try { await ProcessAsync(p => p.ProcessMeetingJob(id, audio)); }
            finally { _app.PythonApi.Body = previous; }

            var meeting = Load(id);
            Assert.Equal(MeetingStatus.Failed, meeting.Status);
            Assert.Equal("yalnız döküm", meeting.Transkript);
        }

        [Fact]
        public async Task Retry_without_audio_but_with_transcript_regenerates_only_the_summary()
        {
            var id = NewMeeting(MeetingStatus.Failed, audio: null, transcript: "kayıtlı döküm");
            var alice = _app.ClientFor(S.AliceId);
            var token = await GetAntiforgeryTokenAsync(alice, $"/Details?id={id}");

            await PostFormAsync(alice, $"/Details?id={id}&handler=RetryProcessing", token, ("id", $"{id}"));

            Assert.Equal(1, _app.Jobs.CountFor("ResummarizeMeetingJob", a => (int)a[0]! == id));
            Assert.Equal(0, _app.Jobs.CountFor("ProcessMeetingJob", a => (int)a[0]! == id));
            Assert.Equal(MeetingStatus.Processing, Load(id).Status);
        }

        [Fact]
        public async Task Regenerated_summary_replaces_ai_items_and_keeps_manual_ones()
        {
            var id = NewMeeting(MeetingStatus.Processing, audio: null, transcript: "kayıtlı döküm");

            await ProcessAsync(p => p.ResummarizeMeetingJob(id));

            var meeting = Load(id);
            Assert.Equal(MeetingStatus.Completed, meeting.Status);
            Assert.Equal("api/summarize", _app.PythonApi.LastPath);
            Assert.Contains("a", meeting.GorusulenKonular);                          // new AI topic from the stub
            Assert.Contains(meeting.GorusulenKonular, k => k.StartsWith("elle eklenen konu|~|"));
            Assert.DoesNotContain("eski yapay zekâ konusu", meeting.GorusulenKonular);
            Assert.Contains(meeting.AksiyonMaddeleri, a => a.Text.StartsWith("elle görev|~|"));
            Assert.DoesNotContain(meeting.AksiyonMaddeleri, a => a.Text == "eski görev");
        }

        [Fact]
        public async Task Meeting_page_offers_the_transcript()
        {
            var id = NewMeeting(MeetingStatus.Completed, audio: null, transcript: "Herkese günaydın, bugünkü gündem lansman.");

            var page = WebUtility.HtmlDecode(await _app.ClientFor(S.AliceId).GetStringAsync($"/Details?id={id}"));

            Assert.Contains("Toplantı dökümü", page);
            Assert.Contains("Herkese günaydın, bugünkü gündem lansman.", page);
        }
    }
}
