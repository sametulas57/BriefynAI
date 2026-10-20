namespace MeetingWeb.Models
{
    // Role of a member within a workspace. Stored as a number, so existing values must not change.
    public enum WorkspaceRole
    {
        Owner = 1,
        Member = 2
    }
}
