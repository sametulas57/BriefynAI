using System.ComponentModel.DataAnnotations;

namespace MeetingWeb.Models
{
    public class Workspace
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        [Display(Name = "Çalışma Alanı Adı")]
        public string Name { get; set; } = string.Empty;

        // The private space created at registration (one per user, enforced in the database). It cannot be deleted,
        // and inviting people into it is possible but discouraged. Decided by this flag, not by the name.
        public bool IsPersonal { get; set; }

        [MaxLength(450)]
        public string? CreatedByUserId { get; set; }
        public ApplicationUser? CreatedBy { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public ICollection<WorkspaceMember> Members { get; set; } = new List<WorkspaceMember>();

        public ICollection<Meeting> Meetings { get; set; } = new List<Meeting>();
    }
}
