using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MeetingWeb.Models;

namespace MeetingWeb.Data
{
    // Schema v2 (ADR-0009). Rules that must hold even when two requests race (one reaction per user, one personal
    // space per user, unique phone and invitation) live in the database, not only in code.
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Workspace> Workspaces { get; set; }
        public DbSet<WorkspaceMember> WorkspaceMembers { get; set; }
        public DbSet<WorkspaceInvitation> WorkspaceInvitations { get; set; }
        public DbSet<WorkspaceTitle> WorkspaceTitles { get; set; }
        public DbSet<Meeting> Meetings { get; set; }
        public DbSet<MeetingSeries> MeetingSeries { get; set; }
        public DbSet<MeetingTranscript> MeetingTranscripts { get; set; }
        public DbSet<MeetingPoint> MeetingPoints { get; set; }
        public DbSet<ActionItem> ActionItems { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<CommentReaction> CommentReactions { get; set; }
        public DbSet<ZoomConnection> ZoomConnections { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            // Required for Identity framework configuration.
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>(u =>
            {
                u.Property(x => x.PhoneNumber).HasMaxLength(20);
                u.HasIndex(x => x.PhoneNumber).IsUnique().HasFilter("[PhoneNumber] IS NOT NULL AND [PhoneNumber] <> ''");
            });

            builder.Entity<Workspace>(w =>
            {
                w.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.ClientSetNull);
                w.HasIndex(x => x.CreatedByUserId).IsUnique().HasFilter("[IsPersonal] = 1").HasDatabaseName("IX_Workspaces_PersonalPerUser");
            });

            builder.Entity<WorkspaceMember>(m =>
            {
                m.HasKey(x => new { x.WorkspaceId, x.UserId });
                m.HasOne(x => x.Workspace).WithMany(w => w.Members).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
                m.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
                m.HasIndex(x => x.UserId);
                // Not a database cascade: SQL Server refuses a second cascade path from Workspaces to WorkspaceMembers.
                // DeleteTitle clears the members first; deleting a team removes titles and members in the same cascade.
                m.HasOne(x => x.Title).WithMany().HasForeignKey(x => x.TitleId).OnDelete(DeleteBehavior.ClientSetNull);
                m.ToTable(t => t.HasCheckConstraint("CK_WorkspaceMembers_Role", "[Role] IN (1, 2, 3)"));
            });

            builder.Entity<WorkspaceTitle>(t =>
            {
                t.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
                t.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
            });

            builder.Entity<WorkspaceInvitation>(i =>
            {
                i.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
                i.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.ClientSetNull);
                i.HasOne(x => x.UsedBy).WithMany().HasForeignKey(x => x.UsedByUserId).OnDelete(DeleteBehavior.ClientSetNull);
                i.HasIndex(x => x.TokenHash).IsUnique();
            });

