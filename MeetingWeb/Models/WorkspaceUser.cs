using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace MeetingWeb.Models
{
    // Junction table to resolve Many-to-Many relationship between Workspaces and Users.
    public class WorkspaceUser
    {
        public int WorkspaceId { get; set; }
        public Workspace Workspace { get; set; } = null!;

        public string UserId { get; set; } = string.Empty;
        public IdentityUser User { get; set; } = null!;

        // Role-Based Access Control (RBAC) identifier within the workspace.
        [Required]
        [MaxLength(50)]
        public string Role { get; set; } = "Üye";
    }
}