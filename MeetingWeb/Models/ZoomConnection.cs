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

        public string AccessToken { get; set; } = string.Empty;

        // Zoom hands out a new one on every refresh; only the latest works.
        public string RefreshToken { get; set; } = string.Empty;

        public DateTime AccessTokenExpiresAtUtc { get; set; }

        public DateTime ConnectedAtUtc { get; set; } = DateTime.UtcNow;

        [Timestamp]
        public byte[]? RowVersion { get; set; }
    }
}
