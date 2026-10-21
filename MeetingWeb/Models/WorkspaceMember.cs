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

        // Single place for title permissions (PRD roller-ve-unvanlar); the admin role will join here later.
        public bool CanManageTitles => Role == WorkspaceRole.Owner;

        public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
