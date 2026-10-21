namespace MeetingWeb.Models
{
    // Membership of a user in a workspace (many-to-many junction).
    public class WorkspaceMember
    {
        public int WorkspaceId { get; set; }
        public Workspace Workspace { get; set; } = null!;

        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        public WorkspaceRole Role { get; set; } = WorkspaceRole.Member;

        // At most one title per person (so nobody shows up in two groups); cleared when the title is deleted.
        public int? TitleId { get; set; }
        public WorkspaceTitle? Title { get; set; }

        // Single place for role permissions (PRD roller-ve-unvanlar). Founder and admins run the team (invite, remove
        // members, titles); only the founder changes roles, removes admins and deletes the team.
        public static bool RunsTeam(WorkspaceRole role) => role is WorkspaceRole.Owner or WorkspaceRole.Admin;

        public bool CanManageTitles => RunsTeam(Role);
        public bool CanInvite => RunsTeam(Role);
        public bool CanManageRoles => Role == WorkspaceRole.Owner;

        public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
