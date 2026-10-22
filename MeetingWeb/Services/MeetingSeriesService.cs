using System.Globalization;
using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Services
{
    public enum SeriesResult { Ok, NotFound, Forbidden, Invalid }

    public record SeriesSummary(int Id, string Name, int MeetingCount, DateTime? LastMeetingAtUtc);

    // Meeting series (PRD iç proje notu, ADR-0013). Every call that takes a user
    // checks team membership here (ADR-0005); another team's series behaves as if it did not exist.
    public interface IMeetingSeriesService
    {
        Task<MeetingSeries?> FindAsync(string? userId, int seriesId);
        Task<List<SeriesSummary>> ListAsync(string? userId, int workspaceId);
        Task<MeetingSeries?> CreateAsync(string? userId, int workspaceId, string? name);
        Task<bool> CanManageAsync(string? userId, MeetingSeries series);
        Task<SeriesResult> RenameAsync(string? userId, int seriesId, string? name);
        Task<SeriesResult> DeleteAsync(string? userId, int seriesId);
        Task<SeriesResult> SetMeetingSeriesAsync(string? userId, int meetingId, int? seriesId);
        Task<bool> BelongsToWorkspaceAsync(int seriesId, int workspaceId);
        Task<int> FindOrCreateForZoomAsync(int workspaceId, long zoomMeetingNumber, string topic, string? createdByUserId);
    }

    public class MeetingSeriesService : IMeetingSeriesService
    {
        private readonly ApplicationDbContext _db;

        public MeetingSeriesService(ApplicationDbContext db) => _db = db;

        // Zoom meeting types 3 (recurring, no fixed time) and 8 (recurring, fixed time) keep one number for every session.
        // Type 4 (personal meeting ID) reuses one number for unrelated meetings, so it never starts a series.
        public static bool StartsSeries(int zoomMeetingType) => zoomMeetingType is 3 or 8;

        public static string ZoomMeetingKey(long zoomMeetingNumber) =>
            EncryptionHelper.KeyedHash(zoomMeetingNumber.ToString(CultureInfo.InvariantCulture), "MeetingSeries.ZoomMeetingKey");

        private Task<WorkspaceMember?> MembershipAsync(string? userId, int workspaceId) =>
            string.IsNullOrEmpty(userId)
                ? Task.FromResult<WorkspaceMember?>(null)
                : _db.WorkspaceMembers.AsNoTracking().SingleOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId);

        private static string? CleanName(string? name)
        {
            var trimmed = name?.Trim();
            return string.IsNullOrEmpty(trimmed) || trimmed.Length > MeetingSeries.MaxNameLength ? null : trimmed;
        }

        public async Task<MeetingSeries?> FindAsync(string? userId, int seriesId)
        {
            var series = await _db.MeetingSeries.AsNoTracking().SingleOrDefaultAsync(s => s.Id == seriesId);
            return series != null && await MembershipAsync(userId, series.WorkspaceId) != null ? series : null;
        }

        public async Task<List<SeriesSummary>> ListAsync(string? userId, int workspaceId)
        {
            if (await MembershipAsync(userId, workspaceId) == null) return new();

            var rows = await _db.MeetingSeries.AsNoTracking()
                .Where(s => s.WorkspaceId == workspaceId)
                .Select(s => new
                {
                    s.Id, s.Name, s.CreatedAtUtc,
                    Count = s.Meetings.Count,
                    Last = s.Meetings.Max(m => (DateTime?)m.CreatedAtUtc)
                })
                .ToListAsync();
            return rows.OrderByDescending(r => r.Last ?? r.CreatedAtUtc)
                .Select(r => new SeriesSummary(r.Id, r.Name, r.Count, r.Last)).ToList();
        }

        public async Task<MeetingSeries?> CreateAsync(string? userId, int workspaceId, string? name)
        {
            var clean = CleanName(name);
            if (clean == null || await MembershipAsync(userId, workspaceId) == null) return null;

            var series = new MeetingSeries { WorkspaceId = workspaceId, Name = clean, CreatedByUserId = userId };
            _db.MeetingSeries.Add(series);
            await _db.SaveChangesAsync();
            return series;
        }

        // Renaming and deleting: whoever opened the series, the founder or an admin.
        public async Task<bool> CanManageAsync(string? userId, MeetingSeries series)
        {
            var member = await MembershipAsync(userId, series.WorkspaceId);
            return member != null && (series.CreatedByUserId == userId || WorkspaceMember.RunsTeam(member.Role));
        }

        public async Task<SeriesResult> RenameAsync(string? userId, int seriesId, string? name)
        {
            var series = await _db.MeetingSeries.SingleOrDefaultAsync(s => s.Id == seriesId);
            if (series == null || await MembershipAsync(userId, series.WorkspaceId) == null) return SeriesResult.NotFound;
            if (!await CanManageAsync(userId, series)) return SeriesResult.Forbidden;
            var clean = CleanName(name);
            if (clean == null) return SeriesResult.Invalid;

            series.Name = clean;
            await _db.SaveChangesAsync();
            return SeriesResult.Ok;
        }

        // The meetings stay; only their link goes (the database does not cascade, see ApplicationDbContext).
        public async Task<SeriesResult> DeleteAsync(string? userId, int seriesId)
        {
            var series = await _db.MeetingSeries.SingleOrDefaultAsync(s => s.Id == seriesId);
            if (series == null || await MembershipAsync(userId, series.WorkspaceId) == null) return SeriesResult.NotFound;
            if (!await CanManageAsync(userId, series)) return SeriesResult.Forbidden;

            await using var tx = await _db.Database.BeginTransactionAsync();
            await _db.Meetings.Where(m => m.SeriesId == seriesId).ExecuteUpdateAsync(s => s.SetProperty(m => m.SeriesId, (int?)null));
            _db.MeetingSeries.Remove(series);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return SeriesResult.Ok;
        }

        // Linking or unlinking is content work like adding a topic: any member of the meeting's team, only into a series
        // of that same team. Written with a single UPDATE so a parallel edit of the meeting is not overwritten.
        public async Task<SeriesResult> SetMeetingSeriesAsync(string? userId, int meetingId, int? seriesId)
        {
            var workspaceId = await _db.Meetings.AsNoTracking().Where(m => m.Id == meetingId).Select(m => (int?)m.WorkspaceId).SingleOrDefaultAsync();
            if (workspaceId == null || await MembershipAsync(userId, workspaceId.Value) == null) return SeriesResult.NotFound;
            if (seriesId != null && !await BelongsToWorkspaceAsync(seriesId.Value, workspaceId.Value)) return SeriesResult.NotFound;

            await _db.Meetings.Where(m => m.Id == meetingId && m.WorkspaceId == workspaceId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.SeriesId, seriesId));
            return SeriesResult.Ok;
        }

        public Task<bool> BelongsToWorkspaceAsync(int seriesId, int workspaceId) =>
            _db.MeetingSeries.AnyAsync(s => s.Id == seriesId && s.WorkspaceId == workspaceId);

        // Zoom import path: the caller has already checked that the importing user may write into this team. Two imports
        // of the same Zoom meeting at once both end up in the one series the unique index lets through.
        public async Task<int> FindOrCreateForZoomAsync(int workspaceId, long zoomMeetingNumber, string topic, string? createdByUserId)
        {
            var key = ZoomMeetingKey(zoomMeetingNumber);
            var existing = await ZoomSeriesIdAsync(workspaceId, key);
            if (existing != null) return existing.Value;

            var name = topic.Trim();
            if (name.Length > MeetingSeries.MaxNameLength) name = name[..MeetingSeries.MaxNameLength].TrimEnd();
            if (name.Length == 0) name = "Zoom toplantısı";
            var series = new MeetingSeries { WorkspaceId = workspaceId, Name = name, ZoomMeetingKey = key, CreatedByUserId = createdByUserId };
            _db.MeetingSeries.Add(series);
            try
            {
                await _db.SaveChangesAsync();
                return series.Id;
            }
            catch (DbUpdateException)
            {
                _db.Entry(series).State = EntityState.Detached;
                return await ZoomSeriesIdAsync(workspaceId, key) ?? throw new InvalidOperationException("Zoom series could not be saved.");
            }
        }

        private Task<int?> ZoomSeriesIdAsync(int workspaceId, string key) =>
            _db.MeetingSeries.AsNoTracking().Where(s => s.WorkspaceId == workspaceId && s.ZoomMeetingKey == key)
                .Select(s => (int?)s.Id).SingleOrDefaultAsync();
    }
}
