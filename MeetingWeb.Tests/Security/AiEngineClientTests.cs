using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingWeb.Tests.Security
{
    public class AiEngineClientFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // S5 (backend side): the AI engine rejects callers without the shared key, so the backend must always send it.
    public class AiEngineClientTests : IClassFixture<AiEngineClientFixture>
    {
        private readonly BriefynFactory _app;

        public AiEngineClientTests(AiEngineClientFixture fixture) => _app = fixture.Factory;

        [Fact]
        public async Task Backend_sends_the_shared_api_key_to_the_ai_engine()
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var audio = Path.Combine(_app.AudioRoot, "anahtar.m4a");
            File.WriteAllBytes(audio, new byte[] { 1 });
            var meeting = new Meeting { WorkspaceId = _app.Seed.WorkspaceA, Title = "anahtar", AudioFileName = "anahtar.m4a" };
            db.Meetings.Add(meeting);
            await db.SaveChangesAsync();

            await scope.ServiceProvider.GetRequiredService<IMeetingProcessor>().ProcessMeetingJob(meeting.Id);

            Assert.Equal(BriefynFactory.AiEngineApiKey, _app.PythonApi.LastApiKey);
            Assert.DoesNotContain("name=speakers", _app.PythonApi.LastBody);
        }

        [Fact]
        public async Task Backend_sends_sidecar_when_present()
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var audio = Path.Combine(_app.AudioRoot, "konusmaci.m4a");
            File.WriteAllBytes(audio, new byte[] { 1 });
            File.WriteAllText(audio + ".vtt", "WEBVTT\n\nAli: söz");
            var meeting = new Meeting { WorkspaceId = _app.Seed.WorkspaceA, Title = "konuşmacı", AudioFileName = "konusmaci.m4a" };
            db.Meetings.Add(meeting);
            await db.SaveChangesAsync();

            await scope.ServiceProvider.GetRequiredService<IMeetingProcessor>().ProcessMeetingJob(meeting.Id);

            Assert.Contains("name=speakers", _app.PythonApi.LastBody);
            Assert.Contains("Ali: söz", _app.PythonApi.LastBody);
        }
    }
}