            builder.Entity<Meeting>(m =>
            {
                m.Property(x => x.Title).HasConversion(new EncryptedConverter("Meetings.Title"));
                m.HasOne(x => x.Workspace).WithMany(w => w.Meetings).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
                m.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.ClientSetNull);
                // Not a database cascade: SQL Server refuses a second cascade path from Workspaces to Meetings. Deleting a
                // series clears the link in code (MeetingSeriesService); deleting a team removes both in the same cascade.
                m.HasOne(x => x.Series).WithMany(s => s.Meetings).HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.ClientSetNull);
                // History lists a workspace's meetings newest first.
                m.HasIndex(x => new { x.WorkspaceId, x.CreatedAtUtc });
                m.HasIndex(x => new { x.WorkspaceId, x.ExternalRecordingId }).IsUnique().HasFilter("[ExternalRecordingId] IS NOT NULL");
                // Zoom UUIDs are case-sensitive; SQL Server's default comparison is not.
                if (Database.IsSqlServer()) m.Property(x => x.ExternalRecordingId).UseCollation("Latin1_General_100_BIN2");
                m.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_Meetings_Status", "[Status] IN (0, 1, 2)");
                    t.HasCheckConstraint("CK_Meetings_FailureReason", "[FailureReason] IS NULL OR [FailureReason] IN (1)");
                    t.HasCheckConstraint("CK_Meetings_Source", "[Source] IN (1, 2)");
                });
            });

            builder.Entity<MeetingSeries>(s =>
            {
                s.Property(x => x.Name).HasConversion(new EncryptedConverter("MeetingSeries.Name"));
                s.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
                s.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.ClientSetNull);
                // Two sessions of one Zoom meeting imported at once must still land in a single series.
                s.HasIndex(x => new { x.WorkspaceId, x.ZoomMeetingKey }).IsUnique().HasFilter("[ZoomMeetingKey] IS NOT NULL");
            });

            builder.Entity<MeetingTranscript>(t =>
            {
                t.HasKey(x => x.MeetingId);
                t.Property(x => x.Text).HasConversion(new EncryptedConverter("MeetingTranscripts.Text"));
                t.HasOne(x => x.Meeting).WithOne(m => m.Transcript).HasForeignKey<MeetingTranscript>(x => x.MeetingId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<MeetingPoint>(p =>
            {
                p.Property(x => x.Text).HasConversion(new EncryptedConverter("MeetingPoints.Text"));
                p.HasOne(x => x.Meeting).WithMany(m => m.Points).HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.Cascade);
                p.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.ClientSetNull);
                p.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_MeetingPoints_Kind", "[Kind] IN (1, 2, 3)");
                    t.HasCheckConstraint("CK_MeetingPoints_Source", "[Source] IN (1, 2)");
                });
            });

            builder.Entity<ActionItem>(a =>
            {
                a.Property(x => x.Text).HasConversion(new EncryptedConverter("ActionItems.Text"));
                a.HasOne(x => x.Meeting).WithMany(m => m.ActionItems).HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.Cascade);
                a.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.ClientSetNull);
                a.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeUserId).OnDelete(DeleteBehavior.ClientSetNull);
                a.HasOne(x => x.CompletedBy).WithMany().HasForeignKey(x => x.CompletedByUserId).OnDelete(DeleteBehavior.ClientSetNull);
                // "Bana atananlar" looks tasks up by assignee.
                a.HasIndex(x => x.AssigneeUserId);
                a.ToTable(t => t.HasCheckConstraint("CK_ActionItems_Source", "[Source] IN (1, 2)"));
            });

            builder.Entity<Comment>(c =>
            {
                // Comments discuss meeting content, so they are encrypted too.
                c.Property(x => x.Content).HasConversion(new EncryptedConverter("Comments.Content"));
                c.HasOne(x => x.Meeting).WithMany(m => m.Comments).HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.Cascade);
                c.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.ClientCascade);
                // Replies are removed with their thread by the meeting cascade or by the delete handler.
                c.HasOne(x => x.ParentComment).WithMany(x => x.Replies).HasForeignKey(x => x.ParentCommentId).OnDelete(DeleteBehavior.ClientCascade);
            });

            builder.Entity<CommentReaction>(r =>
            {
                r.HasOne(x => x.Comment).WithMany(c => c.Reactions).HasForeignKey(x => x.CommentId).OnDelete(DeleteBehavior.Cascade);
                r.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.ClientCascade);
                r.HasIndex(x => new { x.CommentId, x.UserId }).IsUnique();
            });

            builder.Entity<ZoomConnection>(z =>
            {
                z.Property(x => x.AccessToken).HasConversion(new EncryptedConverter("ZoomConnections.AccessToken"));
                z.Property(x => x.RefreshToken).HasConversion(new EncryptedConverter("ZoomConnections.RefreshToken"));
                z.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
                z.HasIndex(x => x.UserId).IsUnique();
                z.HasOne(x => x.AutoImportWorkspace).WithMany().HasForeignKey(x => x.AutoImportWorkspaceId).OnDelete(DeleteBehavior.SetNull);
                // Zoom's notifications name the Zoom user (ADR-0012).
                z.HasIndex(x => x.ZoomUserId);
            });

            // SQLite (tests) cannot generate rowversion values; there StampRowVersions sets a new one on every save, so the
            // same concurrency check runs in tests as on SQL Server.
            if (!Database.IsSqlServer())
            {
                builder.Entity<Meeting>().Property(x => x.RowVersion).ValueGeneratedNever();
                builder.Entity<ActionItem>().Property(x => x.RowVersion).ValueGeneratedNever();
                builder.Entity<ZoomConnection>().Property(x => x.RowVersion).ValueGeneratedNever();
            }
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            StampRowVersions();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            StampRowVersions();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void StampRowVersions()
        {
            if (Database.IsSqlServer()) return;
            foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
            {
                if (entry.Metadata.FindProperty("RowVersion") != null) entry.Property("RowVersion").CurrentValue = Guid.NewGuid().ToByteArray();
            }
        }
    }
}
