using System.Net;
using System.Text.Json;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features;

public class SpeakerNamingFixture : IAsyncLifetime
{
    public BriefynFactory Factory { get; } = new();
    public Task InitializeAsync() => Factory.InitializeAsync();
    public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
}

public class SpeakerNamingTests : IClassFixture<SpeakerNamingFixture>
{
    private const string Transcript = "Konuşmacı 1: Herkese günaydın.\r\nKonuşmacı 2: Merhaba.\nKonuşmacı 1: Başlayalım. Cümlede Konuşmacı 1: aynı kalır.";
    private readonly BriefynFactory _app;
    private SeedData S => _app.Seed;

    public SpeakerNamingTests(SpeakerNamingFixture fixture) => _app = fixture.Factory;

    private int NewMeeting(MeetingStatus status = MeetingStatus.Completed, string? transcript = Transcript) => _app.WithDb(db =>
    {
        var meeting = new Meeting
        {
            WorkspaceId = S.WorkspaceA,
            Title = "konuşmacı adları testi",
            Status = status,
            FailureReason = status == MeetingStatus.Failed ? MeetingFailureReason.NoSpeech : null,
            Transcript = transcript == null ? null : new MeetingTranscript { Text = transcript }
        };
        db.Meetings.Add(meeting);
        db.SaveChanges();
        return meeting.Id;
    });

    private Meeting Load(int id) => _app.WithDb(db => db.Meetings
        .Include(m => m.Transcript).AsNoTracking().Single(m => m.Id == id));

    private int Jobs(int id) => _app.Jobs.CountFor("ResummarizeMeetingJob", a => (int)a[0]! == id);

    private async Task<HttpResponseMessage> RenameAsync(int id, string first, string second, string? userId = null)
    {
        using var client = _app.ClientFor(userId ?? S.CarolId);
        var tokenMeetingId = userId == S.BobId ? S.MeetingB : id;
        var token = await GetAntiforgeryTokenAsync(client, $"/Details?id={tokenMeetingId}");
        return await PostFormAsync(client, "/Details?handler=NameSpeakers", token,
            ("meetingId", id.ToString()), ("labels", "Konuşmacı 1"), ("names", first),
            ("labels", "Konuşmacı 2"), ("names", second));
    }

