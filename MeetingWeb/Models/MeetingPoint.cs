using System.ComponentModel.DataAnnotations;

namespace MeetingWeb.Models
{
    // One discussed topic or decision of a meeting, written by the AI or added by a user.
    public class MeetingPoint
    {
        [Key]
        public int Id { get; set; }

        public int MeetingId { get; set; }
        public Meeting? Meeting { get; set; }

        public MeetingPointKind Kind { get; set; }

        // Encrypted at rest.
        public string Text { get; set; } = string.Empty;

        public EntrySource Source { get; set; } = EntrySource.Ai;

        // Author of a manual entry; null for AI lines.
        [MaxLength(450)]
        public string? CreatedByUserId { get; set; }
        public ApplicationUser? CreatedBy { get; set; }

        public int SortOrder { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
