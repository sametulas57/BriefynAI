using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace MeetingWeb.Models
{
    // Single-use invitation link. Only the SHA-256 hash of the token is stored; the token itself is shown once.
    public class WorkspaceInvitation
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

        [Key]
        public int Id { get; set; }

        public int WorkspaceId { get; set; }
        public Workspace Workspace { get; set; } = null!;

        [Required]
        [MaxLength(64)]
        public string TokenHash { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.Add(Lifetime);

        [MaxLength(450)]
        public string? CreatedByUserId { get; set; }
        public ApplicationUser? CreatedBy { get; set; }

        public DateTime? UsedAtUtc { get; set; }

        [MaxLength(450)]
        public string? UsedByUserId { get; set; }
        public ApplicationUser? UsedBy { get; set; }

        public bool IsUsable(DateTime utcNow) => UsedAtUtc == null && ExpiresAtUtc > utcNow;

        // 32 random bytes, URL-safe.
        public static string NewToken() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static string HashOf(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
