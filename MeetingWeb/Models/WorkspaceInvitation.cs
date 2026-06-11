using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeetingWeb.Models
{
    public class WorkspaceInvitation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int WorkspaceId { get; set; }

        [ForeignKey("WorkspaceId")]
        public Workspace Workspace { get; set; } = null!;

        // Cryptographically secure unique token for invitation links.
        [Required]
        [MaxLength(100)]
        public string Token { get; set; } = Guid.NewGuid().ToString();

        // Timestamp for invitation creation in UTC.
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Security constraint: Token expiration window (Time-to-Live set to 7 days).
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);

        // Optional restrictor: Tie the invitation to a specific email address.
        [MaxLength(256)]
        public string? InviteeEmail { get; set; }

        // State tracker to enforce single-use token policy.
        public bool IsUsed { get; set; } = false;
    }
}