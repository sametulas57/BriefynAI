using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeetingWeb.Models
{
    // Entity representing a user comment or reply within a meeting summary.
    public class MeetingComment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int MeetingId { get; set; }

        [ForeignKey("MeetingId")]
        public MeetingSummary? Meeting { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public IdentityUser? User { get; set; }

        [Required(ErrorMessage = "Comment content cannot be empty.")]
        [MaxLength(2000)]
        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Self-referencing foreign key to support nested/hierarchical reply threads.
        public int? ParentCommentId { get; set; }

        [ForeignKey("ParentCommentId")]
        public MeetingComment? ParentComment { get; set; }

        // Navigation property for child replies in the thread.
        public ICollection<MeetingComment> Replies { get; set; } = new List<MeetingComment>();

        // Navigation property for user reactions (likes/dislikes).
        public ICollection<CommentReaction> Reactions { get; set; } = new List<CommentReaction>();

        // Computed property for UI rendering; requires eager loading of Reactions.
        [NotMapped]
        public int LikeCount => Reactions?.Count(r => r.IsLike) ?? 0;

        [NotMapped]
        public int DislikeCount => Reactions?.Count(r => !r.IsLike) ?? 0;
    }

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