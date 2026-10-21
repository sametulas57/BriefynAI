using System.ComponentModel.DataAnnotations;

namespace MeetingWeb.Models
{
    // A label a team gives its people (a unit or a profession, the team decides). It groups people when sharing a
    // report and gives no permissions; permissions come only from WorkspaceRole.
    public class WorkspaceTitle
    {
        public const int NameMaxLength = 50;

        [Key]
        public int Id { get; set; }

        public int WorkspaceId { get; set; }
        public Workspace? Workspace { get; set; }

        [Required]
        [MaxLength(NameMaxLength)]
        public string Name { get; set; } = string.Empty;

        // One of TitleIcons.All (a Bootstrap Icons name without the "bi-" prefix); null for no icon.
        [MaxLength(50)]
        public string? Icon { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
