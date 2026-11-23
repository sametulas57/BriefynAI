using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MeetingWeb.Tests.Postgres;

public class PostgresConstraintTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private BriefynFactory App => fixture.Factory;

    [PostgresFact]
    public void Phone_number_is_unique_except_for_empty_and_null_values()
    {
        const string phone = "+905551234567";
        App.WithDb(db =>
        {
            db.Users.AddRange(
                new ApplicationUser { PhoneNumber = phone },
                new ApplicationUser { PhoneNumber = "" },
                new ApplicationUser { PhoneNumber = "" },
                new ApplicationUser { PhoneNumber = null },
                new ApplicationUser { PhoneNumber = null });
            return db.SaveChanges();
        });

        var error = Assert.Throws<DbUpdateException>(() => App.WithDb(db =>
        {
            db.Users.Add(new ApplicationUser { PhoneNumber = phone });
            return db.SaveChanges();
        }));

        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal("23505", postgres.SqlState);
        Assert.Equal("IX_AspNetUsers_PhoneNumber", postgres.ConstraintName);
        App.WithDb(db =>
        {
            Assert.Equal(1, db.Users.Count(u => u.PhoneNumber == phone));
            Assert.Equal(2, db.Users.Count(u => u.PhoneNumber == ""));
            Assert.True(db.Users.Count(u => u.PhoneNumber == null) >= 2);
            return 0;
        });
    }

    [PostgresFact]
    public void Only_one_personal_workspace_is_allowed_per_user()
    {
        var userId = App.WithDb(db =>
        {
            var user = new ApplicationUser();
            db.Users.Add(user);
            db.Workspaces.AddRange(
                new Workspace { Name = "Kişisel", IsPersonal = true, CreatedBy = user },
                new Workspace { Name = "Birinci ekip", CreatedBy = user },
                new Workspace { Name = "İkinci ekip", CreatedBy = user });
            db.SaveChanges();
            return user.Id;
        });

        var error = Assert.Throws<DbUpdateException>(() => App.WithDb(db =>
        {
            db.Workspaces.Add(new Workspace { Name = "İkinci kişisel", IsPersonal = true, CreatedByUserId = userId });
            return db.SaveChanges();
        }));

        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal("23505", postgres.SqlState);
        Assert.Equal("IX_Workspaces_PersonalPerUser", postgres.ConstraintName);
        Assert.Equal(2, App.WithDb(db => db.Workspaces.Count(w => w.CreatedByUserId == userId && !w.IsPersonal)));
    }

    [PostgresTheory]
    [InlineData("Yönetici", "yönetici")]
    [InlineData("İK uzmanı", "ik uzmanı")]
    [InlineData("IŞIK", "ışık")]
    public void Titles_of_a_team_differ_by_more_than_letter_case(string name, string sameName)
    {
        var teamId = NewTeamWithTitle(name);

        // The page's "already exists" message comes from this query.
        Assert.True(App.WithDb(db => db.WorkspaceTitles.Any(t => t.WorkspaceId == teamId && t.Name == sameName)));
        var error = Assert.Throws<DbUpdateException>(() => App.WithDb(db =>
        {
            db.WorkspaceTitles.Add(new WorkspaceTitle { WorkspaceId = teamId, Name = sameName });
            return db.SaveChanges();
        }));

        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal("23505", postgres.SqlState);
        Assert.Equal("IX_WorkspaceTitles_WorkspaceId_Name", postgres.ConstraintName);
    }

    [PostgresFact]
    public void Dotted_and_dotless_i_are_different_letters_in_titles()
    {
        var teamId = NewTeamWithTitle("İK");

        App.WithDb(db =>
        {
            db.WorkspaceTitles.Add(new WorkspaceTitle { WorkspaceId = teamId, Name = "IK" });
            return db.SaveChanges();
        });

        Assert.Equal(2, App.WithDb(db => db.WorkspaceTitles.Count(t => t.WorkspaceId == teamId)));
    }

    // Titles and series have no database cascade to members and meetings; the team's own cascade must remove both.
    [PostgresFact]
    public void Deleting_a_team_removes_titled_members_and_meetings_in_a_series()
    {
        var teamId = App.WithDb(db =>
        {
            var team = new Workspace { Name = "Silinecek ekip" };
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Workspace = team,
                UserId = App.Seed.BobId,
                Role = WorkspaceRole.Owner,
                Title = new WorkspaceTitle { Workspace = team, Name = "Yönetici" }
            });
            var comment = new Comment { UserId = App.Seed.BobId, Content = "yorum" };
            db.Meetings.Add(new Meeting
            {
                Workspace = team,
                Title = "silinecek",
                Series = new MeetingSeries { Workspace = team, Name = "Haftalık" },
                Comments = new List<Comment> { comment }
            });
            db.SaveChanges();
            db.Comments.Add(new Comment { MeetingId = comment.MeetingId, ParentCommentId = comment.Id, UserId = App.Seed.BobId, Content = "yanıt" });
            db.SaveChanges();
            return team.Id;
        });

        App.WithDb(db => db.Database.ExecuteSql($"""DELETE FROM "Workspaces" WHERE "Id" = {teamId}"""));

        App.WithDb(db =>
        {
            Assert.False(db.WorkspaceMembers.Any(m => m.WorkspaceId == teamId));
            Assert.False(db.WorkspaceTitles.Any(t => t.WorkspaceId == teamId));
            Assert.False(db.Meetings.Any(m => m.WorkspaceId == teamId));
            Assert.False(db.MeetingSeries.Any(s => s.WorkspaceId == teamId));
            Assert.False(db.Comments.Any(c => c.Content != "" && c.Meeting!.WorkspaceId == teamId));
            return 0;
        });
    }

    private int NewTeamWithTitle(string title) => App.WithDb(db =>
    {
        var team = new Workspace { Name = "Unvan ekibi" };
        db.WorkspaceTitles.Add(new WorkspaceTitle { Workspace = team, Name = title });
        db.SaveChanges();
        return team.Id;
    });

    [PostgresFact]
    public void Undefined_workspace_role_is_rejected()
    {
        var error = Assert.Throws<DbUpdateException>(() => App.WithDb(db =>
        {
            db.Workspaces.Add(new Workspace
            {
                Name = "Geçersiz rol",
                Members = new List<WorkspaceMember>
                {
                    new() { UserId = App.Seed.CarolId, Role = (WorkspaceRole)7 }
                }
            });
            return db.SaveChanges();
        }));

        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal("23514", postgres.SqlState);
        Assert.Equal("CK_WorkspaceMembers_Role", postgres.ConstraintName);
    }

    [PostgresFact]
    public void Utc_timestamp_retains_microseconds_and_kind()
    {
        var timestamp = new DateTime(2026, 11, 23, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234560);
        var id = App.WithDb(db =>
        {
            Assert.True(db.Database.IsNpgsql());
            var meeting = new Meeting { WorkspaceId = App.Seed.WorkspaceA, Title = "Zaman testi", CreatedAtUtc = timestamp };
            db.Meetings.Add(meeting);
            db.SaveChanges();
            return meeting.Id;
        });

        var actual = App.WithDb(db => db.Meetings.AsNoTracking().Single(m => m.Id == id).CreatedAtUtc);

        Assert.Equal(timestamp, actual);
        Assert.Equal(DateTimeKind.Utc, actual.Kind);
    }

    [PostgresFact]
    public async Task Log_cleanup_deletes_only_rows_older_than_thirty_days()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var marker = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        var old = now.AddDays(-31);
        var recent = now.AddDays(-29);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Logs" ("Message", "TimeStamp") VALUES ({marker}, {old}), ({marker}, {recent})
            """);
        var cleanup = new LogCleanupService(db, new ConfigurationBuilder().Build());

        var deleted = await cleanup.RunAsync();

        Assert.Equal(1, deleted);
        var remaining = await db.Database.SqlQuery<DateTime>($"""
            SELECT "TimeStamp" AS "Value" FROM "Logs" WHERE "Message" = {marker}
            """).ToListAsync();
        Assert.Equal(recent, Assert.Single(remaining));
    }
}
