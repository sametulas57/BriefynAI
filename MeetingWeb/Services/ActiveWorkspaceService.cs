using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Services
{
    // S7: the session only remembers which workspace the user last chose. That memory goes stale when the user is
    // removed from a team, or when a different user signs in on the same browser (the session cookie outlives logins).
    // Every read therefore re-checks membership; a stale value is replaced by the user's own default workspace.
    public interface IActiveWorkspaceService
    {
        Task<WorkspaceMember?> GetActiveMembershipAsync(string? userId);
    }

    public class ActiveWorkspaceService : IActiveWorkspaceService
    {
        public const string SessionKey = "ActiveWorkspaceId";

        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ActiveWorkspaceService(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<WorkspaceMember?> GetActiveMembershipAsync(string? userId)
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            if (string.IsNullOrEmpty(userId))
            {
                session?.Remove(SessionKey);
                return null;
            }

            var remembered = session?.GetInt32(SessionKey);
            var memberships = _context.WorkspaceMembers.Include(wu => wu.Workspace).Where(wu => wu.UserId == userId);

            var membership = remembered == null
                ? null
                : await memberships.FirstOrDefaultAsync(wu => wu.WorkspaceId == remembered);

            // Fallback: prefer a workspace the user owns, then the oldest one.
            membership ??= await memberships
                .OrderByDescending(wu => wu.Role == WorkspaceRole.Owner)
                .ThenBy(wu => wu.WorkspaceId)
                .FirstOrDefaultAsync();

            if (membership == null) session?.Remove(SessionKey);
            else if (membership.WorkspaceId != remembered) session?.SetInt32(SessionKey, membership.WorkspaceId);

            return membership;
        }
    }
}
