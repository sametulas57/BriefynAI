using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingWeb.Tests.Features;

// PRD 2026-10-22-zoom-asama-2 (ADR-0012): Zoom's notifications import new recordings without pressing "Al".
public class ZoomAutoImportTests
{
    private static async Task<BriefynFactory> AppAsync()
    {
        var app = new BriefynFactory { ZoomEnabled = true };
        await app.InitializeAsync();
        return app;
    }

    private static void Connect(BriefynFactory app, string userId, int? autoImportWorkspaceId = null) => app.WithDb(db =>
    {
        db.ZoomConnections.Add(new ZoomConnection
        {
            UserId = userId,
            ZoomUserId = "zoom-" + userId,
            ZoomEmail = "zoom@" + userId,
            AccessToken = StubZoomHandler.AccessToken,
            RefreshToken = StubZoomHandler.RefreshToken,
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(50),
            AutoImportWorkspaceId = autoImportWorkspaceId
        });
        return db.SaveChanges();
    });

    private static int? AutoImportOf(BriefynFactory app, string userId) =>
        app.WithDb(db => db.ZoomConnections.AsNoTracking().Single(z => z.UserId == userId).AutoImportWorkspaceId);

    private static async Task<bool> SetAsync(BriefynFactory app, string userId, int? workspaceId)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IZoomService>().SetAutoImportAsync(userId, workspaceId);
    }

    // 13
    [Fact]
    public async Task Auto_import_can_be_set_only_to_own_team()
    {
        using var app = await AppAsync();
        Connect(app, app.Seed.AliceId);

        Assert.True(await SetAsync(app, app.Seed.AliceId, app.Seed.WorkspaceA));
        Assert.Equal(app.Seed.WorkspaceA, AutoImportOf(app, app.Seed.AliceId));

        Assert.False(await SetAsync(app, app.Seed.AliceId, app.Seed.WorkspaceB));
        Assert.Equal(app.Seed.WorkspaceA, AutoImportOf(app, app.Seed.AliceId));

        Assert.True(await SetAsync(app, app.Seed.AliceId, null));
        Assert.Null(AutoImportOf(app, app.Seed.AliceId));

        // Without a connection there is nothing to turn on.
        Assert.False(await SetAsync(app, app.Seed.CarolId, app.Seed.WorkspaceA));
    }

    [Fact]
    public async Task Deleting_the_team_turns_auto_import_off()
    {
        using var app = await AppAsync();
        var teamId = app.WithDb(db =>
        {
            var team = new Workspace { Name = "Silinecek" };
            db.Workspaces.Add(team);
            db.SaveChanges();
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = team.Id, UserId = app.Seed.AliceId, Role = WorkspaceRole.Owner });
            db.SaveChanges();
            return team.Id;
        });
        Connect(app, app.Seed.AliceId, teamId);

        app.WithDb(db => db.Workspaces.Where(w => w.Id == teamId).ExecuteDelete());

        Assert.Null(AutoImportOf(app, app.Seed.AliceId));
    }
}
