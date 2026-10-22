using System.Net;
using MeetingWeb.Models;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features;

public class SeriesPagesTests : IAsyncLifetime
{
    private readonly BriefynFactory _app = new();
    private SeedData S => _app.Seed;

    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private int AddSeries(int workspaceId, string creator, string name) => _app.WithDb(db =>
    {
        var series = new MeetingSeries { WorkspaceId = workspaceId, CreatedByUserId = creator, Name = name };
        db.MeetingSeries.Add(series);
        db.SaveChanges();
        return series.Id;
    });

    private int? SeriesOf(int meetingId) => _app.WithDb(db => db.Meetings.AsNoTracking().Single(m => m.Id == meetingId).SeriesId);
    private static async Task<string> Html(HttpResponseMessage response) => WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task Index_lists_only_active_team_and_creates_a_series()
    {
        AddSeries(S.WorkspaceA, S.AliceId, "A serisi");
        AddSeries(S.WorkspaceB, S.BobId, "B gizli seri");
        using var carol = _app.ClientFor(S.CarolId);
        var html = await Html(await carol.GetAsync("/Series"));
        Assert.Contains("A serisi", html);
        Assert.DoesNotContain("B gizli seri", html);

        var token = await GetAntiforgeryTokenAsync(carol, "/Profile");
        var response = await PostFormAsync(carol, "/Series?handler=Create", token, ("name", "Yeni seri"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Yeni seri", await Html(await carol.GetAsync("/Series")));
    }

    [Fact]
    public async Task Show_hides_other_team_and_lists_own_meetings()
    {
        var own = AddSeries(S.WorkspaceA, S.AliceId, "Bizim seri");
        var other = AddSeries(S.WorkspaceB, S.BobId, "Gizli seri");
        _app.WithDb(db => { db.Meetings.Single(m => m.Id == S.MeetingA).SeriesId = own; return db.SaveChanges(); });
        using var alice = _app.ClientFor(S.AliceId);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/Series/{other}")).StatusCode);
        var response = await alice.GetAsync($"/Series/{own}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Html(response);
        Assert.Contains("Bizim seri", html);
        Assert.Contains("A gizli toplantı", html);
    }

    [Fact]
    public async Task Only_creator_or_team_manager_can_rename_and_delete()
    {
        var own = AddSeries(S.WorkspaceA, S.AliceId, "Alice serisi");
        var other = AddSeries(S.WorkspaceB, S.BobId, "Bob serisi");
        using var carol = _app.ClientFor(S.CarolId);
        var token = await GetAntiforgeryTokenAsync(carol, "/Profile");
        Assert.Equal(HttpStatusCode.NotFound, (await PostFormAsync(carol, $"/Series/{other}?handler=Rename", token, ("name", "Sızma"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostFormAsync(carol, $"/Series/{other}?handler=Delete", token)).StatusCode);
        await PostFormAsync(carol, $"/Series/{own}?handler=Delete", token);
        Assert.True(_app.WithDb(db => db.MeetingSeries.Any(s => s.Id == own)));

        using var alice = _app.ClientFor(S.AliceId);
        token = await GetAntiforgeryTokenAsync(alice, "/Profile");
        await PostFormAsync(alice, $"/Series/{own}?handler=Rename", token, ("name", "Yeni ad"));
        Assert.Equal("Yeni ad", _app.WithDb(db => db.MeetingSeries.Single(s => s.Id == own).Name));
        _app.WithDb(db => { db.Meetings.Single(m => m.Id == S.MeetingA).SeriesId = own; return db.SaveChanges(); });
        await PostFormAsync(alice, $"/Series/{own}?handler=Delete", token);
        Assert.False(_app.WithDb(db => db.MeetingSeries.Any(s => s.Id == own)));
        Assert.Null(SeriesOf(S.MeetingA));
    }

    [Fact]
    public async Task Details_links_unlinks_and_rejects_another_teams_series()
    {
        var own = AddSeries(S.WorkspaceA, S.AliceId, "Kendi seri");
        var other = AddSeries(S.WorkspaceB, S.BobId, "Başka seri");
        _app.WithDb(db => { db.Meetings.Single(m => m.Id == S.MeetingA).Status = MeetingStatus.Completed; return db.SaveChanges(); });
        using var alice = _app.ClientFor(S.AliceId);
        var token = await GetAntiforgeryTokenAsync(alice, "/Profile");
        var url = $"/Details?handler=SetSeries&meetingId={S.MeetingA}";
        await PostFormAsync(alice, url, token, ("seriesId", own.ToString()));
        Assert.Equal(own, SeriesOf(S.MeetingA));
        Assert.Contains("Sıradaki gündem", await Html(await alice.GetAsync($"/Details?id={S.MeetingA}")));
        await PostFormAsync(alice, url, token, ("seriesId", other.ToString()));
        Assert.Equal(own, SeriesOf(S.MeetingA));
        await PostFormAsync(alice, url, token, ("seriesId", ""));
        Assert.Null(SeriesOf(S.MeetingA));
    }

    [Fact]
    public async Task Upload_accepts_own_series_and_rejects_other_team_before_creating_meeting()
    {
        var own = AddSeries(S.WorkspaceA, S.AliceId, "Yükleme serisi");
        var other = AddSeries(S.WorkspaceB, S.BobId, "Başka seri");
        using var alice = _app.ClientFor(S.AliceId);
        var token = await GetAntiforgeryTokenAsync(alice, "/Profile");
        async Task<HttpResponseMessage> Upload(int seriesId)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(token), "__RequestVerificationToken");
            form.Add(new StringContent(seriesId.ToString()), "SeriesId");
            form.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "UploadFile", "test.mp3");
            return await alice.PostAsync("/Upload", form);
        }

        var before = _app.WithDb(db => db.Meetings.Count());
        var rejected = await Upload(other);
        Assert.Contains("Seçilen seri bulunamadı.", await Html(rejected));
        Assert.Equal(before, _app.WithDb(db => db.Meetings.Count()));
        await Upload(own);
        Assert.Equal(before + 1, _app.WithDb(db => db.Meetings.Count()));
        Assert.Equal(own, _app.WithDb(db => db.Meetings.OrderByDescending(m => m.Id).First().SeriesId));
    }

    [Fact]
    public async Task History_shows_series_badge()
    {
        var series = AddSeries(S.WorkspaceA, S.AliceId, "Haftalık seri");
        _app.WithDb(db => { db.Meetings.Single(m => m.Id == S.MeetingA).SeriesId = series; return db.SaveChanges(); });
        using var alice = _app.ClientFor(S.AliceId);
        var html = await Html(await alice.GetAsync("/History"));
        Assert.Contains("Haftalık seri", html);
        Assert.Contains($"/Series/{series}", html);
    }
}
