using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeetingWeb.Models
{
    // Entity representing a user comment or reply within a meeting.
    public class Comment
    {
        [Key]
        public int Id { get; set; }

        public int MeetingId { get; set; }
        public Meeting? Meeting { get; set; }

        [Required]
        [MaxLength(450)]
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser? User { get; set; }

        // Longest comment a user may write; checked in the page handler. The column itself has no limit because it
        // stores ciphertext, which is longer than the text.
        public const int MaxContentLength = 2000;

        [Required(ErrorMessage = "Comment content cannot be empty.")]
        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // Self-referencing foreign key to support nested/hierarchical reply threads.
        public int? ParentCommentId { get; set; }
        public Comment? ParentComment { get; set; }

        // Navigation property for child replies in the thread.
        public ICollection<Comment> Replies { get; set; } = new List<Comment>();

        // Navigation property for user reactions (likes/dislikes).
        public ICollection<CommentReaction> Reactions { get; set; } = new List<CommentReaction>();

        // Computed property for UI rendering; requires eager loading of Reactions.
        [NotMapped]
        public int LikeCount => Reactions?.Count(r => r.IsLike) ?? 0;

        [NotMapped]
        public int DislikeCount => Reactions?.Count(r => !r.IsLike) ?? 0;
    }
}
