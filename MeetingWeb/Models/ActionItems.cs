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
        public string Text { get; set; } = string.Empty;

        // State tracking flag to indicate task completion status.
        public bool IsDone { get; set; } = false;

        // --- ASSIGNMENT ---
        // Who should do it (a member of the meeting's team) and by when. Ids are kept in clear so the
        // "Bana atananlar" list can be queried; names are resolved from the user's profile at display time.
        [MaxLength(450)]
        public string? AssigneeUserId { get; set; }

        public DateOnly? DueDate { get; set; }

        // Who marked it done and when, shown in the archived summary.
        [MaxLength(450)]
        public string? CompletedByUserId { get; set; }

        public DateTime? CompletedAtUtc { get; set; }

        // Marks the task done (or open again), keeping "who completed it" in step with IsDone.
        public void SetDone(bool done, string? userId)
        {
            IsDone = done;
            CompletedByUserId = done ? userId : null;
            CompletedAtUtc = done ? DateTime.UtcNow : null;
        }

        // --- NAVIGATION PROPERTIES ---

        // Foreign key linking the action item to its parent meeting.
        [Required]
        public int MeetingSummaryId { get; set; }

        [ForeignKey("MeetingSummaryId")]
        public MeetingSummary? MeetingSummary { get; set; }
    }
}