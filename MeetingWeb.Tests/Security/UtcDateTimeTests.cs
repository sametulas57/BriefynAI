using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingWeb.Tests.Security
{
    public class UtcDateTimeFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    public class UtcDateTimeTests(UtcDateTimeFixture fixture) : IClassFixture<UtcDateTimeFixture>
    {
        private readonly BriefynFactory _app = fixture.Factory;

        [Theory]
        [InlineData(false, DateTimeKind.Local)]
        [InlineData(true, DateTimeKind.Local)]
        [InlineData(false, DateTimeKind.Unspecified)]
        [InlineData(true, DateTimeKind.Unspecified)]
        public async Task Saving_non_utc_time_is_rejected(bool asyncSave, DateTimeKind kind)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                db.Workspaces.Add(new Workspace { Name = "UTC test", CreatedAtUtc = DateTime.SpecifyKind(DateTime.Now, kind) });
                if (asyncSave) await db.SaveChangesAsync();
                else db.SaveChanges();
            });

            Assert.Contains($"Non-UTC DateTime written: Workspace.CreatedAtUtc (Kind={kind})", error.Message);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Saving_utc_time_succeeds_and_a_new_context_reads_utc(bool asyncSave)
        {
            var now = DateTime.UtcNow;
            int id;
            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var meeting = new Meeting { WorkspaceId = _app.Seed.WorkspaceA, Title = "UTC test", CreatedAtUtc = now, CompletedAtUtc = now };
                db.Meetings.Add(meeting);
                if (asyncSave) await db.SaveChangesAsync();
                else db.SaveChanges();
                id = meeting.Id;
            }

            _app.WithDb(db =>
            {
                var saved = db.Meetings.AsNoTracking().Single(m => m.Id == id);
                Assert.Equal(now, saved.CreatedAtUtc);
                Assert.Equal(DateTimeKind.Utc, saved.CreatedAtUtc.Kind);
                Assert.Equal(now, saved.CompletedAtUtc);
                Assert.Equal(DateTimeKind.Utc, saved.CompletedAtUtc!.Value.Kind);
                return true;
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Modifying_a_nullable_time_to_non_utc_is_rejected(bool asyncSave)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                var meeting = await db.Meetings.SingleAsync(m => m.Id == _app.Seed.MeetingA);
                meeting.CompletedAtUtc = DateTime.Now;
                if (asyncSave) await db.SaveChangesAsync();
                else db.SaveChanges();
            });

            Assert.Contains("Non-UTC DateTime written: Meeting.CompletedAtUtc (Kind=Local)", error.Message);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Saving_another_property_does_not_check_an_unmodified_time(bool asyncSave)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var workspace = await db.Workspaces.SingleAsync(w => w.Id == _app.Seed.WorkspaceB);
            var createdAt = workspace.CreatedAtUtc;
            workspace.CreatedAtUtc = DateTime.SpecifyKind(createdAt, DateTimeKind.Local);
            workspace.Name = "UTC property test " + Guid.NewGuid();
            db.ChangeTracker.DetectChanges();
            Assert.False(db.Entry(workspace).Property(w => w.CreatedAtUtc).IsModified);

            if (asyncSave) await db.SaveChangesAsync();
            else db.SaveChanges();

            _app.WithDb(readDb =>
            {
                var saved = readDb.Workspaces.AsNoTracking().Single(w => w.Id == workspace.Id);
                Assert.Equal(workspace.Name, saved.Name);
                Assert.Equal(createdAt, saved.CreatedAtUtc);
                Assert.Equal(DateTimeKind.Utc, saved.CreatedAtUtc.Kind);
                return true;
            });
        }
    }
}
