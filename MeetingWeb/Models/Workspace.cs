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

        // Standardize timestamps using UTC to prevent server timezone anomalies.
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property: One-to-many relationship with Workspace members.
        public ICollection<WorkspaceUser> WorkspaceUsers { get; set; } = new List<WorkspaceUser>();

        // Navigation property: One-to-many relationship with Meeting summaries.
        public ICollection<MeetingSummary> Meetings { get; set; } = new List<MeetingSummary>();
    }
}