    private async Task AssertMessageAsync(HttpResponseMessage response, int id, string message)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Details?id={id}", response.Headers.Location!.OriginalString);
        // Use the same response cookie to read TempData on the redirected page.
        using var client = _app.ClientFor(S.CarolId);
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Select(c => c.Split(';')[0])));
        var page = WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location));
        Assert.Contains(message, page);
    }

    [Fact]
    public void Labels_only_finds_line_prefixes_in_first_appearance_order()
    {
        Assert.Equal(new[] { "Konuşmacı 2", "Konuşmacı 1" }, SpeakerNamingService.Labels(
            "Konuşmacı 2: Merhaba.\r\nKonuşmacı 1: Cümlede Konuşmacı 3: geçiyor.\nKonuşmacı 2: Yine ben.\n Konuşmacı 4: girintili\nKonuşmacı 5:boşluksuz"));
        Assert.Empty(SpeakerNamingService.Labels("Herkese günaydın."));
        Assert.Empty(SpeakerNamingService.Labels(null));
    }

    [Fact]
    public async Task Unknown_labels_are_ignored_even_if_their_names_are_invalid()
    {
        var id = NewMeeting();
        using var scope = _app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISpeakerNamingService>();

        var result = await service.RenameAsync(S.CarolId, id,
            new[] { "Konuşmacı 3", "Konuşmacı 2" }, new[] { "Geçersiz: ad", "Elif" });

        Assert.Equal(SpeakerNamingResult.Renamed, result);
        Assert.Equal(Transcript.Replace("Konuşmacı 2: ", "Elif: "), Load(id).Transcript!.Text);
        Assert.Equal(1, Jobs(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Etiketsiz döküm")]
    public async Task Transcript_without_labels_has_nothing_to_rename(string? transcript)
    {
        var id = NewMeeting(transcript: transcript);
        using var scope = _app.Services.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISpeakerNamingService>()
            .RenameAsync(S.CarolId, id, new[] { "Konuşmacı 1" }, new[] { "Murat" });

        Assert.Equal(SpeakerNamingResult.NothingToRename, result);
        Assert.Equal(transcript, Load(id).Transcript?.Text);
        Assert.Equal(MeetingStatus.Completed, Load(id).Status);
        Assert.Equal(0, Jobs(id));
    }

    [Fact]
    public async Task Missing_meeting_returns_not_found()
    {
        using var scope = _app.Services.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISpeakerNamingService>()
            .RenameAsync(S.CarolId, int.MaxValue, new[] { "Konuşmacı 1" }, new[] { "Murat" });

        Assert.Equal(SpeakerNamingResult.NotFound, result);
        Assert.Equal(0, Jobs(int.MaxValue));
    }

    [Fact]
    public async Task Conditional_update_rejects_a_request_that_loaded_the_previous_status()
    {
        var id = NewMeeting();
        using var firstScope = _app.Services.CreateScope();
        using var secondScope = _app.Services.CreateScope();
        // Both requests see Completed before either takes the processing slot.
        await firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Meetings.Include(m => m.Transcript).SingleAsync(m => m.Id == id);
        await secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Meetings.Include(m => m.Transcript).SingleAsync(m => m.Id == id);

        var first = await firstScope.ServiceProvider.GetRequiredService<ISpeakerNamingService>()
            .RenameAsync(S.CarolId, id, new[] { "Konuşmacı 1" }, new[] { "Murat" });
        var second = await secondScope.ServiceProvider.GetRequiredService<ISpeakerNamingService>()
            .RenameAsync(S.CarolId, id, new[] { "Konuşmacı 2" }, new[] { "Elif" });

        Assert.Equal(SpeakerNamingResult.Renamed, first);
        Assert.Equal(SpeakerNamingResult.Busy, second);
        Assert.StartsWith("Murat: ", Load(id).Transcript!.Text);
        Assert.Contains("Konuşmacı 2: ", Load(id).Transcript!.Text);
        Assert.Equal(1, Jobs(id));
    }

    [Theory]
    [InlineData(MeetingStatus.Completed)]
    [InlineData(MeetingStatus.Failed)]
    public async Task Failed_transcript_write_restores_previous_status(MeetingStatus status)
    {
        var id = NewMeeting(status);
        _app.WithDb(db => db.Database.ExecuteSqlRaw("""
            CREATE TRIGGER RejectSpeakerTranscript BEFORE UPDATE ON MeetingTranscripts
            BEGIN SELECT RAISE(ABORT, 'transcript write rejected'); END;
            """));
        try
        {
            using var scope = _app.Services.CreateScope();
            await Assert.ThrowsAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<ISpeakerNamingService>()
                .RenameAsync(S.CarolId, id, new[] { "Konuşmacı 1" }, new[] { "Murat" }));
        }
        finally { _app.WithDb(db => db.Database.ExecuteSqlRaw("DROP TRIGGER RejectSpeakerTranscript")); }

        Assert.Equal(status, Load(id).Status);
        Assert.Equal(status == MeetingStatus.Failed ? MeetingFailureReason.NoSpeech : (MeetingFailureReason?)null, Load(id).FailureReason);
        Assert.Equal(Transcript, Load(id).Transcript!.Text);
        Assert.Equal(0, Jobs(id));
    }

    [Fact]
    public async Task Regenerated_summary_receives_renamed_transcript_saved_with_encryption()
    {
        var id = NewMeeting();
        using var response = await RenameAsync(id, "Murat", "Elif");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var raw = _app.WithDb(db => db.Database.SqlQueryRaw<string>(
            "SELECT \"Text\" AS \"Value\" FROM \"MeetingTranscripts\" WHERE \"MeetingId\" = {0}", id).AsEnumerable().Single());
        Assert.StartsWith("v3:1:", raw);
        using var scope = _app.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IMeetingProcessor>().ResummarizeMeetingJob(id);

        using var body = JsonDocument.Parse(_app.PythonApi.LastBody!);
        Assert.Equal(Load(id).Transcript!.Text, body.RootElement.GetProperty("transkript").GetString());
        Assert.StartsWith("Murat: ", body.RootElement.GetProperty("transkript").GetString());
        Assert.Equal(MeetingStatus.Completed, Load(id).Status);
    }

    [Theory]
    [InlineData(MeetingStatus.Completed)]
    [InlineData(MeetingStatus.Failed)]
    public async Task Member_can_rename_line_prefixes_and_queue_one_summary(MeetingStatus status)
    {
        var id = NewMeeting(status);

        using var response = await RenameAsync(id, "  Murat   Yılmaz  ", "Elif");

        await AssertMessageAsync(response, id, "Adlar kaydedildi. Özet bu adlarla yeniden hazırlanıyor; bittiğinde bu sayfa kendiliğinden yenilenir.");
        var meeting = Load(id);
        Assert.Equal("Murat Yılmaz: Herkese günaydın.\r\nElif: Merhaba.\nMurat Yılmaz: Başlayalım. Cümlede Konuşmacı 1: aynı kalır.", meeting.Transcript!.Text);
        Assert.Equal(MeetingStatus.Processing, meeting.Status);
        Assert.Null(meeting.FailureReason);
        Assert.Equal(1, Jobs(id));
        Assert.Equal(0, _app.Jobs.CountFor("ProcessMeetingJob", a => (int)a[0]! == id));
    }

    [Fact]
    public async Task Blank_name_leaves_its_label_unchanged()
    {
        var id = NewMeeting();

        using var response = await RenameAsync(id, " ", "Elif");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Transcript.Replace("Konuşmacı 2: ", "Elif: "), Load(id).Transcript!.Text);
        Assert.Equal(1, Jobs(id));
    }

    [Fact]
    public async Task Two_labels_can_be_merged_into_one_name()
    {
        var id = NewMeeting();

        using var response = await RenameAsync(id, "Murat", "Murat");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("Murat: Herkese günaydın.\r\nMurat: Merhaba.\nMurat: Başlayalım. Cümlede Konuşmacı 1: aynı kalır.", Load(id).Transcript!.Text);
        Assert.Equal(1, Jobs(id));
    }

    [Theory]
    [InlineData("Murat: Bey")]
    [InlineData("Murat\u0001")]
    [InlineData("Murat\nYılmaz")]
    [InlineData("Murat\tYılmaz")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Invalid_name_rejects_all_pairs_without_changes(string invalid)
    {
        var id = NewMeeting(MeetingStatus.Failed);

        using var response = await RenameAsync(id, "Murat", invalid);

        await AssertMessageAsync(response, id, "Ad en çok 60 karakter olabilir ve iki nokta (:) içeremez.");
        Assert.Equal(Transcript, Load(id).Transcript!.Text);
        Assert.Equal(MeetingStatus.Failed, Load(id).Status);
        Assert.Equal(MeetingFailureReason.NoSpeech, Load(id).FailureReason);
        Assert.Equal(0, Jobs(id));
    }

    [Fact]
    public async Task All_blank_names_leave_everything_unchanged()
    {
        var id = NewMeeting();

        using var response = await RenameAsync(id, "  ", "");

        await AssertMessageAsync(response, id, "En az bir konuşmacıya ad yazın.");
        Assert.Equal(Transcript, Load(id).Transcript!.Text);
        Assert.Equal(MeetingStatus.Completed, Load(id).Status);
        Assert.Equal(0, Jobs(id));
    }

    [Fact]
    public async Task Processing_meeting_cannot_be_renamed()
    {
        var id = NewMeeting(MeetingStatus.Processing);

        using var response = await RenameAsync(id, "Murat", "Elif");

        await AssertMessageAsync(response, id, "Bu toplantı şu anda işleniyor. Bittikten sonra adları yeniden kaydedin.");
        Assert.Equal(Transcript, Load(id).Transcript!.Text);
        Assert.Equal(MeetingStatus.Processing, Load(id).Status);
        Assert.Equal(0, Jobs(id));
    }

    [Fact]
    public async Task Second_request_does_not_queue_another_summary_or_overwrite_names()
    {
        var id = NewMeeting();
        using var first = await RenameAsync(id, "Murat", " ");

        using var second = await RenameAsync(id, "Mehmet", "Elif");

        await AssertMessageAsync(second, id, "Bu toplantı şu anda işleniyor. Bittikten sonra adları yeniden kaydedin.");
        Assert.Equal("Murat: Herkese günaydın.\r\nKonuşmacı 2: Merhaba.\nMurat: Başlayalım. Cümlede Konuşmacı 1: aynı kalır.", Load(id).Transcript!.Text);
        Assert.Equal(1, Jobs(id));
    }

    [Fact]
    public async Task Outsider_cannot_rename_another_workspaces_meeting()
    {
        var id = NewMeeting();

        using var response = await RenameAsync(id, "Murat", "Elif", S.BobId);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/History", response.Headers.Location!.OriginalString);
        Assert.Equal(Transcript, Load(id).Transcript!.Text);
        Assert.Equal(MeetingStatus.Completed, Load(id).Status);
        Assert.Equal(0, Jobs(id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Page_offers_form_only_when_transcript_has_labels(bool hasLabels)
    {
        var id = NewMeeting(transcript: hasLabels ? Transcript : "Herkese günaydın.");
        using var client = _app.ClientFor(S.CarolId);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/Details?id={id}"));

        if (hasLabels)
        {
            Assert.Contains("handler=NameSpeakers", page);
            Assert.Contains("name=\"labels\" value=\"Konuşmacı 1\"", page);
            Assert.Contains("name=\"labels\" value=\"Konuşmacı 2\"", page);
            Assert.Contains("name=\"names\"", page);
            Assert.Contains("maxlength=\"60\"", page);
            Assert.Contains("<datalist", page);
            Assert.Contains("Adları kaydet", page);
        }
        else Assert.DoesNotContain("NameSpeakers", page);
    }
}
