using System.Net;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features;

public class AccountDeletionTests
{
    private const string Password = "Test-Parola-123";

    private static async Task<BriefynFactory> ReadyAsync()
    {
        var app = new BriefynFactory();
        await app.InitializeAsync();
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.AddPasswordAsync((await users.FindByIdAsync(app.Seed.AliceId))!, Password)).Succeeded);
        return app;
    }

    private static async Task<(HttpClient Client, HttpResponseMessage Response)> RequestAsync(BriefynFactory app, string password)
    {
        var client = app.ClientFor(app.Seed.AliceId);
        var token = await GetAntiforgeryTokenAsync(client, "/Profile");
        return (client, await PostFormAsync(client, "/Profile?handler=RequestDeletion", token, ("DeletionPassword", password)));
    }

    [Fact]
    public async Task Wrong_password_does_not_request_deletion()
    {
        using var app = await ReadyAsync();
        var (_, res) = await RequestAsync(app, "wrong");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Null(app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.AliceId).DeletionRequestedAtUtc));
    }

    [Fact]
    public async Task Owner_of_shared_team_is_refused_with_team_name()
    {
        using var app = await ReadyAsync();
        var (client, res) = await RequestAsync(app, Password);
        var page = WebUtility.HtmlDecode(await client.GetStringAsync(res.Headers.Location!.OriginalString));
        Assert.Contains("A Şirketi", page);
        Assert.Null(app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.AliceId).DeletionRequestedAtUtc));
    }

    [Fact]
    public async Task Correct_password_saves_request_and_signs_out()
    {
        using var app = await ReadyAsync();
        app.WithDb(db => { db.WorkspaceMembers.Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.AliceId).Role = WorkspaceRole.Member; db.SaveChanges(); return 0; });
        var (client, res) = await RequestAsync(app, Password);
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.NotNull(app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.AliceId).DeletionRequestedAtUtc));
        Assert.Contains(res.Headers.GetValues("Set-Cookie"), value => value.Contains("Identity.Application"));
        client.DefaultRequestHeaders.Remove(TestAuthHandler.UserHeader);
        var page = WebUtility.HtmlDecode(await client.GetStringAsync(res.Headers.Location!.OriginalString));
        Assert.Contains("7 gün", page);
    }

    [Fact]
    public async Task Successful_login_cancels_pending_request()
    {
        using var app = await ReadyAsync();
        app.WithDb(db => { db.Users.Single(u => u.Id == app.Seed.AliceId).DeletionRequestedAtUtc = DateTime.UtcNow; db.SaveChanges(); return 0; });
        var client = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var token = await GetAntiforgeryTokenAsync(client, "/Auth/Login");
        var res = await PostFormAsync(client, "/Auth/Login", token, ("Input.Email", SeedData.AliceEmail), ("Input.Password", Password));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Null(app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.AliceId).DeletionRequestedAtUtc));
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, app.Seed.AliceId);
        var page = WebUtility.HtmlDecode(await client.GetStringAsync(res.Headers.Location!.OriginalString));
        Assert.Contains("Hesap silme isteğiniz iptal edildi", page);
    }

    [Fact]
    public async Task Owner_can_transfer_to_active_member_but_not_pending_or_outsider()
    {
        using var app = await ReadyAsync();
        var alice = app.ClientFor(app.Seed.AliceId);
        var token = await GetAntiforgeryTokenAsync(alice, "/Workspace/Members");
        foreach (var target in new[] { app.Seed.BobId, app.Seed.CarolId })
        {
            if (target == app.Seed.CarolId)
                app.WithDb(db => { db.Users.Single(u => u.Id == target).DeletionRequestedAtUtc = DateTime.UtcNow; db.SaveChanges(); return 0; });
            await PostFormAsync(alice, "/Workspace/Members?handler=TransferOwnership", token, ("targetUserId", target));
            Assert.Equal(WorkspaceRole.Owner, app.WithDb(db => db.WorkspaceMembers.AsNoTracking().Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.AliceId).Role));
        }
        app.WithDb(db => { db.Users.Single(u => u.Id == app.Seed.CarolId).DeletionRequestedAtUtc = null; db.SaveChanges(); return 0; });
        await PostFormAsync(alice, "/Workspace/Members?handler=TransferOwnership", token, ("targetUserId", app.Seed.CarolId));
        Assert.Equal(WorkspaceRole.Member, app.WithDb(db => db.WorkspaceMembers.AsNoTracking().Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.AliceId).Role));
        Assert.Equal(WorkspaceRole.Owner, app.WithDb(db => db.WorkspaceMembers.AsNoTracking().Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.CarolId).Role));
        await PostFormAsync(alice, "/Workspace/Members?handler=TransferOwnership", token, ("targetUserId", app.Seed.AliceId));
        Assert.Equal(WorkspaceRole.Owner, app.WithDb(db => db.WorkspaceMembers.AsNoTracking().Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.CarolId).Role));
    }

    [Fact]
    public async Task Sweep_respects_seven_day_wait()
    {
        using var app = await ReadyAsync();
        app.WithDb(db => { db.Users.Single(u => u.Id == app.Seed.BobId).DeletionRequestedAtUtc = DateTime.UtcNow.AddDays(-6); db.SaveChanges(); return 0; });
        using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IAccountDeletionService>().RunAsync();
        Assert.True(app.WithDb(db => db.Users.Any(u => u.Id == app.Seed.BobId)));
        app.WithDb(db => { db.Users.Single(u => u.Id == app.Seed.BobId).DeletionRequestedAtUtc = DateTime.UtcNow.AddDays(-8); db.SaveChanges(); return 0; });
        using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IAccountDeletionService>().RunAsync();
        Assert.False(app.WithDb(db => db.Users.Any(u => u.Id == app.Seed.BobId)));
    }

    [Fact]
    public async Task Sweep_cleans_owned_data_and_keeps_team_content_without_personal_links()
    {
        using var app = await ReadyAsync();
        var audio = "owned-test.m4a";
        File.WriteAllText(Path.Combine(app.AudioRoot, audio), "audio");
        var avatar = "account-test.jpg";
        File.WriteAllText(Path.Combine(app.WebRoot, "uploads", "avatars", avatar), "photo");
        var ids = app.WithDb(db =>
        {
            var own = new Workspace { Name = "Kişisel", IsPersonal = true, CreatedByUserId = app.Seed.AliceId };
            db.Workspaces.Add(own); db.SaveChanges();
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = own.Id, UserId = app.Seed.AliceId, Role = WorkspaceRole.Owner });
            var meeting = new Meeting { WorkspaceId = own.Id, Title = "Özel", AudioFileName = audio };
            db.Meetings.Add(meeting);
            var point = db.MeetingPoints.First(p => p.MeetingId == app.Seed.MeetingA);
            point.Source = EntrySource.Manual; point.CreatedByUserId = app.Seed.AliceId;
            var task = db.ActionItems.First(a => a.MeetingId == app.Seed.MeetingA);
            task.Source = EntrySource.Manual; task.CreatedByUserId = app.Seed.AliceId; task.AssigneeUserId = app.Seed.AliceId; task.CompletedByUserId = app.Seed.AliceId; task.CompletedAtUtc = DateTime.UtcNow;
            db.WorkspaceMembers.Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.AliceId).Role = WorkspaceRole.Member;
            db.Meetings.Single(m => m.Id == app.Seed.MeetingA).CreatedByUserId = app.Seed.AliceId;
            db.Workspaces.Single(w => w.Id == app.Seed.WorkspaceA).CreatedByUserId = app.Seed.AliceId;
            var invitation = new WorkspaceInvitation { WorkspaceId = app.Seed.WorkspaceA, TokenHash = new string('A', 64), CreatedByUserId = app.Seed.AliceId, UsedByUserId = app.Seed.AliceId };
            db.WorkspaceInvitations.Add(invitation);
            var reply = new Comment { MeetingId = app.Seed.MeetingA, ParentCommentId = app.Seed.CommentA, UserId = app.Seed.CarolId, Content = "Yanıt" };
            var otherComment = new Comment { MeetingId = app.Seed.MeetingA, UserId = app.Seed.CarolId, Content = "Kalan yorum" };
            db.Comments.AddRange(reply, otherComment);
            db.CommentReactions.Add(new CommentReaction { CommentId = app.Seed.CommentA, UserId = app.Seed.AliceId, IsLike = true });
            var user = db.Users.Single(u => u.Id == app.Seed.AliceId);
            user.AvatarFileName = avatar;
            user.DeletionRequestedAtUtc = DateTime.UtcNow.AddDays(-8);
            db.SaveChanges(); return (own.Id, meeting.Id, point.Id, task.Id, invitation.Id, reply.Id, otherComment.Id);
        });
        using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IAccountDeletionService>().RunAsync();
        app.WithDb(db =>
        {
            Assert.False(db.Users.Any(u => u.Id == app.Seed.AliceId));
            Assert.False(db.Workspaces.Any(w => w.Id == ids.Item1));
            Assert.False(db.Meetings.Any(m => m.Id == ids.Item2));
            Assert.False(db.Comments.Any(c => c.Id == app.Seed.CommentA || c.Id == ids.Item6));
            Assert.True(db.Comments.Any(c => c.Id == ids.Item7));
            Assert.False(db.CommentReactions.Any(r => r.UserId == app.Seed.AliceId));
            Assert.Null(db.Meetings.Single(m => m.Id == app.Seed.MeetingA).CreatedByUserId);
            Assert.Null(db.Workspaces.Single(w => w.Id == app.Seed.WorkspaceA).CreatedByUserId);
            var invitation = db.WorkspaceInvitations.Single(i => i.Id == ids.Item5);
            Assert.Null(invitation.CreatedByUserId); Assert.Null(invitation.UsedByUserId);
            Assert.Null(db.MeetingPoints.Single(p => p.Id == ids.Item3).CreatedByUserId);
            var task = db.ActionItems.Single(a => a.Id == ids.Item4);
            Assert.Null(task.CreatedByUserId); Assert.Null(task.AssigneeUserId); Assert.Null(task.CompletedByUserId); Assert.NotNull(task.CompletedAtUtc);
            Assert.True(db.Users.Any(u => u.Id == app.Seed.CarolId));
            Assert.True(db.WorkspaceMembers.Any(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.CarolId));
            Assert.True(db.Meetings.Any(m => m.Id == app.Seed.MeetingA));
            return 0;
        });
        Assert.False(File.Exists(Path.Combine(app.AudioRoot, audio)));
        Assert.False(File.Exists(Path.Combine(app.WebRoot, "uploads", "avatars", avatar)));
    }

    [Fact]
    public async Task Sweep_skips_account_that_became_owner_of_shared_team()
    {
        using var app = await ReadyAsync();
        app.WithDb(db => { db.Users.Single(u => u.Id == app.Seed.AliceId).DeletionRequestedAtUtc = DateTime.UtcNow.AddDays(-8); db.SaveChanges(); return 0; });
        using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IAccountDeletionService>().RunAsync();
        Assert.True(app.WithDb(db => db.Users.Any(u => u.Id == app.Seed.AliceId)));
    }

    [Fact]
    public async Task Deleted_author_of_manual_point_is_shown()
    {
        using var app = await ReadyAsync();
        app.WithDb(db =>
        {
            var point = db.MeetingPoints.First(x => x.MeetingId == app.Seed.MeetingA);
            point.Source = EntrySource.Manual;
            point.CreatedByUserId = null;
            db.Meetings.Single(x => x.Id == app.Seed.MeetingA).Status = MeetingStatus.Completed;
            db.SaveChanges();
            return 0;
        });
        var page = WebUtility.HtmlDecode(await app.ClientFor(app.Seed.CarolId).GetStringAsync($"/Details?id={app.Seed.MeetingA}"));
        Assert.Contains("Ekleyen: Silinmiş üye", page);
    }

    [Fact]
    public async Task Outsider_cannot_transfer_ownership_in_another_team()
    {
        using var app = await ReadyAsync();
        var bob = app.ClientFor(app.Seed.BobId);
        var token = await GetAntiforgeryTokenAsync(bob, "/Workspace/Members");
        var res = await PostFormAsync(bob, "/Workspace/Members?handler=TransferOwnership", token, ("targetUserId", app.Seed.CarolId));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(WorkspaceRole.Owner, app.WithDb(db => db.WorkspaceMembers.AsNoTracking().Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.AliceId).Role));
    }
}
