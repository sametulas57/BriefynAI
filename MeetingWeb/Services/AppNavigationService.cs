using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Services
{
    public record AppNavigation(WorkspaceMember? Active, IReadOnlyList<WorkspaceMember> Memberships);

    // Everything the signed-in navigation (_AppNav) shows about the user's teams.
    public class AppNavigationService(ApplicationDbContext db, IActiveWorkspaceService activeWorkspace)
    {
        public async Task<AppNavigation> GetAsync(string? userId)
        {
            var active = await activeWorkspace.GetActiveMembershipAsync(userId);
            if (active == null) return new AppNavigation(null, []);
            var memberships = await db.WorkspaceMembers.AsNoTracking().Include(m => m.Workspace)
                .Where(m => m.UserId == userId).OrderBy(m => m.WorkspaceId).ToListAsync();
            return new AppNavigation(active, memberships);
        }
    }
}
