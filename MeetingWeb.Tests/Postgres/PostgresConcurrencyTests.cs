using System.Net;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Postgres;

public class PostgresConcurrencyTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private BriefynFactory App => fixture.Factory;
    private SeedData S => App.Seed;

    private Meeting NewMeeting() => App.WithDb(db =>
    {
        var meeting = new Meeting
        {
            WorkspaceId = S.WorkspaceA,
            Title = "Konuşmacı 1 ile planlama",
            Status = MeetingStatus.Completed,
            Transcript = new MeetingTranscript { Text = "Konuşmacı 1: Herkese günaydın.\nKonuşmacı 2: Merhaba." },
            ActionItems = new List<ActionItem> { new() { Text = "Raporu hazırla" } }
        };
        db.Meetings.Add(meeting);
        db.SaveChanges();
        return meeting;
    });

    [PostgresFact]
    public async Task Stale_action_item_save_throws_and_keeps_the_first_writers_value()
    {
        var taskId = NewMeeting().ActionItems.Single().Id;
        using var firstScope = App.Services.CreateScope();
        using var secondScope = App.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var firstTask = await first.ActionItems.SingleAsync(a => a.Id == taskId);
        var secondTask = await second.ActionItems.SingleAsync(a => a.Id == taskId);

        firstTask.Text = "İlk yazarın değeri";
        await first.SaveChangesAsync();
        secondTask.Text = "Eski kayıttan gelen değer";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        Assert.Equal("İlk yazarın değeri", App.WithDb(db => db.ActionItems.AsNoTracking().Single(a => a.Id == taskId).Text));
    }

    [PostgresFact]
    public async Task ExecuteUpdate_advances_the_meetings_xmin()
    {
        var id = NewMeeting().Id;
        var before = App.WithDb(db => db.Meetings.AsNoTracking().Single(m => m.Id == id).RowVersion);
        using (var scope = App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.Meetings.Where(m => m.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MeetingStatus.Failed)));
        }

        var after = App.WithDb(db => db.Meetings.AsNoTracking().Single(m => m.Id == id).RowVersion);

        Assert.NotEqual(before, after);
    }

    [PostgresFact]
    public async Task Completed_speaker_rename_refreshes_the_claimed_version()
    {
        var id = NewMeeting().Id;
        using var scope = App.Services.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISpeakerNamingService>()
            .RenameAsync(S.CarolId, id, new[] { "Konuşmacı 1" }, new[] { "Mehmet" });

        Assert.Equal(SpeakerNamingResult.Renamed, result);
        var reloaded = App.WithDb(db => db.Meetings.AsNoTracking().Include(m => m.Transcript).Single(m => m.Id == id));
        Assert.Equal(MeetingStatus.Completed, reloaded.Status);
        Assert.StartsWith("Mehmet: ", reloaded.Transcript!.Text);
    }

    [PostgresFact]
    public async Task Assign_action_reports_a_concurrent_writer_and_preserves_their_assignment()
    {
        var meeting = NewMeeting();
        var taskId = meeting.ActionItems.Single().Id;
        var otherDueDate = new DateOnly(2026, 12, 15);
        using var client = App.ClientFor(S.AliceId);
        var url = $"/Details?id={meeting.Id}";
        var token = await GetAntiforgeryTokenAsync(client, url);
        var hookFired = false;
        fixture.Interceptor.BeforeUpdate = () =>
        {
            using var connection = new NpgsqlConnection(fixture.ConnectionString);
            connection.Open();
            using var command = new NpgsqlCommand("""
                UPDATE "ActionItems" SET "AssigneeUserId" = @user, "DueDate" = @due WHERE "Id" = @id
                """, connection);
            command.Parameters.AddWithValue("user", S.AliceId);
            command.Parameters.AddWithValue("due", otherDueDate);
            command.Parameters.AddWithValue("id", taskId);
            Assert.Equal(1, command.ExecuteNonQuery());
            hookFired = true;
        };

        try
        {
            var json = await PostFormForJsonAsync(client, "/Details?handler=AssignAction", token,
                ("taskId", taskId.ToString()), ("assigneeUserId", S.CarolId), ("dueDate", "2026-12-20"));

            Assert.True(hookFired);
            Assert.False(json.GetProperty("success").GetBoolean());
            Assert.True(json.GetProperty("reload").GetBoolean());
            var page = WebUtility.HtmlDecode(await client.GetStringAsync(url));
            Assert.Contains("başka biri tarafından değiştirildi", page);
            var task = App.WithDb(db => db.ActionItems.AsNoTracking().Single(a => a.Id == taskId));
            Assert.Equal(S.AliceId, task.AssigneeUserId);
            Assert.Equal(otherDueDate, task.DueDate);
        }
        finally
        {
            fixture.Interceptor.BeforeUpdate = null;
        }
    }
}
