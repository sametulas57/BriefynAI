using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeetingWeb.Models
{
    // Represents an actionable task extracted from a meeting summary or added by a user.
    public class ActionItem
    {
        [Key]
        public int Id { get; set; }

        public int MeetingId { get; set; }
        public Meeting? Meeting { get; set; }

        // The descriptive text of the action item. (Encrypted at rest via DbContext).
        [Required]
        public string Text { get; set; } = string.Empty;

        public EntrySource Source { get; set; } = EntrySource.Ai;

        // Author of a manual task; null for AI tasks.
        [MaxLength(450)]
        public string? CreatedByUserId { get; set; }
        public ApplicationUser? CreatedBy { get; set; }

        // --- ASSIGNMENT ---
        // Who should do it (a member of the meeting's team) and by when. Ids are kept in clear so the
        // "Bana atananlar" list can be queried; names are resolved from the user's profile at display time.
        [MaxLength(450)]
        public string? AssigneeUserId { get; set; }
        public ApplicationUser? Assignee { get; set; }

        public DateOnly? DueDate { get; set; }

        // Who marked it done and when, shown in the archived summary. A task is done when CompletedAtUtc is set.
        [MaxLength(450)]
        public string? CompletedByUserId { get; set; }
        public ApplicationUser? CompletedBy { get; set; }

        public DateTime? CompletedAtUtc { get; set; }

        public int SortOrder { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [Timestamp]
        public byte[]? RowVersion { get; set; }

        [NotMapped]
        public bool IsDone => CompletedAtUtc != null;

        // Marks the task done (or open again), keeping "who completed it" in step with the completion time.
        public void SetDone(bool done, string? userId)
        {
            CompletedByUserId = done ? userId : null;
            CompletedAtUtc = done ? DateTime.UtcNow : null;
        }
    }
}
