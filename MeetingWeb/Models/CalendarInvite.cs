using System.ComponentModel.DataAnnotations;

namespace MeetingWeb.Models
{
    public enum CalendarInviteStatus { Active = 1, Cancelled = 2 }

    // Record of the calendar invite sent for one task to one person (calendar sync, SPEC.md). Lives independently of the
    // task: no foreign key to ActionItems, so deleting a meeting keeps the UID and sequence needed to send the cancellation.
    public class CalendarInvite
    {
        [Key]
        public int Id { get; set; }

        public int ActionItemId { get; set; }

        [Required, MaxLength(450)]
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser? User { get; set; }

        // iCalendar UID, stable for the record's life.
        [Required, MaxLength(100)]
        public string Uid { get; set; } = string.Empty;

        // iCalendar SEQUENCE, raised on every send.
        public int Sequence { get; set; }

        // The date the person's calendar currently holds.
        public DateOnly DueDate { get; set; }

        public CalendarInviteStatus Status { get; set; } = CalendarInviteStatus.Active;

        // Saved before the e-mail goes out, cleared after (plan decision 3).
        public bool SendPending { get; set; }

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        [Timestamp]
        public uint RowVersion { get; set; }
    }
}
