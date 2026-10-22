using System.Data;
using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Services;

public interface IAccountDeletionService
{
    Task<IReadOnlyList<string>> BlockingTeamsAsync(string userId);
    Task RunAsync();
}

public class AccountDeletionService : IAccountDeletionService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAudioStorage _audio;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AccountDeletionService> _logger;
    private readonly IZoomService _zoom;

    public AccountDeletionService(ApplicationDbContext db, UserManager<ApplicationUser> users,
        IAudioStorage audio, IWebHostEnvironment env, ILogger<AccountDeletionService> logger, IZoomService zoom)
    {
        _zoom = zoom;
        _db = db;
        _users = users;
        _audio = audio;
        _env = env;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> BlockingTeamsAsync(string userId) =>
        await _db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.Role == WorkspaceRole.Owner &&
                _db.WorkspaceMembers.Any(other => other.WorkspaceId == m.WorkspaceId && other.UserId != userId))
            .Select(m => m.Workspace.Name).ToListAsync();

    public async Task RunAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var ids = await _db.Users.AsNoTracking()
            .Where(u => u.DeletionRequestedAtUtc != null && u.DeletionRequestedAtUtc <= cutoff)
            .OrderBy(u => u.DeletionRequestedAtUtc).ThenBy(u => u.Id)
            .Select(u => u.Id).ToListAsync();
        foreach (var id in ids)
        {
            try { await DeleteOneAsync(id, cutoff); }
            catch (Exception)
            {
                _db.ChangeTracker.Clear();
                _logger.LogWarning("Account deletion failed for user {UserId}", id);
            }
        }
    }

    private async Task DeleteOneAsync(string userId, DateTime cutoff)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = await _users.FindByIdAsync(userId);
        if (user?.DeletionRequestedAtUtc is not { } requested || requested > cutoff) return;
        if ((await BlockingTeamsAsync(userId)).Count != 0)
        {
            _logger.LogWarning("Account deletion skipped for user {UserId}: shared team ownership", userId);
            return;
        }

        var ownedSpaces = await _db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.UserId == userId && !_db.WorkspaceMembers.Any(other => other.WorkspaceId == m.WorkspaceId && other.UserId != userId))
            .Select(m => m.WorkspaceId).ToListAsync();
        var audioFiles = await _db.Meetings.AsNoTracking()
            .Where(m => ownedSpaces.Contains(m.WorkspaceId) && m.AudioFileName != null)
            .Select(m => m.AudioFileName!).ToListAsync();
        var avatar = user.AvatarFileName;
        var zoom = await _db.ZoomConnections.AsNoTracking().SingleOrDefaultAsync(z => z.UserId == userId);

        await _db.MeetingPoints.Where(p => p.CreatedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedByUserId, (string?)null));
        await _db.ActionItems.Where(a => a.CreatedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CreatedByUserId, (string?)null));
        await _db.ActionItems.Where(a => a.AssigneeUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AssigneeUserId, (string?)null));
        await _db.ActionItems.Where(a => a.CompletedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CompletedByUserId, (string?)null));
        await _db.Meetings.Where(m => m.CreatedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.CreatedByUserId, (string?)null));
        await _db.MeetingSeries.Where(s => s.CreatedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedByUserId, (string?)null));
        await _db.Workspaces.Where(w => w.CreatedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.CreatedByUserId, (string?)null));
        await _db.WorkspaceInvitations.Where(i => i.CreatedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.CreatedByUserId, (string?)null));
        await _db.WorkspaceInvitations.Where(i => i.UsedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedByUserId, (string?)null));

        // A user's comment takes its whole reply thread with it, including other members' replies.
        var affectedMeetings = await _db.Comments.AsNoTracking().Where(c => c.UserId == userId)
            .Select(c => c.MeetingId)
            .Concat(_db.Meetings.AsNoTracking().Where(m => ownedSpaces.Contains(m.WorkspaceId)).Select(m => m.Id))
            .Distinct().ToListAsync();
        var comments = await _db.Comments.AsNoTracking().Where(c => affectedMeetings.Contains(c.MeetingId))
            .Select(c => new { c.Id, c.ParentCommentId, c.UserId, c.Meeting!.WorkspaceId }).ToListAsync();
        var toDelete = comments.Where(c => c.UserId == userId || ownedSpaces.Contains(c.WorkspaceId)).Select(c => c.Id).ToHashSet();
        while (true)
        {
            var children = comments.Where(c => c.ParentCommentId != null && toDelete.Contains(c.ParentCommentId.Value))
                .Select(c => c.Id).ToList();
            if (children.All(toDelete.Contains)) break;
            toDelete.UnionWith(children);
        }
        await _db.CommentReactions.Where(r => r.UserId == userId || toDelete.Contains(r.CommentId)).ExecuteDeleteAsync();
        // SQL Server's self-reference is ClientCascade; remove leaves before their parents.
        while (toDelete.Count > 0)
        {
            var leaves = toDelete.Where(id => !comments.Any(c => c.ParentCommentId == id && toDelete.Contains(c.Id))).ToList();
            await _db.Comments.Where(c => leaves.Contains(c.Id)).ExecuteDeleteAsync();
            toDelete.ExceptWith(leaves);
        }
        await _db.Workspaces.Where(w => ownedSpaces.Contains(w.Id)).ExecuteDeleteAsync();
        await _db.WorkspaceMembers.Where(m => m.UserId == userId).ExecuteDeleteAsync();
        await _db.ZoomConnections.Where(z => z.UserId == userId).ExecuteDeleteAsync();
        var result = await _users.DeleteAsync(user);
        if (!result.Succeeded) throw new InvalidOperationException("Account deletion failed.");
        if (!string.IsNullOrEmpty(avatar))
        {
            var path = Path.Combine(_env.WebRootPath, "uploads", "avatars", Path.GetFileName(avatar));
            File.Delete(path);
        }
        await tx.CommitAsync();

        foreach (var file in audioFiles) _audio.Delete(file);
        // ADR-0011: the user's Zoom grant is cancelled at Zoom too (best effort; the row is already gone).
        if (zoom != null) await _zoom.RevokeQuietlyAsync(zoom);
    }
}
