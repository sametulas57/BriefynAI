using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeetingWeb.Models
{
    // Aggregate root: one analysed (or in-progress) meeting of a workspace.
    public class Meeting
    {
        [Key]
        public int Id { get; set; }

        public int WorkspaceId { get; set; }
        public Workspace? Workspace { get; set; }

        // Encrypted at rest; no length limit because the column stores ciphertext (S4).
        public string Title { get; set; } = string.Empty;

        public MeetingStatus Status { get; set; } = MeetingStatus.Processing;

        public MeetingSource Source { get; set; } = MeetingSource.Upload;

        // Stored file name in the private audio store; cleared once the recording is deleted (ADR-0002).
        [MaxLength(300)]
        public string? AudioFileName { get; set; }

        [MaxLength(450)]
        public string? CreatedByUserId { get; set; }
        public ApplicationUser? CreatedBy { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAtUtc { get; set; }

        [Timestamp]
        public byte[]? RowVersion { get; set; }

        // Kept in its own table so lists never load (and decrypt) the full transcript.
        public MeetingTranscript? Transcript { get; set; }

        public List<MeetingPoint> Points { get; set; } = new();

        public List<ActionItem> ActionItems { get; set; } = new();

        public ICollection<Comment> Comments { get; set; } = new List<Comment>();

        // Summary lines in display order: AI lines first, then the ones users added.
        public IEnumerable<MeetingPoint> PointsOf(MeetingPointKind kind) =>
            Points.Where(p => p.Kind == kind).OrderBy(p => p.Source).ThenBy(p => p.SortOrder).ThenBy(p => p.Id);

        [NotMapped]
        public IEnumerable<ActionItem> OrderedActionItems => ActionItems.OrderBy(a => a.SortOrder).ThenBy(a => a.Id);
    }
}
