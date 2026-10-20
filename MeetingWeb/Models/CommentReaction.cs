using System.ComponentModel.DataAnnotations;

namespace MeetingWeb.Models
{
    // A like or dislike; the database allows one reaction per user per comment.
    public class CommentReaction
    {
        [Key]
        public int Id { get; set; }

        public int CommentId { get; set; }
        public Comment? Comment { get; set; }

        [Required]
        [MaxLength(450)]
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser? User { get; set; }

        // State flag representing reaction type (True = Like, False = Dislike).
        public bool IsLike { get; set; }
    }
}
