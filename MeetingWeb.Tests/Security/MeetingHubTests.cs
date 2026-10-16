using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingWeb.Tests.Security
{
    public class MeetingHubFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // S3: real-time notifications must reach only the members of the meeting's workspace.
    public class MeetingHubTests : IClassFixture<MeetingHubFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public MeetingHubTests(MeetingHubFixture fixture) => _app = fixture.Factory;

        private HubConnection Connect(string? userId) => new HubConnectionBuilder()
            .WithUrl("https://localhost/meetingHub", o =>
            {
                o.HttpMessageHandlerFactory = _ => _app.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling; // TestServer has no WebSocket transport
                if (userId != null) o.Headers[TestAuthHandler.UserHeader] = userId;
            })
            .Build();

        private static TaskCompletionSource<int> Listen(HubConnection connection)
        {
            var received = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.On<int>("MeetingProcessed", id => received.TrySetResult(id));
            return received;
        }

        private async Task ProcessNewMeetingInWorkspaceAAsync()
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var audio = Path.Combine(_app.AudioRoot, Guid.NewGuid() + ".m4a");
            File.WriteAllBytes(audio, new byte[] { 1 });
            var meeting = new MeetingSummary { WorkspaceId = S.WorkspaceA, ToplantiKonusu = "hub", AudioFilePath = Path.GetFileName(audio) };
            db.MeetingSummaries.Add(meeting);
            await db.SaveChangesAsync();

            await scope.ServiceProvider.GetRequiredService<IMeetingProcessor>().ProcessMeetingJob(meeting.Id, audio);
        }

        [Fact]
        public async Task Anonymous_clients_cannot_connect()
        {
            await using var anonymous = Connect(null);
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => anonymous.StartAsync());
        }

        [Fact]
        public async Task Only_members_of_the_meetings_workspace_are_notified()
        {
            await using var carol = Connect(S.CarolId);   // member of workspace A
            await using var bob = Connect(S.BobId);       // workspace B only
            var carolGot = Listen(carol);
            var bobGot = Listen(bob);
            await carol.StartAsync();
            await bob.StartAsync();

            await ProcessNewMeetingInWorkspaceAAsync();

            Assert.Same(carolGot.Task, await Task.WhenAny(carolGot.Task, Task.Delay(TimeSpan.FromSeconds(10))));
            // Carol got hers; give Bob's long-polling loop a moment, then make sure nothing arrived.
            await Task.Delay(TimeSpan.FromSeconds(1));
            Assert.False(bobGot.Task.IsCompleted, "a user outside the workspace received the notification");
        }
    }
}
