using System.ComponentModel.DataAnnotations;

namespace MeetingWeb.Models
{
    // A user's own Zoom account link (ADR-0011): at most one per user. Both tokens are encrypted (v3) and never logged.
    public class ZoomConnection
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(450)]
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser? User { get; set; }

        [MaxLength(100)]
        public string ZoomUserId { get; set; } = string.Empty;

        [MaxLength(256)]
        public string ZoomEmail { get; set; } = string.Empty;

        // Permissions Zoom granted to this link (space-separated); null = not known yet (older link, filled on the next
        // token refresh). Not secret, so stored as plain text.
        [MaxLength(1000)]
        public string? GrantedScopes { get; set; }

        public string AccessToken { get; set; } = string.Empty;

        // Zoom hands out a new one on every refresh; only the latest works.
        public string RefreshToken { get; set; } = string.Empty;

        public DateTime AccessTokenExpiresAtUtc { get; set; }

        public DateTime ConnectedAtUtc { get; set; } = DateTime.UtcNow;

        // Team that new recordings go to without pressing "Al" (ADR-0012); null = off. Cleared when the team is deleted.
        public int? AutoImportWorkspaceId { get; set; }
        public Workspace? AutoImportWorkspace { get; set; }

        // Set while one server process refreshes the tokens, so another process does not use the same refresh token
        // at the same time (Zoom would reject the second one). Ignored after a minute.
        public DateTime? RefreshStartedAtUtc { get; set; }

        [Timestamp]
        public byte[]? RowVersion { get; set; }
    }
}
