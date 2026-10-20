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
    public class AudioFileFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // S2: meeting recordings must never be publicly downloadable, and must not outlive their purpose.
    public class AudioFileTests : IClassFixture<AudioFileFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public AudioFileTests(AudioFileFixture fixture) => _app = fixture.Factory;

        private async Task<int> CreateMeetingAsync(MeetingStatus status, string? storedAudio, DateTime? createdUtc = null)
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var meeting = new Meeting
            {
                WorkspaceId = S.WorkspaceA,
                Title = "ses testi",
                Status = status,
                AudioFileName = storedAudio,
                CreatedAtUtc = createdUtc ?? DateTime.UtcNow
            };
            db.Meetings.Add(meeting);
            await db.SaveChangesAsync();
            return meeting.Id;
        }

        private string WriteAudio(string name, DateTime? lastWriteUtc = null)
        {
            var path = Path.Combine(_app.AudioRoot, name);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            if (lastWriteUtc != null) File.SetLastWriteTimeUtc(path, lastWriteUtc.Value);
            return path;
        }

        private Meeting Meeting(int id) => _app.WithDb(db => db.Meetings.AsNoTracking().Single(m => m.Id == id));

        [Fact]
        public async Task Upload_stores_recording_outside_wwwroot()
        {
            var alice = _app.ClientFor(S.AliceId);
            var token = await GetAntiforgeryTokenAsync(alice, "/Upload");

            using var form = new MultipartFormDataContent
            {
                { new ByteArrayContent(new byte[] { 1, 2, 3, 4 }), "UploadFile", "toplanti.m4a" },
                { new StringContent(token), "__RequestVerificationToken" }
            };
            var res = await alice.PostAsync("/Upload", form);

            Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
            var stored = _app.WithDb(db => db.Meetings.AsNoTracking()
                .Where(m => m.AudioFileName != null && m.AudioFileName.EndsWith("toplanti.m4a"))
                .Select(m => m.AudioFileName!).Single());
            Assert.False(stored.StartsWith("/"), "AudioFileName must not be a public URL");
            Assert.True(File.Exists(Path.Combine(_app.AudioRoot, stored)));
            Assert.Empty(Directory.GetFiles(Path.Combine(_app.WebRoot, "uploads"), "*toplanti.m4a"));
            // ADR-0009: the job carries only the meeting id, never the file path.
            Assert.Equal(1, _app.Jobs.CountFor("ProcessMeetingJob", a => a.Count == 1 && a[0] is int));
        }

        [Fact]
        public async Task Legacy_recordings_under_uploads_are_not_served()
        {
            File.WriteAllBytes(Path.Combine(_app.WebRoot, "uploads", "eski_kayit.m4a"), new byte[] { 1 });

            var anonymous = await _app.CreateClient().GetAsync("/uploads/eski_kayit.m4a");
            var loggedIn = await _app.ClientFor(S.BobId).GetAsync("/uploads/eski_kayit.m4a");

            Assert.Equal(HttpStatusCode.NotFound, anonymous.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, loggedIn.StatusCode);
        }

        [Fact]
        public async Task Avatars_stay_public()
        {
            File.WriteAllBytes(Path.Combine(_app.WebRoot, "uploads", "avatars", "u1.jpg"), new byte[] { 1 });

            var res = await _app.CreateClient().GetAsync("/uploads/avatars/u1.jpg");

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        [Fact]
        public async Task Recording_is_deleted_after_successful_analysis()
        {
            var path = WriteAudio("basarili.m4a");
            var id = await CreateMeetingAsync(MeetingStatus.Processing, "basarili.m4a");

            using (var scope = _app.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<IMeetingProcessor>().ProcessMeetingJob(id);

            Assert.False(File.Exists(path));
            var meeting = Meeting(id);
            Assert.Equal(MeetingStatus.Completed, meeting.Status);
            Assert.Null(meeting.AudioFileName);
        }

        [Fact]
        public async Task Recording_is_kept_when_analysis_fails_so_it_can_be_retried()
        {
            var path = WriteAudio("basarisiz.m4a");
            var id = await CreateMeetingAsync(MeetingStatus.Processing, "basarisiz.m4a");
            var failingApi = new BriefynFactoryScope(_app, HttpStatusCode.InternalServerError);

            await Assert.ThrowsAnyAsync<Exception>(() => failingApi.ProcessAsync(id));

            Assert.True(File.Exists(path));
            Assert.Equal(MeetingStatus.Failed, Meeting(id).Status);
            Assert.Equal("basarisiz.m4a", Meeting(id).AudioFileName);
        }

        [Fact]
        public async Task Cleanup_removes_expired_failed_recordings_and_orphans_only()
        {
            var now = DateTime.UtcNow;
            var oldFailed = WriteAudio("eski_hata.m4a");
            var oldFailedId = await CreateMeetingAsync(MeetingStatus.Failed, "eski_hata.m4a", now.AddDays(-8));
            var recentFailed = WriteAudio("yeni_hata.m4a");
            var recentFailedId = await CreateMeetingAsync(MeetingStatus.Failed, "yeni_hata.m4a", now.AddDays(-1));
            var processing = WriteAudio("isleniyor.m4a", now.AddDays(-10));
            await CreateMeetingAsync(MeetingStatus.Processing, "isleniyor.m4a", now.AddDays(-10));
            var oldOrphan = WriteAudio("sahipsiz_eski.m4a", now.AddDays(-8));
            var freshOrphan = WriteAudio("sahipsiz_yeni.m4a", now);

            using (var scope = _app.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<IAudioCleanupService>().CleanupAsync(now);

            Assert.False(File.Exists(oldFailed));
            Assert.Null(Meeting(oldFailedId).AudioFileName);
            Assert.True(File.Exists(recentFailed));
            Assert.Equal("yeni_hata.m4a", Meeting(recentFailedId).AudioFileName);
            Assert.True(File.Exists(processing));
            Assert.False(File.Exists(oldOrphan));
            Assert.True(File.Exists(freshOrphan));
        }

        [Fact]
        public async Task Deleting_a_meeting_deletes_its_recording()
        {
            var path = WriteAudio("silinecek.m4a");
            var id = await CreateMeetingAsync(MeetingStatus.Failed, "silinecek.m4a");
            var alice = _app.ClientFor(S.AliceId);
            var token = await GetAntiforgeryTokenAsync(alice, "/History"); // also puts workspace A in the session

            var res = await PostFormAsync(alice, "/History?handler=Delete", token, ("id", $"{id}"));

            Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
            Assert.False(_app.WithDb(db => db.Meetings.Any(m => m.Id == id)));
            Assert.False(File.Exists(path));
        }
    }

    // Runs the processor with the Python stub switched to a given status, then restores it.
    internal sealed class BriefynFactoryScope
    {
        private readonly BriefynFactory _app;
        private readonly HttpStatusCode _status;

        public BriefynFactoryScope(BriefynFactory app, HttpStatusCode status) { _app = app; _status = status; }

        public async Task ProcessAsync(int meetingId)
        {
            var previous = _app.PythonApi.Status;
            _app.PythonApi.Status = _status;
            try
            {
                using var scope = _app.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IMeetingProcessor>().ProcessMeetingJob(meetingId);
            }
            finally
            {
                _app.PythonApi.Status = previous;
            }
        }
    }
}
