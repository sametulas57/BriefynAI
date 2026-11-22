using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingWeb.Tests.Features;

public class MeetingProcessorAiTests : IAsyncLifetime
{
    private readonly BriefynFactory _app = new();

    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Processor_passes_null_when_speaker_file_is_absent()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audio = Path.Combine(_app.AudioRoot, "kayit.m4a");
        File.WriteAllBytes(audio, new byte[] { 1 });
        var meeting = new Meeting { WorkspaceId = _app.Seed.WorkspaceA, Title = "kayıt", AudioFileName = "kayit.m4a" };
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<IMeetingProcessor>().ProcessMeetingJob(meeting.Id);

        Assert.Equal(1, _app.Ai.CallCount);
        Assert.Equal(audio, _app.Ai.LastAudioPath);
        Assert.Null(_app.Ai.LastSpeakersPath);
        Assert.Null(_app.Ai.LastSpeakersText);
    }

    [Fact]
    public async Task Processor_passes_speaker_file_path_and_contents_when_present()
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

        Assert.Equal(1, _app.Ai.CallCount);
        Assert.Equal(audio, _app.Ai.LastAudioPath);
        Assert.Equal(audio + ".vtt", _app.Ai.LastSpeakersPath);
        Assert.Equal("WEBVTT\n\nAli: söz", _app.Ai.LastSpeakersText);
    }
}
