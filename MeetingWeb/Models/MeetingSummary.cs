using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeetingWeb.Models
{
    // Aggregate Root entity representing the core meeting record in the database.
    public class MeetingSummary
    {
        [Key]
        public int Id { get; set; }

        // Standardized UTC timestamp to prevent cross-timezone data corruption.
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        // Tracks the lifecycle state of the AI background job.
        public MeetingStatus Status { get; set; } = MeetingStatus.Processing;

        public string? AudioFilePath { get; set; }

        // --- ENCRYPTED DOMAIN DATA ---

        [MaxLength(255)]
        public string ToplantiKonusu { get; set; } = string.Empty;

        // Note: These lists are serialized and encrypted at rest via ApplicationDbContext.
        public List<string> GorusulenKonular { get; set; } = new();
        public List<string> AlinanKararlar { get; set; } = new();

        // --- NAVIGATION PROPERTIES ---

        // One-to-many relationship mapping AI-generated tasks to database entities.
        public List<ActionItem> AksiyonMaddeleri { get; set; } = new();

        // Foreign key linking the meeting to a specific isolated tenant (Workspace).
        public int? WorkspaceId { get; set; }

        [ForeignKey("WorkspaceId")]
        public Workspace? Workspace { get; set; }

        // One-to-many relationship for the collaborative discussion thread.
        public ICollection<MeetingComment> Comments { get; set; } = new List<MeetingComment>();
    }
}