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
    public class PolishFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // Polish items from the plan ("cilalar"): no meeting stays "processing" forever, deleting a team works,
    // and big uploads / unknown pages get a friendly answer.
    public class PolishTests : IClassFixture<PolishFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public PolishTests(PolishFixture fixture) => _app = fixture.Factory;

        private static async Task<string> PageTextAsync(HttpClient client, string url) =>
            WebUtility.HtmlDecode(await client.GetStringAsync(url));

        [Fact]
        public async Task Empty_ai_answer_marks_the_meeting_failed_instead_of_processing_forever()
        {
            var audio = Path.Combine(_app.AudioRoot, "bos-cevap.m4a");
            File.WriteAllBytes(audio, new byte[] { 1 });
            var id = _app.WithDb(db =>
            {
                var m = new MeetingSummary { WorkspaceId = S.WorkspaceA, ToplantiKonusu = "bos", AudioFilePath = "bos-cevap.m4a" };
                db.MeetingSummaries.Add(m);
                db.SaveChanges();
                return m.Id;
            });
            var previous = _app.PythonApi.Body;
            _app.PythonApi.Body = """{"success": true, "transkript": "x", "summary": null}""";
            try
            {
                using var scope = _app.Services.CreateScope();
                try { await scope.ServiceProvider.GetRequiredService<IMeetingProcessor>().ProcessMeetingJob(id, audio); }
                catch { /* a failed job may rethrow so Hangfire records it */ }
            }
            finally { _app.PythonApi.Body = previous; }

            var meeting = _app.WithDb(db => db.MeetingSummaries.AsNoTracking().Single(m => m.Id == id));
            Assert.Equal(MeetingStatus.Failed, meeting.Status);
            Assert.True(File.Exists(audio), "the recording is kept so the user can retry");
        }

        [Fact]
        public async Task Owner_can_delete_a_team_that_has_meetings()
        {
            // A fresh team with a meeting, an action item and a comment, owned by Bob.
            var (teamId, meetingId) = _app.WithDb(db =>
            {
                var team = new Workspace { Name = "Silinecek Ekip" };
                db.Workspaces.Add(team);
                db.SaveChanges();
                db.WorkspaceUsers.Add(new WorkspaceUser { WorkspaceId = team.Id, UserId = S.BobId, Role = "Sahip" });
                var meeting = new MeetingSummary
                {
                    WorkspaceId = team.Id, ToplantiKonusu = "ekip toplantısı",
                    AksiyonMaddeleri = new List<ActionItem> { new() { Text = "görev" } }
                };
                db.MeetingSummaries.Add(meeting);
                db.SaveChanges();
                db.MeetingComments.Add(new MeetingComment { MeetingId = meeting.Id, UserId = S.BobId, Content = "yorum" });
                db.SaveChanges();
                return (team.Id, meeting.Id);
            });
            var bob = _app.ClientFor(S.BobId);
            await bob.GetAsync($"/Workspace/Switch?workspaceId={teamId}&returnUrl=/Workspace/Members");
            var token = await GetAntiforgeryTokenAsync(bob, "/Workspace/Members");

            var res = await PostFormAsync(bob, "/Workspace/Members?handler=DeleteWorkspace", token);

            Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
            Assert.False(_app.WithDb(db => db.Workspaces.Any(w => w.Id == teamId)));
            Assert.False(_app.WithDb(db => db.MeetingSummaries.Any(m => m.Id == meetingId)));
        }

        [Fact]
        public async Task Too_large_upload_gets_a_clear_message()
        {
            var alice = _app.ClientFor(S.AliceId);
            var token = await GetAntiforgeryTokenAsync(alice, "/Upload");
            using var form = new MultipartFormDataContent
            {
                { new ByteArrayContent(new byte[2 * 1024 * 1024]), "UploadFile", "cok-buyuk.m4a" }, // test limit is 1 MB
                { new StringContent(token), "__RequestVerificationToken" }
            };

            var res = await alice.PostAsync("/Upload", form);
            var page = WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Contains("Dosya çok büyük", page);
            Assert.False(_app.WithDb(db => db.MeetingSummaries.AsEnumerable().Any(m => m.ToplantiKonusu == "cok-buyuk.m4a")));
        }

        [Fact]
        public async Task Unknown_page_shows_a_friendly_not_found_page()
        {
            var res = await _app.ClientFor(S.AliceId).GetAsync("/boyle-bir-sayfa-yok");
            var page = WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.Contains("Aradığınız sayfa bulunamadı", page);
        }
    }
}
