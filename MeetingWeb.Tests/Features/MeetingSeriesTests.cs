using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingWeb.Tests.Features
{
    public class MeetingSeriesFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // PRD iç proje notu criteria 1–5 and 8, ADR-0013. Pages call this service, so
    // the permission rules are tested here; page tests check that pages pass the signed-in user through.
    public class MeetingSeriesTests : IClassFixture<MeetingSeriesFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public MeetingSeriesTests(MeetingSeriesFixture fixture) => _app = fixture.Factory;

        private async Task<T> WithSeries<T>(Func<IMeetingSeriesService, Task<T>> action)
        {
            using var scope = _app.Services.CreateScope();
            return await action(scope.ServiceProvider.GetRequiredService<IMeetingSeriesService>());
        }

        private int AddMeeting(int workspaceId, string title = "Toplantı") => _app.WithDb(db =>
        {
            var meeting = new Meeting { WorkspaceId = workspaceId, Title = title, Status = MeetingStatus.Completed };
            db.Meetings.Add(meeting);
            db.SaveChanges();
            return meeting.Id;
        });

        private int? SeriesOf(int meetingId) =>
            _app.WithDb(db => db.Meetings.AsNoTracking().Single(m => m.Id == meetingId).SeriesId);

        private void MakeAdmin(string userId, int workspaceId) => _app.WithDb(db =>
        {
            db.WorkspaceMembers.Single(m => m.WorkspaceId == workspaceId && m.UserId == userId).Role = WorkspaceRole.Admin;
            return db.SaveChanges();
        });

        // Criterion 1
        [Fact]
        public async Task Member_creates_a_series_and_links_and_unlinks_a_meeting()
        {
            var meeting = AddMeeting(S.WorkspaceA);
            var series = await WithSeries(s => s.CreateAsync(S.CarolId, S.WorkspaceA, "  Pazartesi ekip toplantısı  "));

            Assert.NotNull(series);
            Assert.Equal("Pazartesi ekip toplantısı", series!.Name);
            Assert.Equal(S.CarolId, series.CreatedByUserId);
            Assert.Null(series.ZoomMeetingKey);

            Assert.Equal(SeriesResult.Ok, await WithSeries(s => s.SetMeetingSeriesAsync(S.CarolId, meeting, series.Id)));
            Assert.Equal(series.Id, SeriesOf(meeting));

            var listed = Assert.Single(await WithSeries(s => s.ListAsync(S.CarolId, S.WorkspaceA)), x => x.Id == series.Id);
            Assert.Equal(("Pazartesi ekip toplantısı", 1), (listed.Name, listed.MeetingCount));
            Assert.NotNull(listed.LastMeetingAtUtc);

            Assert.Equal(SeriesResult.Ok, await WithSeries(s => s.SetMeetingSeriesAsync(S.CarolId, meeting, null)));
            Assert.Null(SeriesOf(meeting));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task A_series_needs_a_name_of_at_most_100_characters(string blank)
        {
            Assert.Null(await WithSeries(s => s.CreateAsync(S.AliceId, S.WorkspaceA, blank)));
            Assert.Null(await WithSeries(s => s.CreateAsync(S.AliceId, S.WorkspaceA, new string('a', 101))));
            Assert.NotNull(await WithSeries(s => s.CreateAsync(S.AliceId, S.WorkspaceA, new string('a', 100))));
        }

        // Criterion 2
        [Fact]
        public async Task Another_teams_series_is_invisible_and_cannot_take_a_meeting()
        {
            var seriesB = (await WithSeries(s => s.CreateAsync(S.BobId, S.WorkspaceB, "B serisi")))!;
            var meetingA = AddMeeting(S.WorkspaceA);

            Assert.Null(await WithSeries(s => s.CreateAsync(S.AliceId, S.WorkspaceB, "Sızma")));
            Assert.Null(await WithSeries(s => s.FindAsync(S.AliceId, seriesB.Id)));
            Assert.Empty(await WithSeries(s => s.ListAsync(S.AliceId, S.WorkspaceB)));

            // Alice's own meeting into Bob's series, and Bob's meeting into nothing he can reach from Alice's side.
            Assert.Equal(SeriesResult.NotFound, await WithSeries(s => s.SetMeetingSeriesAsync(S.AliceId, meetingA, seriesB.Id)));
            Assert.Null(SeriesOf(meetingA));
            Assert.Equal(SeriesResult.NotFound, await WithSeries(s => s.SetMeetingSeriesAsync(S.AliceId, S.MeetingB, seriesB.Id)));
            Assert.Null(SeriesOf(S.MeetingB));

            // Bob is a member of B, but meeting A is not his: he cannot pull it into his series either.
            Assert.Equal(SeriesResult.NotFound, await WithSeries(s => s.SetMeetingSeriesAsync(S.BobId, meetingA, seriesB.Id)));
            Assert.Null(SeriesOf(meetingA));

            Assert.Equal(SeriesResult.NotFound, await WithSeries(s => s.RenameAsync(S.AliceId, seriesB.Id, "Değişti")));
            Assert.Equal(SeriesResult.NotFound, await WithSeries(s => s.DeleteAsync(S.AliceId, seriesB.Id)));
            Assert.Equal("B serisi", (await WithSeries(s => s.FindAsync(S.BobId, seriesB.Id)))!.Name);
        }

        // Criterion 3
        [Fact]
        public async Task Only_the_creator_founder_or_admin_renames_and_deletes()
        {
            var aliceSeries = (await WithSeries(s => s.CreateAsync(S.AliceId, S.WorkspaceA, "Alice'in serisi")))!;
            var carolSeries = (await WithSeries(s => s.CreateAsync(S.CarolId, S.WorkspaceA, "Carol'ın serisi")))!;

            // Carol is a plain member: her own series yes, the founder's no.
            Assert.False(await WithSeries(s => s.CanManageAsync(S.CarolId, aliceSeries)));
            Assert.Equal(SeriesResult.Forbidden, await WithSeries(s => s.RenameAsync(S.CarolId, aliceSeries.Id, "Değişti")));
            Assert.Equal(SeriesResult.Forbidden, await WithSeries(s => s.DeleteAsync(S.CarolId, aliceSeries.Id)));
            Assert.True(await WithSeries(s => s.CanManageAsync(S.CarolId, carolSeries)));
            Assert.Equal(SeriesResult.Ok, await WithSeries(s => s.RenameAsync(S.CarolId, carolSeries.Id, "Carol'ın yeni adı")));
            Assert.Equal(SeriesResult.Invalid, await WithSeries(s => s.RenameAsync(S.CarolId, carolSeries.Id, " ")));
            Assert.Equal("Carol'ın yeni adı", (await WithSeries(s => s.FindAsync(S.AliceId, carolSeries.Id)))!.Name);

            // The founder manages anyone's series.
            Assert.Equal(SeriesResult.Ok, await WithSeries(s => s.RenameAsync(S.AliceId, carolSeries.Id, "Kurucu değiştirdi")));
        }

        [Fact]
        public async Task An_admin_manages_any_series_of_the_team()
        {
            var dave = _app.WithDb(db =>
            {
                var user = new ApplicationUser { UserName = "dave@a.test", NormalizedUserName = "DAVE@A.TEST", Email = "dave@a.test", NormalizedEmail = "DAVE@A.TEST", SecurityStamp = Guid.NewGuid().ToString() };
                db.Users.Add(user);
                db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = S.WorkspaceA, UserId = user.Id, Role = WorkspaceRole.Member });
                db.SaveChanges();
                return user.Id;
            });
            var series = (await WithSeries(s => s.CreateAsync(S.AliceId, S.WorkspaceA, "Yönetici denemesi")))!;

            Assert.Equal(SeriesResult.Forbidden, await WithSeries(s => s.DeleteAsync(dave, series.Id)));
            MakeAdmin(dave, S.WorkspaceA);
            Assert.Equal(SeriesResult.Ok, await WithSeries(s => s.DeleteAsync(dave, series.Id)));
        }

        // Criterion 3 (deleting keeps the meetings)
        [Fact]
        public async Task Deleting_a_series_keeps_its_meetings_and_clears_their_link()
        {
            var series = (await WithSeries(s => s.CreateAsync(S.AliceId, S.WorkspaceA, "Silinecek seri")))!;
            var first = AddMeeting(S.WorkspaceA, "Birinci");
            var second = AddMeeting(S.WorkspaceA, "İkinci");
            await WithSeries(s => s.SetMeetingSeriesAsync(S.AliceId, first, series.Id));
            await WithSeries(s => s.SetMeetingSeriesAsync(S.CarolId, second, series.Id));

            Assert.Equal(SeriesResult.Ok, await WithSeries(s => s.DeleteAsync(S.AliceId, series.Id)));

            Assert.Null(await WithSeries(s => s.FindAsync(S.AliceId, series.Id)));
            Assert.True(_app.WithDb(db => db.Meetings.Count(m => m.Id == first || m.Id == second)) == 2);
            Assert.Null(SeriesOf(first));
            Assert.Null(SeriesOf(second));
        }

        // Criterion 4
        [Fact]
        public async Task Series_name_is_encrypted_in_the_database()
        {
            var series = (await WithSeries(s => s.CreateAsync(S.AliceId, S.WorkspaceA, "Gizli müşteri görüşmesi")))!;

            var raw = RawColumn("Name", series.Id);
            Assert.StartsWith(EncryptionHelper.Prefix, raw);
            Assert.DoesNotContain("Gizli", raw);
        }

        // Criterion 5 (the upload page asks this before writing the series on the new meeting)
        [Fact]
        public async Task Upload_accepts_only_a_series_of_the_target_team()
        {
            var seriesA = (await WithSeries(s => s.CreateAsync(S.AliceId, S.WorkspaceA, "Yükleme serisi")))!;
            var seriesB = (await WithSeries(s => s.CreateAsync(S.BobId, S.WorkspaceB, "Başka ekip")))!;

            Assert.True(await WithSeries(s => s.BelongsToWorkspaceAsync(seriesA.Id, S.WorkspaceA)));
            Assert.False(await WithSeries(s => s.BelongsToWorkspaceAsync(seriesB.Id, S.WorkspaceA)));
            Assert.False(await WithSeries(s => s.BelongsToWorkspaceAsync(999999, S.WorkspaceA)));
        }

        // Criteria 6 and 8 (the service side; the Zoom import path is tested with the Zoom tests)
        [Fact]
        public async Task Zoom_series_is_found_by_a_keyed_hash_of_the_meeting_number_per_team()
        {
            const long number = 85746352419;
            var first = await WithSeries(s => s.FindOrCreateForZoomAsync(S.WorkspaceA, number, "Haftalık sprint", S.AliceId));
            var again = await WithSeries(s => s.FindOrCreateForZoomAsync(S.WorkspaceA, number, "Konu değişti", S.CarolId));
            var otherTeam = await WithSeries(s => s.FindOrCreateForZoomAsync(S.WorkspaceB, number, "Haftalık sprint", S.BobId));

            Assert.Equal(first, again);
            Assert.NotEqual(first, otherTeam);
            Assert.Equal("Haftalık sprint", (await WithSeries(s => s.FindAsync(S.CarolId, first)))!.Name);

            var stored = RawColumn("ZoomMeetingKey", first);
            Assert.Equal(64, stored.Length);
            Assert.DoesNotContain(number.ToString(), stored);
            Assert.Equal(MeetingSeriesService.ZoomMeetingKey(number), stored);
            // Not a plain hash: a guessed number cannot be checked without the server key.
            Assert.NotEqual(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(number.ToString()))).ToLowerInvariant(), stored);
            Assert.DoesNotContain(number.ToString(), AllSeriesText());
        }

        [Theory]
        [InlineData(3, true)]
        [InlineData(8, true)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(4, false)]
        [InlineData(9, false)]
        public void Only_recurring_zoom_meetings_start_a_series(int zoomType, bool expected) =>
            Assert.Equal(expected, MeetingSeriesService.StartsSeries(zoomType));

        [Fact]
        public void A_zoom_meeting_number_belongs_to_one_series_per_team()
        {
            var key = MeetingSeriesService.ZoomMeetingKey(11122233344);
            _app.WithDb(db => { db.MeetingSeries.Add(new MeetingSeries { WorkspaceId = S.WorkspaceA, Name = "Bir", ZoomMeetingKey = key }); return db.SaveChanges(); });

            Assert.Throws<DbUpdateException>(() => _app.WithDb(db =>
            {
                db.MeetingSeries.Add(new MeetingSeries { WorkspaceId = S.WorkspaceA, Name = "İki", ZoomMeetingKey = key });
                return db.SaveChanges();
            }));
            // Series without a Zoom number are not limited.
            _app.WithDb(db =>
            {
                db.MeetingSeries.AddRange(new MeetingSeries { WorkspaceId = S.WorkspaceA, Name = "Elle 1" }, new MeetingSeries { WorkspaceId = S.WorkspaceA, Name = "Elle 2" });
                return db.SaveChanges();
            });
        }

        [Fact]
        public void Pending_points_are_a_valid_kind()
        {
            _app.WithDb(db =>
            {
                db.MeetingPoints.Add(new MeetingPoint { MeetingId = S.MeetingA, Kind = MeetingPointKind.Pending, Text = "Bütçe cevabı bekleniyor" });
                return db.SaveChanges();
            });
            Assert.Equal(3, (int)MeetingPointKind.Pending);
        }

        [Fact]
        public async Task Deleting_a_team_removes_its_series()
        {
            var team = _app.WithDb(db =>
            {
                var ws = new Workspace { Name = "Geçici ekip" };
                db.Workspaces.Add(ws);
                db.SaveChanges();
                db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = ws.Id, UserId = S.AliceId, Role = WorkspaceRole.Owner });
                db.SaveChanges();
                return ws.Id;
            });
            var series = (await WithSeries(s => s.CreateAsync(S.AliceId, team, "Geçici seri")))!;
            var meeting = AddMeeting(team);
            await WithSeries(s => s.SetMeetingSeriesAsync(S.AliceId, meeting, series.Id));

            _app.WithDb(db => db.Workspaces.Where(w => w.Id == team).ExecuteDelete());

            Assert.False(_app.WithDb(db => db.MeetingSeries.Any(x => x.Id == series.Id)));
            Assert.False(_app.WithDb(db => db.Meetings.Any(m => m.Id == meeting)));
        }

        private string RawColumn(string column, int seriesId) => _app.WithDb(db =>
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT \"{column}\" FROM \"MeetingSeries\" WHERE \"Id\" = $id";
            command.Parameters.AddWithValue("$id", seriesId);
            return (string)command.ExecuteScalar()!;
        });

        private string AllSeriesText() => _app.WithDb(db =>
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT group_concat(COALESCE(\"Name\", '') || '|' || COALESCE(\"ZoomMeetingKey\", ''), ';') FROM \"MeetingSeries\"";
            return command.ExecuteScalar() as string ?? "";
        });
    }
}
