using System.ComponentModel.DataAnnotations;

namespace MeetingWeb.Models
{
    // A team's recurring meeting ("Pazartesi ekip toplantısı"); a meeting belongs to at most one series (ADR-0013).
    public class MeetingSeries
    {
        public const int MaxNameLength = 100;

        [Key]
        public int Id { get; set; }

        public int WorkspaceId { get; set; }
        public Workspace? Workspace { get; set; }

        // Encrypted at rest like meeting titles; the length limit is checked in code because the column stores ciphertext.
        public string Name { get; set; } = string.Empty;

        // Keyed hash of the Zoom meeting number for series that Zoom started; never the number itself.
        [MaxLength(64)]
        public string? ZoomMeetingKey { get; set; }

        [MaxLength(450)]
        public string? CreatedByUserId { get; set; }
        public ApplicationUser? CreatedBy { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public List<Meeting> Meetings { get; set; } = new();
    }
}
