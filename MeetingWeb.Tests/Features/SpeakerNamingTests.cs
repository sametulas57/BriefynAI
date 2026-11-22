using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    private int NewMeeting(MeetingStatus status = MeetingStatus.Completed, string? transcript = Transcript, string? suggestions = null) => _app.WithDb(db =>
    {
        var meeting = new Meeting
        {
            WorkspaceId = S.WorkspaceA,
            Title = "konuşmacı adları testi",
            Status = status,
            FailureReason = status == MeetingStatus.Failed ? MeetingFailureReason.NoSpeech : null,
            Transcript = transcript == null ? null : new MeetingTranscript { Text = transcript, SpeakerSuggestions = suggestions }
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
    public async Task Completed_rename_preserves_assigned_task_and_updates_only_ai_text()
    {
        var id = NewMeeting();
        var taskId = _app.WithDb(db =>
        {
            var m = db.Meetings.Single(m => m.Id == id);
            m.Title = "Konuşmacı 1'in toplantısı";
            m.Points.Add(new MeetingPoint { Text = "Konuşmacı 1'in önerisi kabul edildi", Kind = MeetingPointKind.Decision });
            m.ActionItems.Add(new ActionItem { Text = "Konuşmacı 1 raporu hazırlayacak", AssigneeUserId = S.CarolId });
            m.ActionItems.Add(new ActionItem { Text = "Konuşmacı 1 ile görüş", Source = EntrySource.Manual });
            db.SaveChanges();
            return m.ActionItems.Single(a => a.Source == EntrySource.Ai).Id;
        });
        using var scope = _app.Services.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISpeakerNamingService>()
            .RenameAsync(S.CarolId, id, new[] { "Konuşmacı 1" }, new[] { "Mehmet" });

        var meeting = _app.WithDb(db => db.Meetings.Include(m => m.Points).Include(m => m.ActionItems).Single(m => m.Id == id));
        var task = Assert.Single(meeting.ActionItems, a => a.Source == EntrySource.Ai);
        Assert.Equal("Mehmet raporu hazırlayacak", task.Text);
        Assert.Equal(taskId, task.Id);
        Assert.Equal(S.CarolId, task.AssigneeUserId);
        Assert.Equal("Mehmet'in önerisi kabul edildi", Assert.Single(meeting.Points).Text);
        Assert.Equal("Mehmet'in toplantısı", meeting.Title);
        Assert.Equal("Konuşmacı 1 ile görüş", meeting.ActionItems.Single(a => a.Source == EntrySource.Manual).Text);
        Assert.Equal(SpeakerNamingResult.Renamed, result);
        Assert.Equal(MeetingStatus.Completed, meeting.Status);
        Assert.Equal(0, Jobs(id));
    }

    [Fact]
    public async Task Saved_names_remain_in_form_and_can_be_corrected()
    {
        var id = NewMeeting(suggestions: """{"Konuşmacı 1":{"ad":"Tahmin","gerekce":"Bir ipucu"}}""");
        _app.WithDb(db =>
        {
            var m = db.Meetings.Single(m => m.Id == id);
            m.Points.Add(new MeetingPoint { Text = "Konuşmacı 1 karar verdi", Kind = MeetingPointKind.Decision });
            m.ActionItems.Add(new ActionItem { Text = "Konuşmacı 1 raporu hazırlayacak" });
            return db.SaveChanges();
        });
        using var first = await RenameAsync(id, "Ali", "Veli");
        using var client = _app.ClientFor(S.CarolId);
        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/Details?id={id}"));
        Assert.Contains("speaker-naming-form", page);
        Assert.Contains("value=\"Ali\"", Regex.Match(page, "<input[^>]*id=\"speaker-name-0\"[^>]*>").Value);
        Assert.Contains("value=\"Veli\"", Regex.Match(page, "<input[^>]*id=\"speaker-name-1\"[^>]*>").Value);
        Assert.DoesNotContain("data-suggested", Regex.Match(page, "<input[^>]*id=\"speaker-name-0\"[^>]*>").Value);
        Assert.DoesNotContain("id=\"speaker-guess-0\"", page);
        Assert.DoesNotContain("id=\"speaker-suggestion-0\"", page);

        using var correction = await RenameAsync(id, "Mehmet", "");

        Assert.StartsWith("Mehmet: ", Load(id).Transcript!.Text);
        Assert.Contains("Veli: Merhaba.", Load(id).Transcript!.Text);
        Assert.Equal("Mehmet karar verdi", _app.WithDb(db => db.MeetingPoints.Single(p => p.MeetingId == id).Text));
        Assert.Equal("Mehmet raporu hazırlayacak", _app.WithDb(db => db.ActionItems.Single(a => a.MeetingId == id).Text));
    }

    [Fact]
    public async Task Merged_turns_can_be_separated_from_original_labels()
    {
        var id = NewMeeting(transcript: "Konuşmacı 1: a\nKonuşmacı 2: b");
        using var first = await RenameAsync(id, "Ali", "Ali");
        Assert.Equal("Ali: a b", Load(id).Transcript!.Text);

        using var correction = await RenameAsync(id, "", "Veli");

        Assert.Equal("Ali: a\nVeli: b", Load(id).Transcript!.Text);
    }

    [Fact]
    public async Task Name_casing_merges_using_the_lowest_numbered_labels_spelling()
    {
        var id = NewMeeting(transcript: "Konuşmacı 1: a\nKonuşmacı 2: b");

        using var response = await RenameAsync(id, "ali", "Ali");

        Assert.Equal("ali: a b", Load(id).Transcript!.Text);
        var json = Load(id).Transcript!.SpeakerNames!;
        Assert.All(JsonSerializer.Deserialize<Dictionary<string, string>>(json)!.Values, name => Assert.Equal("ali", name));
    }

    [Fact]
    public async Task Adjacent_named_turns_merge_in_the_middle_of_the_transcript()
    {
        var id = NewMeeting(transcript: "Konuşmacı 3: x\nKonuşmacı 1: a\nKonuşmacı 2: b");
        using var response = await RenameAsync(id, "Murat", "Murat");
        Assert.Equal("Konuşmacı 3: x\nMurat: a b", Load(id).Transcript!.Text);
    }

    [Fact]
    public async Task Failed_rename_returns_resummarizing_result()
    {
        var id = NewMeeting(MeetingStatus.Failed);
        using var scope = _app.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISpeakerNamingService>()
            .RenameAsync(S.CarolId, id, new[] { "Konuşmacı 1" }, new[] { "Mehmet" });
        Assert.Equal(SpeakerNamingResult.RenamedAndResummarizing, result);
        Assert.StartsWith("Mehmet: ", Load(id).Transcript!.Text);
        Assert.Equal(MeetingStatus.Processing, Load(id).Status);
        Assert.Equal(1, Jobs(id));
    }

    [Theory]
    [InlineData("Ali\u202e")]
    [InlineData("Ali\u200b")]
    public async Task Format_characters_are_invalid_names(string name)
    {
        var id = NewMeeting();
        using var scope = _app.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISpeakerNamingService>()
            .RenameAsync(S.CarolId, id, new[] { "Konuşmacı 1" }, new[] { name });
        Assert.Equal(SpeakerNamingResult.InvalidName, result);
        Assert.Equal(Transcript, Load(id).Transcript!.Text);
        Assert.Equal(0, Jobs(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ali")]
    public async Task Blank_or_unchanged_saved_name_does_not_rename(string name)
    {
        var id = NewMeeting();
        _app.WithDb(db =>
        {
            var transcript = db.MeetingTranscripts.Single(t => t.MeetingId == id);
            transcript.OriginalText = Transcript;
            transcript.Text = Transcript.Replace("Konuşmacı 1: ", "Ali: ");
            transcript.SpeakerNames = """{"Konuşmacı 1":"Ali"}""";
            return db.SaveChanges();
        });
        using var scope = _app.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISpeakerNamingService>()
            .RenameAsync(S.CarolId, id, new[] { "Konuşmacı 1" }, new[] { name });
        Assert.Equal(SpeakerNamingResult.NothingToRename, result);
        Assert.Equal("Ali", JsonSerializer.Deserialize<Dictionary<string, string>>(Load(id).Transcript!.SpeakerNames!)!["Konuşmacı 1"]);
        Assert.Equal(MeetingStatus.Completed, Load(id).Status);
        Assert.Equal(0, Jobs(id));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task Malformed_saved_names_do_not_prevent_naming(string json)
    {
        var id = NewMeeting();
        _app.WithDb(db => { db.MeetingTranscripts.Single(t => t.MeetingId == id).SpeakerNames = json; return db.SaveChanges(); });
        using var response = await RenameAsync(id, "Ali", "Veli");
        Assert.Equal("Ali", JsonSerializer.Deserialize<Dictionary<string, string>>(Load(id).Transcript!.SpeakerNames!)!["Konuşmacı 1"]);
    }

    [Fact]
    public async Task Splitting_a_shared_name_preserves_ambiguous_summary_text()
    {
        var id = NewMeeting(transcript: "Konuşmacı 1: a\nKonuşmacı 2: b");
        using var first = await RenameAsync(id, "Ali", "Ali");
        _app.WithDb(db =>
        {
            var m = db.Meetings.Single(m => m.Id == id);
            m.Title = "Ali'nin toplantısı";
            m.Points.Add(new MeetingPoint { Text = "Ali karar verdi", Kind = MeetingPointKind.Decision });
            m.ActionItems.Add(new ActionItem { Text = "Ali raporu hazırlayacak" });
            return db.SaveChanges();
        });
        using var correction = await RenameAsync(id, "", "Veli");
        Assert.Equal("Ali: a\nVeli: b", Load(id).Transcript!.Text);
        Assert.Equal("Ali'nin toplantısı", Load(id).Title);
        Assert.Equal("Ali karar verdi", _app.WithDb(db => db.MeetingPoints.Single(p => p.MeetingId == id).Text));
        Assert.Equal("Ali raporu hazırlayacak", _app.WithDb(db => db.ActionItems.Single(a => a.MeetingId == id).Text));
    }

    [Fact]
    public async Task Original_transcript_and_saved_names_are_encrypted()
    {
        var id = NewMeeting();
        using var response = await RenameAsync(id, "Ali", "Veli");
        Assert.Equal(Transcript, Load(id).Transcript!.OriginalText);
        foreach (var sql in new[] { "SELECT \"OriginalText\" AS \"Value\" FROM \"MeetingTranscripts\" WHERE \"MeetingId\" = {0}", "SELECT \"SpeakerNames\" AS \"Value\" FROM \"MeetingTranscripts\" WHERE \"MeetingId\" = {0}" })
        {
            var raw = _app.WithDb(db => db.Database.SqlQueryRaw<string>(
                sql, id).AsEnumerable().Single());
            Assert.StartsWith("v3:1:", raw);
            Assert.DoesNotContain("Ali", raw);
            Assert.DoesNotContain("Konuşmacı", raw);
        }
    }

    [Theory]
    [InlineData("Konuşmacı 1'in önerisi", "Konuşmacı 1", "Mehmet", "Mehmet'in önerisi")]
    [InlineData("Konuşmacı 2'nin raporu", "Konuşmacı 2", "Ayşe", "Ayşe'nin raporu")]
    [InlineData("Konuşmacı 3'e iletilecek", "Konuşmacı 3", "Ali", "Ali'ye iletilecek")]
    [InlineData("Konuşmacı 3'e iletilecek", "Konuşmacı 3", "Burak", "Burak'a iletilecek")]
    [InlineData("Konuşmacı 1'den", "Konuşmacı 1", "Zeynep", "Zeynep'ten")]
    [InlineData("Konuşmacı 1'den", "Konuşmacı 1", "Ali", "Ali'den")]
    [InlineData("Konuşmacı 1'le", "Konuşmacı 1", "Murat", "Murat'la")]
    [InlineData("Konuşmacı 1'le", "Konuşmacı 1", "Ece", "Ece'yle")]
    [InlineData("Konuşmacı 1'i", "Konuşmacı 1", "Can Öztürk", "Can Öztürk'ü")]
    [InlineData("Konuşmacı 1’in", "Konuşmacı 1", "Mehmet", "Mehmet’in")]
    [InlineData("Konuşmacı 1 ve Konuşmacı 10", "Konuşmacı 1", "Ali", "Ali ve Konuşmacı 10")]
    [InlineData("Alişan geldi", "Ali", "Can", "Alişan geldi")]
    [InlineData("Konuşmacı 1'ler", "Konuşmacı 1", "Ali", "Ali'ler")]
    [InlineData("Ali'de", "Ali", "Burak", "Burak'ta")]
    [InlineData("Ali'nin", "Ali", "Özgür", "Özgür'ün")]
    [InlineData("Ali'ye", "Ali", "Umut", "Umut'a")]
    [InlineData("Ali'nin", "Ali", "XYZ", "XYZ'nin")]
    [InlineData("Ali'nin", "Ali", "Ali 2", "Ali 2'nin")]
    [InlineData("Ali'ninX", "Ali", "Ece", "Ece'ninX")]
    [InlineData("1Ali Ali2", "Ali", "Can", "1Ali Ali2")]
    public void Speaker_suffixes_follow_the_new_names_last_word(string text, string oldName, string newName, string expected)
    {
        Assert.Equal(expected, TurkishNameSuffix.ReplaceSpeakerNames(text, new Dictionary<string, string> { [oldName] = newName }));
    }

    [Fact]
    public void Speaker_names_are_swapped_in_one_pass_with_longest_names_first()
    {
        Assert.Equal("Veli ile Ali", TurkishNameSuffix.ReplaceSpeakerNames("Ali ile Veli",
            new Dictionary<string, string> { ["Ali"] = "Veli", ["Veli"] = "Ali" }));
        Assert.Equal("Can ve Ece", TurkishNameSuffix.ReplaceSpeakerNames("Ali Can ve Ali",
            new Dictionary<string, string> { ["Ali"] = "Ece", ["Ali Can"] = "Can" }));
    }

    [Fact]
    public void Labels_only_finds_line_prefixes_in_first_appearance_order()
    {
        Assert.Equal(new[] { "Konuşmacı 2", "Konuşmacı 1" }, SpeakerNamingService.Labels(
            "Konuşmacı 2: Merhaba.\r\nKonuşmacı 1: Cümlede Konuşmacı 3: geçiyor.\nKonuşmacı 2: Yine ben.\n Konuşmacı 4: girintili\nKonuşmacı 5:boşluksuz"));
        Assert.Empty(SpeakerNamingService.Labels("Herkese günaydın."));
        Assert.Empty(SpeakerNamingService.Labels(null));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("?")]
    [InlineData("!")]
    [InlineData("…")]
    public void Rows_use_first_turn_and_stop_at_the_first_sentence(string punctuation)
    {
        var rows = SpeakerNamingService.Rows(new MeetingTranscript { Text = $"Konuşmacı 2: İlk cümle{punctuation} İkinci cümle.\r\nKonuşmacı 1: Merhaba\nKonuşmacı 2: Sonraki tur." });

        Assert.Equal(new[] { "Konuşmacı 2", "Konuşmacı 1" }, rows.Select(r => r.Label));
        Assert.Equal("İlk cümle" + punctuation, rows[0].FirstLine);
        Assert.Equal("Merhaba", rows[1].FirstLine);
        Assert.Null(rows[0].SuggestedName);
    }

    [Fact]
    public void Rows_truncate_a_long_first_turn_at_140_characters()
    {
        var row = Assert.Single(SpeakerNamingService.Rows(new MeetingTranscript { Text = "Konuşmacı 1: " + new string('a', 150) + "." }));

        Assert.Equal(new string('a', 140) + "…", row.FirstLine);
    }

    [Fact]
    public void Rows_normalize_suggestions_and_limit_trimmed_reasons()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Konuşmacı 1"] = new { ad = "  Murat   Yılmaz  ", gerekce = "  " + new string('a', 210) + "  " },
            ["Konuşmacı 2"] = new { ad = new string('b', SpeakerNamingService.MaxNameLength), gerekce = "  " }
        });

        var rows = SpeakerNamingService.Rows(new MeetingTranscript { Text = Transcript, SpeakerSuggestions = json });

        Assert.Equal("Murat Yılmaz", rows[0].SuggestedName);
        Assert.Equal(new string('a', 200), rows[0].Reason);
        Assert.Equal(new string('b', SpeakerNamingService.MaxNameLength), rows[1].SuggestedName);
        Assert.Null(rows[1].Reason);
    }

    [Theory]
    [InlineData("Murat: Bey")]
    [InlineData("Murat\u0001")]
    [InlineData("Murat\nYılmaz")]
    [InlineData("Murat\tYılmaz")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("  ")]
    public void Rows_ignore_suggestions_with_invalid_names(string name)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Konuşmacı 1"] = new { ad = name, gerekce = "Bir gerekçe" }
        });

        var row = Assert.Single(SpeakerNamingService.Rows(new MeetingTranscript { Text = "Konuşmacı 1: Merhaba.", SpeakerSuggestions = json }));

        Assert.Null(row.SuggestedName);
        Assert.Null(row.Reason);
    }

    [Fact]
    public void Rows_ignore_suggestions_for_labels_no_longer_present()
    {
        var rows = SpeakerNamingService.Rows(new MeetingTranscript
        {
            Text = "Murat: Merhaba.\nKonuşmacı 2: Selam.",
            SpeakerSuggestions = """{"Konuşmacı 1":{"ad":"Murat","gerekce":"Adıyla hitap ediliyor."}}"""
        });

        var row = Assert.Single(rows);
        Assert.Equal("Konuşmacı 2", row.Label);
        Assert.Null(row.SuggestedName);
        Assert.Empty(SpeakerNamingService.Rows(null));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Konuşmacı 1\":null}")]
    [InlineData("{\"Konuşmacı 1\":{\"ad\":123}}")]
    public void Rows_ignore_invalid_or_empty_suggestion_json(string json)
    {
        var rows = SpeakerNamingService.Rows(new MeetingTranscript { Text = Transcript, SpeakerSuggestions = json });

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Null(row.SuggestedName));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Processing_upload_stores_suggestions_alongside_the_transcript(bool summarySucceeds)
    {
        var audio = Guid.NewGuid().ToString("N") + ".m4a";
        await File.WriteAllBytesAsync(Path.Combine(_app.AudioRoot, audio), new byte[] { 1 });
        var id = NewMeeting(MeetingStatus.Processing, transcript: null);
        _app.WithDb(db => { db.Meetings.Single(m => m.Id == id).AudioFileName = audio; return db.SaveChanges(); });
        var previousBody = _app.Ai.Body;
        _app.Ai.Body = JsonSerializer.Serialize(new
        {
            success = summarySucceeds,
            transkript = Transcript,
            summary = summarySucceeds ? new { toplanti_konusu = "Konu" } : null,
            konusmaci_onerileri = new Dictionary<string, object>
            {
                ["Konuşmacı 1"] = new { ad = "Murat", gerekce = "Ayşe adıyla hitap ediyor." }
            }
        });
        try
        {
            using var scope = _app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IMeetingProcessor>().ProcessMeetingJob(id);
        }
        finally { _app.Ai.Body = previousBody; }

        var transcript = Load(id).Transcript!;
        Assert.Equal(Transcript, transcript.Text);
        Assert.Equal("Murat", SpeakerNamingService.Rows(transcript)[0].SuggestedName);
        Assert.Equal(summarySucceeds ? MeetingStatus.Completed : MeetingStatus.Failed, Load(id).Status);
        var raw = _app.WithDb(db => db.Database.SqlQueryRaw<string>(
            "SELECT \"SpeakerSuggestions\" AS \"Value\" FROM \"MeetingTranscripts\" WHERE \"MeetingId\" = {0}", id).AsEnumerable().Single());
        Assert.StartsWith("v3:1:", raw);
        Assert.DoesNotContain("Murat", raw);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"konusmaci_onerileri\":null")]
    [InlineData(",\"konusmaci_onerileri\":{}")]
    public async Task Processing_upload_without_suggestions_stores_null(string field)
    {
        var audio = Guid.NewGuid().ToString("N") + ".m4a";
        await File.WriteAllBytesAsync(Path.Combine(_app.AudioRoot, audio), new byte[] { 1 });
        var id = NewMeeting(MeetingStatus.Processing, suggestions: """{"Konuşmacı 1":{"ad":"Eski ad"}}""");
        _app.WithDb(db => { db.Meetings.Single(m => m.Id == id).AudioFileName = audio; return db.SaveChanges(); });
        _app.WithDb(db =>
        {
            var transcript = db.MeetingTranscripts.Single(t => t.MeetingId == id);
            transcript.OriginalText = "Konuşmacı 1: Eski döküm";
            transcript.SpeakerNames = """{"Konuşmacı 1":"Eski ad"}""";
            return db.SaveChanges();
        });
        var previousBody = _app.Ai.Body;
        _app.Ai.Body = "{\"success\":true,\"transkript\":\"Konuşmacı 1: Merhaba.\",\"summary\":{}" + field + "}";
        try
        {
            using var scope = _app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IMeetingProcessor>().ProcessMeetingJob(id);
        }
        finally { _app.Ai.Body = previousBody; }

        Assert.Equal(MeetingStatus.Completed, Load(id).Status);
        Assert.Null(Load(id).Transcript!.SpeakerSuggestions);
        Assert.Null(Load(id).Transcript!.OriginalText);
        Assert.Null(Load(id).Transcript!.SpeakerNames);
    }

    [Theory]
    [InlineData("Adıyla hitap ediliyor.")]
    [InlineData(null)]
    public async Task Page_prefills_suggested_name_and_shows_first_line_and_guess(string? reason)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Konuşmacı 1"] = new { ad = "Murat", gerekce = reason }
        });
        var id = NewMeeting(suggestions: json);
        using var client = _app.ClientFor(S.CarolId);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/Details?id={id}"));

        var suggestedInput = Regex.Match(page, "<input[^>]*id=\"speaker-name-0\"[^>]*>").Value;
        Assert.Contains("value=\"Murat\"", suggestedInput);
        Assert.Contains("data-suggested=\"true\"", suggestedInput);
        Assert.Contains("class=\"speaker-first-line small text-muted\">Herkese günaydın.</div>", page);
        Assert.Contains("class=\"speaker-suggestion small\">Tahmin" + (reason == null ? "" : ": " + reason) + "</div>", page);
        // Found in the app (2026-11-20): Razor wrote data-reasonless="False", and the CSS hid every reason.
        var suggestionLine = Regex.Match(page, "<div id=\"speaker-suggestion-0\"[^>]*>").Value;
        Assert.Equal(reason == null, suggestionLine.Contains("data-reasonless"));
        var emptyInput = Regex.Match(page, "<input[^>]*id=\"speaker-name-1\"[^>]*>").Value;
        Assert.Contains("value=\"\"", emptyInput);
        Assert.DoesNotContain("data-suggested", emptyInput);
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
        Assert.Equal(0, Jobs(id));
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
        Assert.Equal(0, Jobs(id));
    }

    [Fact]
    public async Task Completed_rename_uses_the_row_version_after_claiming_processing()
    {
        var id = NewMeeting();
        // SQL Server changes rowversion on ExecuteUpdate; reproduce that boundary in SQLite.
        _app.WithDb(db => db.Database.ExecuteSqlRaw("""
            CREATE TRIGGER ClaimSpeakerRowVersion AFTER UPDATE OF Status ON Meetings
            WHEN OLD.Status <> 0 AND NEW.Status = 0
            BEGIN UPDATE Meetings SET RowVersion = randomblob(16) WHERE Id = NEW.Id; END;
            """));
        try
        {
            using var response = await RenameAsync(id, "Mehmet", "Veli");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(MeetingStatus.Completed, Load(id).Status);
            Assert.StartsWith("Mehmet: ", Load(id).Transcript!.Text);
        }
        finally { _app.WithDb(db => db.Database.ExecuteSqlRaw("DROP TRIGGER ClaimSpeakerRowVersion")); }
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
        var id = NewMeeting(MeetingStatus.Failed);
        using var response = await RenameAsync(id, "Murat", "Elif");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var raw = _app.WithDb(db => db.Database.SqlQueryRaw<string>(
            "SELECT \"Text\" AS \"Value\" FROM \"MeetingTranscripts\" WHERE \"MeetingId\" = {0}", id).AsEnumerable().Single());
        Assert.StartsWith("v3:1:", raw);
        using var scope = _app.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IMeetingProcessor>().ResummarizeMeetingJob(id);

        using var body = JsonDocument.Parse(_app.Ai.LastBody!);
        Assert.Equal(Load(id).Transcript!.Text, body.RootElement.GetProperty("transkript").GetString());
        Assert.StartsWith("Murat: ", body.RootElement.GetProperty("transkript").GetString());
        Assert.Equal(MeetingStatus.Completed, Load(id).Status);
    }

    [Theory]
    [InlineData(MeetingStatus.Completed)]
    [InlineData(MeetingStatus.Failed)]
    public async Task Member_can_rename_line_prefixes_and_only_failed_meetings_queue_a_summary(MeetingStatus status)
    {
        var id = NewMeeting(status);

        using var response = await RenameAsync(id, "  Murat   Yılmaz  ", "Elif");

        await AssertMessageAsync(response, id, status == MeetingStatus.Completed ? "Adlar kaydedildi."
            : "Adlar kaydedildi. Özet bu adlarla yeniden hazırlanıyor; bittiğinde bu sayfa kendiliğinden yenilenir.");
        var meeting = Load(id);
        Assert.Equal("Murat Yılmaz: Herkese günaydın.\r\nElif: Merhaba.\nMurat Yılmaz: Başlayalım. Cümlede Konuşmacı 1: aynı kalır.", meeting.Transcript!.Text);
        Assert.Equal(status == MeetingStatus.Completed ? MeetingStatus.Completed : MeetingStatus.Processing, meeting.Status);
        Assert.Null(meeting.FailureReason);
        Assert.Equal(status == MeetingStatus.Completed ? 0 : 1, Jobs(id));
        Assert.Equal(0, _app.Jobs.CountFor("ProcessMeetingJob", a => (int)a[0]! == id));
    }

    [Fact]
    public async Task Blank_name_leaves_its_label_unchanged()
    {
        var id = NewMeeting();

        using var response = await RenameAsync(id, " ", "Elif");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Transcript.Replace("Konuşmacı 2: ", "Elif: "), Load(id).Transcript!.Text);
        Assert.Equal(0, Jobs(id));
    }

    [Fact]
    public async Task Two_labels_can_be_merged_into_one_name()
    {
        var id = NewMeeting();

        using var response = await RenameAsync(id, "Murat", "Murat");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("Murat: Herkese günaydın. Merhaba. Başlayalım. Cümlede Konuşmacı 1: aynı kalır.", Load(id).Transcript!.Text);
        Assert.Equal(0, Jobs(id));
    }

    [Fact]
    public async Task Non_adjacent_repeats_and_other_speakers_lines_are_not_merged()
    {
        var id = NewMeeting(transcript: "Konuşmacı 1: a\r\nKonuşmacı 2: b\nElif: c\r\nElif: d\nKonuşmacı 1: e");

        using var response = await RenameAsync(id, "Murat", "Murat");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("Murat: a b\nElif: c\r\nElif: d\nMurat: e", Load(id).Transcript!.Text);
    }

    [Fact]
    public async Task Blank_line_separates_turns_with_the_same_applied_name()
    {
        var id = NewMeeting(transcript: "Konuşmacı 1: a\n\nKonuşmacı 2: b\n");

        using var response = await RenameAsync(id, "Murat", "Murat");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("Murat: a\n\nMurat: b\n", Load(id).Transcript!.Text);
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
        var id = NewMeeting(MeetingStatus.Failed);
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
