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
            var m = new Meeting
            {
                WorkspaceId = S.WorkspaceA, Title = "döküm testi", Status = status, AudioFileName = audio,
                Transcript = transcript == null ? null : new MeetingTranscript { Text = transcript },
                Points = new List<MeetingPoint>
                {
                    new() { Kind = MeetingPointKind.Topic, Text = "eski yapay zekâ konusu" },
                    new() { Kind = MeetingPointKind.Topic, Text = "elle eklenen konu", Source = EntrySource.Manual, CreatedByUserId = S.AliceId }
                },
                ActionItems = new List<ActionItem>
                {
                    new() { Text = "eski görev" },
                    new() { Text = "elle görev", Source = EntrySource.Manual, CreatedByUserId = S.AliceId }
                }
            };
            db.Meetings.Add(m);
            db.SaveChanges();
            return m.Id;
        });

        private Meeting Load(int id) => _app.WithDb(db => db.Meetings
            .Include(m => m.Transcript).Include(m => m.Points).Include(m => m.ActionItems).AsNoTracking().Single(m => m.Id == id));

        private static List<string> Topics(Meeting m) => m.PointsOf(MeetingPointKind.Topic).Select(p => p.Text).ToList();

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

            await ProcessAsync(p => p.ProcessMeetingJob(id));

            Assert.Equal("test", Load(id).Transcript!.Text); // stub answers transkript = "test"
            var raw = _app.WithDb(db => db.Database
                .SqlQueryRaw<string>("SELECT \"Text\" AS \"Value\" FROM \"MeetingTranscripts\" WHERE \"MeetingId\" = {0}", id).AsEnumerable().Single());
            Assert.StartsWith("v3:1:", raw);
        }

        [Fact]
        public async Task Failed_summary_keeps_the_transcript_and_marks_failed()
        {
            var audio = Path.Combine(_app.AudioRoot, "ozetsiz.m4a");
            File.WriteAllBytes(audio, new byte[] { 1 });
            var id = NewMeeting(MeetingStatus.Processing, "ozetsiz.m4a");
            var previous = _app.PythonApi.Body;
            _app.PythonApi.Body = """{"success": false, "transkript": "yalnız döküm", "summary": null}""";
            try { await ProcessAsync(p => p.ProcessMeetingJob(id)); }
            finally { _app.PythonApi.Body = previous; }

            var meeting = Load(id);
            Assert.Equal(MeetingStatus.Failed, meeting.Status);
            Assert.Equal("yalnız döküm", meeting.Transcript!.Text);
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
            Assert.Contains("a", Topics(meeting));                          // new AI topic from the stub
            Assert.Equal("elle eklenen konu", Topics(meeting).Last());     // manual lines follow the AI ones
            Assert.DoesNotContain("eski yapay zekâ konusu", Topics(meeting));
            Assert.Contains(meeting.ActionItems, a => a.Text == "elle görev" && a.CreatedByUserId == S.AliceId);
            Assert.DoesNotContain(meeting.ActionItems, a => a.Text == "eski görev");
        }

        [Fact]
        public async Task Manual_line_added_while_the_summary_is_regenerated_survives()
        {
            // The summary job loads the meeting, then a user adds a decision before the job saves.
            var id = NewMeeting(MeetingStatus.Processing, audio: null, transcript: "kayıtlı döküm");
            using var scope = _app.Services.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IMeetingProcessor>();
            _app.PythonApi.BeforeRespond = () => _app.WithDb(db =>
            {
                db.MeetingPoints.Add(new MeetingPoint { MeetingId = id, Kind = MeetingPointKind.Decision, Text = "araya giren karar", Source = EntrySource.Manual, CreatedByUserId = S.AliceId });
                return db.SaveChanges();
            });
            try { await processor.ResummarizeMeetingJob(id); }
            finally { _app.PythonApi.BeforeRespond = null; }

            Assert.Contains(Load(id).Points, p => p.Text == "araya giren karar");
        }

        // Found in review: a task assigned while the AI was working was judged by its stale, unassigned copy.
        [Fact]
        public async Task Task_assigned_while_the_summary_is_regenerated_is_kept()
        {
            var id = NewMeeting(MeetingStatus.Processing, audio: null, transcript: "kayıtlı döküm");
            using var scope = _app.Services.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IMeetingProcessor>();
            _app.PythonApi.BeforeRespond = () => _app.WithDb(db =>
            {
                db.ActionItems.Where(a => a.MeetingId == id).AsEnumerable().Single(a => a.Text == "eski görev").AssigneeUserId = S.CarolId;
                return db.SaveChanges();
            });
            try { await processor.ResummarizeMeetingJob(id); }
            finally { _app.PythonApi.BeforeRespond = null; }

            var meeting = Load(id);
            Assert.Equal(MeetingStatus.Completed, meeting.Status);
            Assert.Contains(meeting.ActionItems, a => a.Text == "eski görev" && a.AssigneeUserId == S.CarolId);
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
