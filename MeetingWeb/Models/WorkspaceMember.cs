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

        public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
