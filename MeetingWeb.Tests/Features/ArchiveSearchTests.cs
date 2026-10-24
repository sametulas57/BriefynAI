using System.Text.RegularExpressions;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Services.ArchiveSearch;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace MeetingWeb.Tests.Features;

// Arşive soru sor, search half (PRD 2026-10-24 acceptance 1, 2, 6, 8), run on the made-up gold set.
public class ArchiveSearchTests : IAsyncLifetime
{
    // Share of answerable gold questions whose evidence is in the top 8 passages. Floor from the first measurement
    // (PRD §7): the test fails if search gets worse.
    private const double RecallFloor = 0.87; // 2026-10-24: 34/39

    private readonly BriefynFactory _app = new();
    private readonly ITestOutputHelper _output;
    private GoldSet G = null!;

    public ArchiveSearchTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        await _app.InitializeAsync();
        G = _app.WithDb(GoldSet.Load);
    }

    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private async Task<ArchiveSearchResult> Search(string? userId, int workspaceId, ArchiveQuery query)
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IArchiveSearchService>().SearchAsync(userId, workspaceId, query);
    }

    private Task<ArchiveSearchResult> Ask(string team, string question, ArchiveScope scope = ArchiveScope.Archive, int? target = null,
        DateOnly? from = null, DateOnly? to = null) =>
        Search(G.Askers[team], G.Workspaces[team], new ArchiveQuery(question, scope, target, from, to));

    private Task<ArchiveSearchResult> Ask(GoldSet.Question q) => q.ScopeKind switch
    {
        "seri" => Ask(q.Team, q.Text, ArchiveScope.Series, G.Series[q.ScopeTarget!]),
        "toplanti" => Ask(q.Team, q.Text, ArchiveScope.Meeting, G.Meetings[q.ScopeTarget!]),
        _ => Ask(q.Team, q.Text)
    };

    private static bool Contains(ArchiveSearchResult r, string text) => r.Passages.Any(p => GoldSet.Norm(p.Text).Contains(GoldSet.Norm(text)));

    // ---- acceptance 1: only the asker's team ----

    [Fact]
    public async Task Every_gold_question_returns_passages_only_from_the_askers_team()
    {
        foreach (var q in G.Questions)
        {
            var own = G.MeetingIdsOf(q.Team).ToHashSet();
            var result = await Ask(q);
            Assert.All(result.Passages, p => Assert.Contains(p.MeetingId, own));
        }
    }

    [Fact]
    public async Task Leak_questions_never_surface_the_other_teams_wording()
    {
        var leaks = G.Questions.Where(q => q.Kind == "sizma").ToList();
        Assert.NotEmpty(leaks);
        foreach (var q in leaks)
            Assert.False(Contains(await Ask(q), q.Trace!), q.Id);
    }

    [Fact]
    public async Task Other_teams_member_and_outsider_get_nothing()
    {
        var question = "Pusula'nın aylık abonelik fiyatı ne oldu?";
        var asB = await Search(G.Askers["B"], G.Workspaces["A"], new ArchiveQuery(question));
        Assert.Equal(0, asB.MeetingsSearched);
        Assert.Empty(asB.Passages);

        var outsider = await Search(_app.Seed.BobId, G.Workspaces["A"], new ArchiveQuery(question));
        Assert.Empty(outsider.Passages);
        Assert.Empty((await Search(null, G.Workspaces["A"], new ArchiveQuery(question))).Passages);
    }

    [Fact]
    public async Task Scope_pointing_at_another_teams_meeting_or_series_returns_nothing()
    {
        var bSeries = _app.WithDb(db =>
        {
            var s = new MeetingSeries { WorkspaceId = G.Workspaces["B"], Name = "Depo haftalık" };
            db.MeetingSeries.Add(s);
            db.SaveChanges();
            db.Meetings.Where(m => m.Id == G.Meetings["B1"]).ExecuteUpdate(u => u.SetProperty(m => m.SeriesId, s.Id));
            return s.Id;
        });

        var meeting = await Ask("A", "Depo kirası ne kadar?", ArchiveScope.Meeting, G.Meetings["B1"]);
        var series = await Ask("A", "Depo kirası ne kadar?", ArchiveScope.Series, bSeries);
        Assert.Equal(0, meeting.MeetingsSearched);
        Assert.Empty(meeting.Passages);
        Assert.Equal(0, series.MeetingsSearched);
        Assert.Empty(series.Passages);
    }

    // ---- acceptance 2: scope, date range, meeting count ----

    [Fact]
    public async Task Archive_series_and_meeting_scopes_limit_meetings_and_count_them()
    {
        var archive = await Ask("A", "Fiyat için hangi rakamı konuştuk?");
        Assert.Equal(8, archive.MeetingsSearched);
        Assert.NotEmpty(archive.Passages);

        var s1 = new[] { "A1", "A2", "A3", "A4" }.Select(id => G.Meetings[id]).ToHashSet();
        var series = await Ask("A", "Fiyat için hangi rakamı konuştuk?", ArchiveScope.Series, G.Series["S1"]);
        Assert.Equal(4, series.MeetingsSearched);
        Assert.NotEmpty(series.Passages);
        Assert.All(series.Passages, p => Assert.Contains(p.MeetingId, s1));

        var single = await Ask("A", "Fiyat için hangi rakamı konuştuk?", ArchiveScope.Meeting, G.Meetings["A1"]);
        Assert.Equal(1, single.MeetingsSearched);
        Assert.NotEmpty(single.Passages);
        Assert.All(single.Passages, p => Assert.Equal(G.Meetings["A1"], p.MeetingId));
    }

    [Fact]
    public async Task Date_range_is_inclusive_and_combines_with_series()
    {
        var from = new DateOnly(2026, 9, 17);
        var to = new DateOnly(2026, 9, 24);
        var expected = G.MeetingDate.Where(d => G.MeetingTeam[d.Key] == "A" && d.Value >= from && d.Value <= to)
            .Select(d => G.Meetings[d.Key]).ToHashSet(); // A3, A5, A6
        Assert.Equal(3, expected.Count);

        var ranged = await Ask("A", "Fiyat ve çıkış tarihi", from: from, to: to);
        Assert.Equal(3, ranged.MeetingsSearched);
        Assert.All(ranged.Passages, p => Assert.Contains(p.MeetingId, expected));

        var seriesFrom = await Ask("A", "Fiyat ve çıkış tarihi", ArchiveScope.Series, G.Series["S1"], from: new DateOnly(2026, 9, 21));
        Assert.Equal(2, seriesFrom.MeetingsSearched); // A3, A4
    }

    [Fact]
    public async Task Only_completed_meetings_are_searched()
    {
        _app.WithDb(db =>
        {
            db.Meetings.Add(new Meeting { WorkspaceId = G.Workspaces["A"], Title = "Yarım kalan zebercet toplantısı", Status = MeetingStatus.Processing,
                Points = new() { new() { Kind = MeetingPointKind.Decision, Text = "Zebercet işleniyor" } } });
            db.Meetings.Add(new Meeting { WorkspaceId = G.Workspaces["A"], Title = "Başarısız zebercet", Status = MeetingStatus.Failed,
                Transcript = new MeetingTranscript { Text = "Ayşe Kaya: Zebercet başarısız oldu." } });
            return db.SaveChanges();
        });

        var result = await Ask("A", "zebercet");
        Assert.Equal(8, result.MeetingsSearched);
        Assert.Empty(result.Passages);
    }

    [Fact]
    public async Task Passages_carry_unique_ids_and_their_meetings_title_and_date()
    {
        var result = await Ask("A", "Pusula 2.0 ne zaman çıkacak?");
        Assert.InRange(result.Passages.Count, 1, 8);
        Assert.Equal(result.Passages.Count, result.Passages.Select(p => p.Id).Distinct().Count());
        foreach (var p in result.Passages)
        {
            var match = Regex.Match(p.Id, @"^T(\d+)-([KRBAD])(\d+)$");
            Assert.True(match.Success, p.Id);
            Assert.Equal(p.MeetingId.ToString(), match.Groups[1].Value);
            Assert.Equal("KRBAD"[(int)p.Kind], match.Groups[2].Value[0]);
            Assert.False(string.IsNullOrWhiteSpace(p.Text));
            var gold = G.Meetings.Single(m => m.Value == p.MeetingId).Key;
            Assert.Equal(G.MeetingDate[gold], p.MeetingDate);
            Assert.False(string.IsNullOrWhiteSpace(p.MeetingTitle));
        }
        Assert.True(result.Passages.Zip(result.Passages.Skip(1)).All(pair => pair.First.Score >= pair.Second.Score), "en iyi parça önce");
    }

    [Fact]
    public async Task Turkish_suffixes_apostrophes_and_ascii_spelling_still_match()
    {
        // "Ayşe Kaya'nın" / "ayse kaya" / "abonelikler" must reach the same stems as the stored text.
        Assert.NotEmpty((await Ask("A", "ayse kaya ne dedi")).Passages);
        Assert.NotEmpty((await Ask("A", "Ayşe Kaya'nın kararları")).Passages);
        Assert.NotEmpty((await Ask("A", "aboneliklerin fiyatları")).Passages);
        Assert.Empty((await Ask("A", "   ")).Passages);
    }

    // ---- acceptance 6: retrieval quality on the gold set, no model involved ----

    [Fact]
    public async Task Gold_evidence_is_in_the_top_8_for_answerable_questions()
    {
        var answerable = G.Questions.Where(q => !GoldSet.Unanswered.Contains(q.Kind)).ToList();
        var misses = new List<string>();
        foreach (var q in answerable)
        {
            var result = await Ask(q);
            var found = q.Expected.Any(e => result.Passages.Take(8).Any(p =>
                p.MeetingId == G.Meetings[e.Meeting] && GoldSet.Norm(p.Text).Contains(GoldSet.Norm(e.Evidence))));
            if (!found) misses.Add($"{q.Id} ({q.Kind}): {q.Text}");
        }
        var recall = 1.0 - (double)misses.Count / answerable.Count;
        _output.WriteLine($"ilk 8'de doğru parça: {recall:P1} ({answerable.Count - misses.Count}/{answerable.Count})");
        foreach (var miss in misses) _output.WriteLine("  kaçan: " + miss);
        Assert.True(recall >= RecallFloor, $"ilk 8 isabet {recall:P1}, taban {RecallFloor:P0}\n" + string.Join("\n", misses));
    }

    // ---- acceptance 8: the index follows every change ----

    [Fact]
    public async Task New_completed_meeting_is_found_after_the_index_was_built()
    {
        Assert.Empty((await Ask("A", "zebercet")).Passages); // builds and caches the index

        _app.WithDb(db =>
        {
            db.Meetings.Add(new Meeting { WorkspaceId = G.Workspaces["A"], Title = "Zebercet planı", Status = MeetingStatus.Completed,
                CreatedAtUtc = new DateTime(2026, 10, 15, 7, 0, 0, DateTimeKind.Utc),
                Points = new() { new() { Kind = MeetingPointKind.Decision, Text = "Zebercet modülü kasımda başlayacak" } } });
            return db.SaveChanges();
        });

        var result = await Ask("A", "zebercet");
        Assert.Equal(9, result.MeetingsSearched);
        Assert.True(Contains(result, "Zebercet modülü kasımda başlayacak"));
    }

    [Fact]
    public async Task Meeting_that_finishes_processing_becomes_searchable()
    {
        var id = _app.WithDb(db =>
        {
            var m = new Meeting { WorkspaceId = G.Workspaces["A"], Title = "Zebercet", Status = MeetingStatus.Processing,
                Transcript = new MeetingTranscript { Text = "Mert Demir: Zebercet sunucusu hazır." } };
            db.Meetings.Add(m);
            db.SaveChanges();
            return m.Id;
        });
        Assert.Empty((await Ask("A", "zebercet sunucusu")).Passages);

        _app.WithDb(db => { db.Meetings.Single(m => m.Id == id).Status = MeetingStatus.Completed; return db.SaveChanges(); });

        Assert.True(Contains(await Ask("A", "zebercet sunucusu"), "Zebercet sunucusu hazır"));
    }

    [Fact]
    public async Task Edited_decision_and_renamed_meeting_replace_the_old_text()
    {
        var a1 = G.Meetings["A1"];
        Assert.True(Contains(await Ask("A", "abonelik fiyatı 149 TL"), "149 TL olacak"));

        _app.WithDb(db =>
        {
            var decision = db.MeetingPoints.Where(p => p.MeetingId == a1 && p.Kind == MeetingPointKind.Decision).AsEnumerable()
                .Single(p => p.Text.Contains("149 TL"));
            decision.Text = "Aylık abonelik fiyatı zebercet lira olacak";
            db.Meetings.Single(m => m.Id == a1).Title = "Kırmızı balon toplantısı";
            return db.SaveChanges();
        });

        Assert.False(Contains(await Ask("A", "abonelik fiyatı 149 TL"), "149 TL olacak"));
        Assert.True(Contains(await Ask("A", "zebercet lira"), "zebercet lira olacak"));
        Assert.Contains((await Ask("A", "kırmızı balon")).Passages, p => p.MeetingId == a1 && p.MeetingTitle == "Kırmızı balon toplantısı");
    }

    [Fact]
    public async Task Deleted_meeting_disappears()
    {
        Assert.Contains((await Ask("A", "abonelik fiyatı")).Passages, p => p.MeetingId == G.Meetings["A1"]);

        _app.WithDb(db => { db.Meetings.Remove(db.Meetings.Single(m => m.Id == G.Meetings["A1"])); return db.SaveChanges(); });

        var result = await Ask("A", "abonelik fiyatı");
        Assert.Equal(7, result.MeetingsSearched);
        Assert.DoesNotContain(result.Passages, p => p.MeetingId == G.Meetings["A1"]);
    }

    [Fact]
    public async Task Bulk_update_outside_change_tracking_is_seen_too()
    {
        // Series links are written with ExecuteUpdate (MeetingSeriesService), which skips SaveChanges.
        Assert.Equal(4, (await Ask("A", "fiyat", ArchiveScope.Series, G.Series["S1"])).MeetingsSearched);

        using (var scope = _app.Services.CreateScope())
        {
            var series = scope.ServiceProvider.GetRequiredService<IMeetingSeriesService>();
            Assert.Equal(SeriesResult.Ok, await series.SetMeetingSeriesAsync(G.Askers["A"], G.Meetings["A5"], G.Series["S1"]));
        }

        Assert.Equal(5, (await Ask("A", "fiyat", ArchiveScope.Series, G.Series["S1"])).MeetingsSearched);
    }

    [Fact]
    public async Task Removed_member_loses_access_at_once()
    {
        Assert.NotEmpty((await Ask("A", "abonelik fiyatı")).Passages);

        _app.WithDb(db => db.WorkspaceMembers.Where(m => m.UserId == G.Askers["A"]).ExecuteDelete());

        var result = await Ask("A", "abonelik fiyatı");
        Assert.Equal(0, result.MeetingsSearched);
        Assert.Empty(result.Passages);
    }
}
