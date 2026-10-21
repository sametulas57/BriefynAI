using System.Net;
using System.Data.Common;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features;

public class AccountDeletionTests
{
    private const string Password = "Test-Parola-123";

    private static async Task<BriefynFactory> ReadyAsync(BriefynFactory? app = null)
    {
        app ??= new BriefynFactory();
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
        var beforeStamp = app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.AliceId).SecurityStamp);
        var (client, res) = await RequestAsync(app, Password);
        var page = WebUtility.HtmlDecode(await client.GetStringAsync(res.Headers.Location!.OriginalString));
        Assert.Contains("A Şirketi", page);
        Assert.Null(app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.AliceId).DeletionRequestedAtUtc));
        Assert.Equal(beforeStamp, app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.AliceId).SecurityStamp));
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
    public async Task Deletion_request_invalidates_other_sessions()
    {
        using var app = await ReadyAsync();
        app.WithDb(db => { db.WorkspaceMembers.Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.AliceId).Role = WorkspaceRole.Member; db.SaveChanges(); return 0; });
        var before = app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.AliceId).SecurityStamp);

        await RequestAsync(app, Password);

        Assert.NotEqual(before, app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.AliceId).SecurityStamp));
    }

    [Fact]
    public async Task Deletion_request_rejects_another_device_cookie_on_its_next_request()
    {
        using var app = await ReadyAsync(new BriefynFactory { UseCookieAuthentication = true });
        app.WithDb(db => { db.WorkspaceMembers.Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.AliceId).Role = WorkspaceRole.Member; db.SaveChanges(); return 0; });
        var options = new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false };
        using var first = app.CreateClient(options);
        using var second = app.CreateClient(options);
        foreach (var client in new[] { first, second })
        {
            var loginToken = await GetAntiforgeryTokenAsync(client, "/Auth/Login");
            var login = await PostFormAsync(client, "/Auth/Login", loginToken,
                ("Input.Email", SeedData.AliceEmail), ("Input.Password", Password));
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Profile")).StatusCode);
        }

        var deletionToken = await GetAntiforgeryTokenAsync(first, "/Profile");
        var deletion = await PostFormAsync(first, "/Profile?handler=RequestDeletion", deletionToken, ("DeletionPassword", Password));
        Assert.Equal(HttpStatusCode.Redirect, deletion.StatusCode);
        Assert.NotNull(app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.AliceId).DeletionRequestedAtUtc));

        var otherDevice = await second.GetAsync("/Profile");
        Assert.Equal(HttpStatusCode.Redirect, otherDevice.StatusCode);
        var location = otherDevice.Headers.Location!;
        Assert.StartsWith("/Auth/Login", location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString);
    }

    [Fact]
    public async Task Ownership_cannot_be_transferred_to_user_after_deletion_request_is_saved()
    {
        using var app = await ReadyAsync();
        app.WithDb(db =>
        {
            db.WorkspaceMembers.Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.AliceId).Role = WorkspaceRole.Member;
            db.WorkspaceMembers.Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.CarolId).Role = WorkspaceRole.Owner;
            db.SaveChanges();
            return 0;
        });

        var (_, request) = await RequestAsync(app, Password);
        Assert.Equal(HttpStatusCode.Redirect, request.StatusCode);
        Assert.NotNull(app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == app.Seed.AliceId).DeletionRequestedAtUtc));

        var carol = app.ClientFor(app.Seed.CarolId);
        var token = await GetAntiforgeryTokenAsync(carol, "/Workspace/Members");
        var transfer = await PostFormAsync(carol, "/Workspace/Members?handler=TransferOwnership", token, ("targetUserId", app.Seed.AliceId));

        Assert.Equal(HttpStatusCode.Redirect, transfer.StatusCode);
        Assert.Equal(WorkspaceRole.Owner, app.WithDb(db => db.WorkspaceMembers.AsNoTracking().Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.CarolId).Role));
        Assert.Equal(WorkspaceRole.Member, app.WithDb(db => db.WorkspaceMembers.AsNoTracking().Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.AliceId).Role));
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
    public async Task Second_transfer_by_former_owner_is_rejected_and_only_one_owner_remains()
    {
        using var app = await ReadyAsync();
        var alice = app.ClientFor(app.Seed.AliceId);
        var token = await GetAntiforgeryTokenAsync(alice, "/Workspace/Members");
        var first = await PostFormAsync(alice, "/Workspace/Members?handler=TransferOwnership", token, ("targetUserId", app.Seed.CarolId));
        var second = await PostFormAsync(alice, "/Workspace/Members?handler=TransferOwnership", token, ("targetUserId", app.Seed.BobId));

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(new[] { app.Seed.CarolId }, app.WithDb(db => db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == app.Seed.WorkspaceA && m.Role == WorkspaceRole.Owner)
            .Select(m => m.UserId).ToArray()));
    }

    [Fact]
    public async Task Member_who_becomes_owner_cannot_leave_with_an_old_form()
    {
        using var app = await ReadyAsync();
        var carol = app.ClientFor(app.Seed.CarolId);
        var staleToken = await GetAntiforgeryTokenAsync(carol, "/Workspace/Members");
        var alice = app.ClientFor(app.Seed.AliceId);
        var transferToken = await GetAntiforgeryTokenAsync(alice, "/Workspace/Members");
        await PostFormAsync(alice, "/Workspace/Members?handler=TransferOwnership", transferToken, ("targetUserId", app.Seed.CarolId));

        var leave = await PostFormAsync(carol, "/Workspace/Members?handler=LeaveWorkspace", staleToken);

        Assert.Equal(HttpStatusCode.Redirect, leave.StatusCode);
        Assert.Equal(new[] { app.Seed.CarolId }, app.WithDb(db => db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == app.Seed.WorkspaceA && m.Role == WorkspaceRole.Owner)
            .Select(m => m.UserId).ToArray()));
    }

    [Fact]
    public async Task Former_owner_cannot_remove_new_owner_or_member_with_an_old_form()
    {
        using var app = await ReadyAsync();
        app.WithDb(db => { db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = app.Seed.WorkspaceA, UserId = app.Seed.BobId, Role = WorkspaceRole.Member }); db.SaveChanges(); return 0; });
        var alice = app.ClientFor(app.Seed.AliceId);
        var staleToken = await GetAntiforgeryTokenAsync(alice, "/Workspace/Members");
        await PostFormAsync(alice, "/Workspace/Members?handler=TransferOwnership", staleToken, ("targetUserId", app.Seed.CarolId));

        var removeOwner = await PostFormAsync(alice, "/Workspace/Members?handler=RemoveMember", staleToken, ("targetUserId", app.Seed.CarolId));
        var removeMember = await PostFormAsync(alice, "/Workspace/Members?handler=RemoveMember", staleToken, ("targetUserId", app.Seed.BobId));

        Assert.Equal(HttpStatusCode.Unauthorized, removeOwner.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, removeMember.StatusCode);
        Assert.True(app.WithDb(db => db.WorkspaceMembers.AsNoTracking().Any(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.BobId)));
        Assert.Equal(new[] { app.Seed.CarolId }, app.WithDb(db => db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == app.Seed.WorkspaceA && m.Role == WorkspaceRole.Owner)
            .Select(m => m.UserId).ToArray()));
    }

    [Fact]
    public async Task Owner_can_still_remove_member_and_member_can_still_leave()
    {
        using var app = await ReadyAsync();
        app.WithDb(db => { db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = app.Seed.WorkspaceA, UserId = app.Seed.BobId, Role = WorkspaceRole.Member }); db.SaveChanges(); return 0; });
        var alice = app.ClientFor(app.Seed.AliceId);
        var ownerToken = await GetAntiforgeryTokenAsync(alice, "/Workspace/Members");
        var carol = app.ClientFor(app.Seed.CarolId);
        var memberToken = await GetAntiforgeryTokenAsync(carol, "/Workspace/Members");

        var remove = await PostFormAsync(alice, "/Workspace/Members?handler=RemoveMember", ownerToken, ("targetUserId", app.Seed.BobId));
        var leave = await PostFormAsync(carol, "/Workspace/Members?handler=LeaveWorkspace", memberToken);

        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, leave.StatusCode);
        Assert.Equal(new[] { app.Seed.AliceId }, app.WithDb(db => db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == app.Seed.WorkspaceA)
            .Select(m => m.UserId).ToArray()));
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
    public async Task Sweep_loads_only_comments_from_affected_meetings()
    {
        var commands = new CommentSelectInterceptor();
        using var app = await ReadyAsync(new BriefynFactory { CommandInterceptor = commands });
        app.WithDb(db => { db.Users.Single(u => u.Id == app.Seed.CarolId).DeletionRequestedAtUtc = DateTime.UtcNow.AddDays(-8); db.SaveChanges(); return 0; });

        using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IAccountDeletionService>().RunAsync();

        Assert.NotEmpty(commands.Selects);
        Assert.DoesNotContain(commands.Selects, sql => !sql.Contains("WHERE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sweep_continues_after_one_account_fails()
    {
        using var app = await ReadyAsync(new BriefynFactory { AudioStorageOverride = new FailingAudioStorage() });
        app.WithDb(db =>
        {
            db.Users.Single(u => u.Id == app.Seed.BobId).DeletionRequestedAtUtc = DateTime.UtcNow.AddDays(-9);
            db.Users.Single(u => u.Id == app.Seed.CarolId).DeletionRequestedAtUtc = DateTime.UtcNow.AddDays(-8);
            db.Meetings.Single(m => m.Id == app.Seed.MeetingB).AudioFileName = "fail.m4a";
            db.SaveChanges(); return 0;
        });

        using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IAccountDeletionService>().RunAsync();

        Assert.False(app.WithDb(db => db.Users.Any(u => u.Id == app.Seed.CarolId)));
    }

    [Fact]
    public async Task Avatar_delete_failure_keeps_account_for_retry()
    {
        if (OperatingSystem.IsWindows()) return;
        using var app = await ReadyAsync();
        var avatarFolder = Path.Combine(app.WebRoot, "uploads", "avatars");
        var protectedFolder = Path.Combine(app.WebRoot, "protected-avatar");
        Directory.CreateDirectory(protectedFolder);
        File.WriteAllText(Path.Combine(protectedFolder, "locked.jpg"), "photo");
        Directory.Delete(avatarFolder);
        Directory.CreateSymbolicLink(avatarFolder, protectedFolder);
        File.SetUnixFileMode(protectedFolder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            app.WithDb(db =>
            {
                var bob = db.Users.Single(u => u.Id == app.Seed.BobId);
                bob.AvatarFileName = "locked.jpg";
                bob.DeletionRequestedAtUtc = DateTime.UtcNow.AddDays(-8);
                db.SaveChanges(); return 0;
            });

            using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IAccountDeletionService>().RunAsync();

            Assert.True(app.WithDb(db => db.Users.Any(u => u.Id == app.Seed.BobId)));
            Assert.True(File.Exists(Path.Combine(protectedFolder, "locked.jpg")));
        }
        finally
        {
            File.SetUnixFileMode(protectedFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(avatarFolder);
        }
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
        var token = await GetAntiforgeryTokenAsync(bob, "/Profile");
        bob.DefaultRequestHeaders.Remove(TestAuthHandler.UserHeader);
        bob.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, app.Seed.AliceId);
        await bob.GetAsync($"/Workspace/Switch?workspaceId={app.Seed.WorkspaceA}");
        bob.DefaultRequestHeaders.Remove(TestAuthHandler.UserHeader);
        bob.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, app.Seed.BobId);
        var res = await PostFormAsync(bob, "/Workspace/Members?handler=TransferOwnership", token, ("targetUserId", app.Seed.CarolId));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal(WorkspaceRole.Owner, app.WithDb(db => db.WorkspaceMembers.AsNoTracking().Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.AliceId).Role));
        Assert.Equal(WorkspaceRole.Member, app.WithDb(db => db.WorkspaceMembers.AsNoTracking().Single(m => m.WorkspaceId == app.Seed.WorkspaceA && m.UserId == app.Seed.CarolId).Role));
    }

    private sealed class CommentSelectInterceptor : DbCommandInterceptor
    {
        public List<string> Selects { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Comments\"", StringComparison.OrdinalIgnoreCase)) Selects.Add(command.CommandText);
            return new(result);
        }
    }

    private sealed class FailingAudioStorage : IAudioStorage
    {
        public string RootPath => "";
        public Task<string> SaveAsync(Microsoft.AspNetCore.Http.IFormFile file) => throw new NotSupportedException();
        public Task<string?> SaveAsync(Stream content, string fileName, long maxBytes) => throw new NotSupportedException();
        public Task<bool> SaveSidecarAsync(string storedName, Stream content, long maxBytes) => throw new NotSupportedException();
        public string GetPath(string storedName) => storedName;
        public string? GetSidecarPath(string storedName) => null;
        public bool Delete(string? storedName) => storedName == "fail.m4a" ? throw new IOException("test failure") : false;
    }
}
