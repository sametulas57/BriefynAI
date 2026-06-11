using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeetingWeb.Models
{
    // Represents an actionable task extracted from a meeting summary.
    public class ActionItem
    {
        [Key]
        public int Id { get; set; }

        // The descriptive text of the action item. (Encrypted at rest via DbContext).
        [Required]
        [MaxLength(1000)]
        public string Text { get; set; } = string.Empty;

        // State tracking flag to indicate task completion status.
        public bool IsDone { get; set; } = false;

        // --- NAVIGATION PROPERTIES ---

        // Foreign key linking the action item to its parent meeting.
        [Required]
        public int MeetingSummaryId { get; set; }

        [ForeignKey("MeetingSummaryId")]
        public MeetingSummary? MeetingSummary { get; set; }
    }
}