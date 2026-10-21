using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace MeetingWeb.Models
{
    // Identity user with the profile fields the app shows (kept as columns, not claims, so they can be required,
    // length-limited and sorted).
    public class ApplicationUser : IdentityUser
    {
        public const int FullNameMaxLength = 100;

        [Required]
        [MaxLength(FullNameMaxLength)]
        public string FullName { get; set; } = string.Empty;

        // File name inside wwwroot/uploads/avatars; null when the user has no photo.
        [MaxLength(100)]
        public string? AvatarFileName { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? DeletionRequestedAtUtc { get; set; }
    }
}
