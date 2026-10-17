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

        // --- ACTION TRACKING ---

        // Who is responsible. Always a member of the meeting's team; set by a person, never guessed from AI text.
        public string? AssigneeUserId { get; set; }

        [ForeignKey("AssigneeUserId")]
        public Microsoft.AspNetCore.Identity.IdentityUser? Assignee { get; set; }

        // Calendar day in Turkish time (no clock time), shown and compared as a date.
        public DateOnly? DueDate { get; set; }

        // "assigneeId|yyyy-MM-dd" of the reminder already sent. A new assignee or due date gives a different key,
        // so it earns a new reminder without any reset logic; the same key is never sent twice.
        [MaxLength(80)]
        public string? ReminderSentFor { get; set; }

        public DateTime? CompletedAtUtc { get; set; }

        // A task a person has assigned or dated is treated like a manual one: a regenerated summary keeps it.
        [NotMapped]
        public bool IsTracked => AssigneeUserId != null || DueDate != null;

        public string ReminderKey() => $"{AssigneeUserId}|{DueDate:yyyy-MM-dd}";

        // --- NAVIGATION PROPERTIES ---

        // Foreign key linking the action item to its parent meeting.
        [Required]
        public int MeetingSummaryId { get; set; }

        [ForeignKey("MeetingSummaryId")]
        public MeetingSummary? MeetingSummary { get; set; }
    }
}