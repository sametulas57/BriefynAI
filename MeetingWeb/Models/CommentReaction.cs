using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeetingWeb.Models
{
    // Entity mapping to enforce the rule: 1 User = 1 Reaction per Comment.
    public class CommentReaction
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int CommentId { get; set; }

        [ForeignKey("CommentId")]
        public MeetingComment? Comment { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        // State flag representing reaction type (True = Like, False = Dislike).
        [Required]
        public bool IsLike { get; set; }
    }
}
