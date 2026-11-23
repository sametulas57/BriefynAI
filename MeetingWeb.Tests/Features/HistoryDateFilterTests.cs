using System.Net;
using MeetingWeb.Models;
using MeetingWeb.Tests.Infrastructure;

namespace MeetingWeb.Tests.Features;

public class HistoryDateFilterTests : IAsyncLifetime
{
    private readonly BriefynFactory _app = new();

    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    [Theory]
    [InlineData(9, 22, "2026-03-10", true)]
    [InlineData(9, 22, "2026-03-09", false)]
    [InlineData(10, 20, "2026-03-10", true)]
    [InlineData(10, 20, "2026-03-11", false)]
    public async Task History_date_filter_uses_the_Turkish_calendar_day(
        int utcDay, int utcHour, string selectedDay, bool shouldAppear)
    {
        var title = $"History-date-filter-{Guid.NewGuid():N}";
        _app.WithDb(db =>
        {
            db.Meetings.Add(new Meeting
            {
                WorkspaceId = _app.Seed.WorkspaceA,
                Title = title,
                Status = MeetingStatus.Failed,
                CreatedAtUtc = new DateTime(2026, 3, utcDay, utcHour, 30, 0, DateTimeKind.Utc)
            });
            return db.SaveChanges();
        });
        using var alice = _app.ClientFor(_app.Seed.AliceId);

        using var response = await alice.GetAsync($"/History?StartDate={selectedDay}&EndDate={selectedDay}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        if (shouldAppear) Assert.Contains(title, html);
        else Assert.DoesNotContain(title, html);
    }
}
