using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Tests.Security
{
    public class CalendarInviteSchemaTests : IClassFixture<SchemaFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public CalendarInviteSchemaTests(SchemaFixture fixture) => _app = fixture.Factory;

        private void SaveFails(Action<ApplicationDbContext> change) =>
            Assert.Throws<DbUpdateException>(() => _app.WithDb(db => { change(db); return db.SaveChanges(); }));

        private static CalendarInvite Invite(int actionItemId, string userId) => new()
        {
            ActionItemId = actionItemId,
            UserId = userId,
            Uid = Guid.NewGuid().ToString("N") + "@briefyn.test",
            DueDate = new DateOnly(2026, 12, 1)
        };

        [Fact]
        public void One_invite_record_per_task_and_person()
        {
            _app.WithDb(db => { db.CalendarInvites.Add(Invite(S.TaskA, S.AliceId)); return db.SaveChanges(); });

            SaveFails(db => db.CalendarInvites.Add(Invite(S.TaskA, S.AliceId)));

            _app.WithDb(db => { db.CalendarInvites.Add(Invite(S.TaskA, S.CarolId)); return db.SaveChanges(); });
            Assert.Equal(2, _app.WithDb(db => db.CalendarInvites.Count(i => i.ActionItemId == S.TaskA)));
        }

        [Fact]
        public void Invite_uid_is_unique()
        {
            var invite = Invite(S.TaskB, S.BobId);
            _app.WithDb(db => { db.CalendarInvites.Add(invite); return db.SaveChanges(); });

            SaveFails(db =>
            {
                var duplicate = Invite(S.TaskB, S.CarolId);
                duplicate.Uid = invite.Uid;
                db.CalendarInvites.Add(duplicate);
            });
        }

        [Fact]
        public void Invite_record_survives_without_its_task()
        {
            const int missingTaskId = int.MaxValue;
            Assert.False(_app.WithDb(db => db.ActionItems.Any(a => a.Id == missingTaskId)));
            var invite = Invite(missingTaskId, S.AliceId);

            _app.WithDb(db => { db.CalendarInvites.Add(invite); return db.SaveChanges(); });

            Assert.True(_app.WithDb(db => db.CalendarInvites.Any(i => i.Id == invite.Id)));
        }

        [Fact]
        public void Deleting_the_user_removes_their_invite_records()
        {
            var user = new ApplicationUser { UserName = "calendar@briefyn.test" };
            var invite = Invite(S.TaskB, user.Id);
            _app.WithDb(db =>
            {
                db.Users.Add(user);
                db.CalendarInvites.Add(invite);
                return db.SaveChanges();
            });

            // A fresh context leaves the invite untracked, so the database must cascade the delete.
            _app.WithDb(db => { db.Users.Remove(db.Users.Single(u => u.Id == user.Id)); return db.SaveChanges(); });

            Assert.False(_app.WithDb(db => db.CalendarInvites.Any(i => i.Id == invite.Id)));
        }

        [Fact]
        public void Calendar_invites_are_on_by_default_for_new_users()
        {
            Assert.True(new ApplicationUser().CalendarInvitesEnabled);
            Assert.True(_app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == S.AliceId).CalendarInvitesEnabled));
        }

        [Fact]
        public void Public_url_setting_is_empty_by_default()
        {
            var options = _app.Services.GetRequiredService<IOptions<AppOptions>>();

            Assert.Equal("", options.Value.PublicUrl);
        }

        [Fact]
        public void Concurrent_update_of_an_invite_record_is_detected()
        {
            var invite = Invite(S.TaskB, S.AliceId);
            _app.WithDb(db => { db.CalendarInvites.Add(invite); return db.SaveChanges(); });
            using var scope1 = _app.Services.CreateScope();
            using var scope2 = _app.Services.CreateScope();
            var first = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var second = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var a = first.CalendarInvites.Single(i => i.Id == invite.Id);
            var b = second.CalendarInvites.Single(i => i.Id == invite.Id);

            a.Sequence++;
            b.Sequence++;
            first.SaveChanges();

            Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());
        }
    }
}
