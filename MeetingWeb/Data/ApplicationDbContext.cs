using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MeetingWeb.Models;

namespace MeetingWeb.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<MeetingSummary> MeetingSummaries { get; set; }
        public DbSet<Workspace> Workspaces { get; set; }
        public DbSet<WorkspaceUser> WorkspaceUsers { get; set; }
        public DbSet<WorkspaceInvitation> WorkspaceInvitations { get; set; }
        public DbSet<MeetingComment> MeetingComments { get; set; }
        public DbSet<CommentReaction> CommentReactions { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            // Required for Identity framework configuration.
            base.OnModelCreating(builder);

            var stringConverter = new EncryptedConverter();
            var listConverter = new EncryptedStringListConverter();

            // Encrypt sensitive meeting topics in the database.
            builder.Entity<MeetingSummary>()
                .Property(m => m.ToplantiKonusu)
                .HasConversion(stringConverter);

            // Encrypt sensitive lists (discussed topics and decisions).
            builder.Entity<MeetingSummary>()
                .Property(m => m.GorusulenKonular)
                .HasConversion(listConverter);

            builder.Entity<MeetingSummary>()
                .Property(m => m.AlinanKararlar)
                .HasConversion(listConverter);

            // Encrypt action item texts.
            builder.Entity<ActionItem>()
                .Property(a => a.Text)
                .HasConversion(stringConverter);

            // Configure composite primary key for the many-to-many Workspace-User relationship.
            builder.Entity<WorkspaceUser>()
                .HasKey(wu => new { wu.WorkspaceId, wu.UserId });

            // Configure the Workspace side of the relationship.
            builder.Entity<WorkspaceUser>()
                .HasOne(wu => wu.Workspace)
                .WithMany(w => w.WorkspaceUsers)
                .HasForeignKey(wu => wu.WorkspaceId);

            // Configure the User side of the relationship.
            builder.Entity<WorkspaceUser>()
                .HasOne(wu => wu.User)
                .WithMany()
                .HasForeignKey(wu => wu.UserId);

            // Prevent cascade delete on nested comments to maintain comment history integrity.
            builder.Entity<MeetingComment>()
                .HasOne(c => c.ParentComment)
                .WithMany(c => c.Replies)
                .HasForeignKey(c => c.ParentCommentId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}