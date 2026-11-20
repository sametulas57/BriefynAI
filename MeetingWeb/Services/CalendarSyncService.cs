using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Services
{
    public interface ICalendarSyncService
    {
        // Brings the calendar of everyone who has or had this task in line with its current state (SPEC.md). Running it
        // twice sends nothing the second time. Only the task id goes through Hangfire (ADR-0009).
        Task SyncAsync(int actionItemId);
        // Daily safety net (plan decision 6): re-syncs every task with a live calendar entry, so a cancellation missed by a rare
        // path (a deleted team, a failed queue) still goes out. Running it twice sends nothing the second time.
        Task SweepAsync();
        // Uses the same target eligibility as sync, including current team membership and account state.
        Task<bool> IsOwnDatedTaskAsync(int actionItemId, string userId);
        // Explicit request: sends again even when unchanged or automatic invitations are disabled.
        Task ResendAsync(int actionItemId);
        // No email; returns a PUBLISH file sharing the emailed invitation's stable UID.
        Task<string?> BuildDownloadAsync(int actionItemId, string userId);
        // Tasks of a meeting whose calendar entry is live (Active, or a send still pending). Call before deleting the meeting:
        // afterwards the tasks are gone and only these ids lead the sync to the stored invites.
        Task<IReadOnlyList<int>> InvitedTaskIdsForMeetingAsync(int meetingId);
        // Tasks in the workspace's meetings for which this person has a live calendar entry.
        Task<IReadOnlyList<int>> InvitedTaskIdsForMemberAsync(int workspaceId, string userId);
    }

    public class CalendarSyncService(
        ApplicationDbContext db,
        IEmailService email,
        TimeProvider time,
        IOptions<EmailSettings> emailSettings,
        IOptions<AppOptions> appOptions,
        ILogger<CalendarSyncService> logger) : ICalendarSyncService
    {
        private static readonly TimeSpan SendClaim = TimeSpan.FromMinutes(10);

        public Task SyncAsync(int actionItemId) => SyncCoreAsync(actionItemId, force: false);

        public async Task SweepAsync()
        {
            var ids = await db.CalendarInvites.AsNoTracking()
                .Where(i => i.Status == CalendarInviteStatus.Active || i.SendPending)
                .Select(i => i.ActionItemId).Distinct().ToListAsync();
            var failed = 0;
            foreach (var id in ids)
            {
                try { await SyncAsync(id); }
                catch (Exception ex)
                {
                    db.ChangeTracker.Clear();
                    failed++;
                    logger.LogError("Takvim süpürmesinde bir görev eşitlenemedi. Görev={ActionItemId}, HataTürü={ErrorType}", id, ex.GetType().Name);
                }
            }
            logger.LogInformation("Takvim süpürmesi tamamlandı. Görev={Tasks}, Hata={Failed}", ids.Count, failed);
        }

        public Task ResendAsync(int actionItemId) => SyncCoreAsync(actionItemId, force: true);

        public async Task<IReadOnlyList<int>> InvitedTaskIdsForMeetingAsync(int meetingId) =>
            await db.CalendarInvites.AsNoTracking()
                .Where(i => (i.Status == CalendarInviteStatus.Active || i.SendPending)
                    && db.ActionItems.Any(a => a.Id == i.ActionItemId && a.MeetingId == meetingId))
                .Select(i => i.ActionItemId).Distinct().ToListAsync();

        public async Task<IReadOnlyList<int>> InvitedTaskIdsForMemberAsync(int workspaceId, string userId) =>
            await db.CalendarInvites.AsNoTracking()
                .Where(i => i.UserId == userId && (i.Status == CalendarInviteStatus.Active || i.SendPending)
                    && db.ActionItems.Any(a => a.Id == i.ActionItemId && a.Meeting!.WorkspaceId == workspaceId))
                .Select(i => i.ActionItemId).Distinct().ToListAsync();

        public async Task<bool> IsOwnDatedTaskAsync(int actionItemId, string userId)
        {
            var task = await ReadTaskAsync(actionItemId);
            return task?.AssigneeUserId == userId && await TargetUserAsync(task) != null;
        }

        public async Task<string?> BuildDownloadAsync(int actionItemId, string userId)
        {
            for (var attempt = 0; ; attempt++)
            {
                var task = await ReadTaskAsync(actionItemId);
                if (task?.AssigneeUserId != userId) return null;
                var user = await TargetUserAsync(task);
                if (task == null || user == null) return null;
                var record = await db.CalendarInvites.SingleOrDefaultAsync(i => i.ActionItemId == actionItemId && i.UserId == userId);
                var now = time.GetUtcNow().UtcDateTime;
                try
                {
                    if (record == null)
                    {
                        record = new CalendarInvite
                        {
                            ActionItemId = actionItemId,
                            UserId = userId,
                            Uid = NewUid(),
                            Sequence = 0,
                            Status = CalendarInviteStatus.Active,
                            SendPending = false,
                            DueDate = task.DueDate!.Value,
                            UpdatedAtUtc = now
                        };
                        db.CalendarInvites.Add(record);
                    }
                    if (!InFlight(record, now))
                    {
                        // A re-added entry must outrank the cancellation the person may have received.
                        if (record.Status == CalendarInviteStatus.Cancelled) record.Sequence++;
                        record.Status = CalendarInviteStatus.Active;
                        record.DueDate = task.DueDate!.Value;
                        await db.SaveChangesAsync();
                    }
                    return CalendarInviteBuilder.Build(BuildRequestData(task, user, record, now), CalendarMethod.Publish);
                }
                catch (DbUpdateException) when (attempt == 0) { db.ChangeTracker.Clear(); }
            }
        }

        private async Task SyncCoreAsync(int actionItemId, bool force)
        {
            var requests = 0;
            var cancels = 0;
            try
            {
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await SyncOnceAsync();
                        return;
                    }
                    catch (DbUpdateConcurrencyException) when (attempt < 3) { db.ChangeTracker.Clear(); }
                    catch (DbUpdateException) when (attempt < 3) { db.ChangeTracker.Clear(); }
                }
            }
            finally
            {
                if (requests + cancels > 0)
                    logger.LogInformation("Takvim eşitlendi. Görev={ActionItemId}, Davet={Requests}, İptal={Cancels}", actionItemId, requests, cancels);
            }

            async Task SyncOnceAsync()
            {
                var task = await ReadTaskAsync(actionItemId);
                var user = await TargetUserAsync(task);
                var records = await db.CalendarInvites.Where(i => i.ActionItemId == actionItemId).ToListAsync();
                var now = time.GetUtcNow().UtcDateTime;
                foreach (var previous in records.Where(i => i.UserId != user?.Id))
                {
                    if (InFlight(previous, now)) continue;
                    // An active record still pending (its invite send failed) is cancelled too: the invite may have arrived.
                    var needsCancel = previous.Status == CalendarInviteStatus.Active;
                    if (needsCancel || (previous.Status == CalendarInviteStatus.Cancelled && previous.SendPending))
                    {
                        previous.Status = CalendarInviteStatus.Cancelled;
                        if (needsCancel) previous.Sequence++;
                        previous.SendPending = true;
                        previous.UpdatedAtUtc = now;
                        await db.SaveChangesAsync();
                        var attendee = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == previous.UserId);
                        if (!string.IsNullOrWhiteSpace(attendee?.Email))
                        {
                            var cancellation = new CalendarInviteData(previous.Uid, previous.Sequence, previous.DueDate, "", "", null,
                                emailSettings.Value.SenderName, emailSettings.Value.SenderEmail,
                                string.IsNullOrWhiteSpace(attendee.FullName) ? UserDisplay.UnknownName : attendee.FullName, attendee.Email, now);
                            await SendAsync(previous, cancellation, CalendarMethod.Cancel);
                            cancels++;
                        }
                        previous.SendPending = false;
                        await db.SaveChangesAsync();
                    }
                    if (task == null && previous.Status == CalendarInviteStatus.Cancelled && !previous.SendPending)
                    {
                        db.CalendarInvites.Remove(previous);
                        await db.SaveChangesAsync();
                    }
                }
                if (task == null || user == null)
                {
                    return;
                }
                var record = records.FirstOrDefault(i => i.UserId == user.Id);
                if (record != null && InFlight(record, now)) return;
                if (!force && (record == null || record.Status == CalendarInviteStatus.Cancelled)
                    && (!user.CalendarInvitesEnabled || task.DueDate < DisplayTime.TodayIn(now))) return;
                if (record == null)
                {
                    record = new CalendarInvite { ActionItemId = actionItemId, UserId = user.Id, Uid = NewUid() };
                    db.CalendarInvites.Add(record);
                }
                else
                {
                    if (!force && record.Status == CalendarInviteStatus.Active && record.DueDate == task.DueDate && !record.SendPending) return;
                    // A cancelled entry always moves past its cancellation; otherwise only a completed send moves on.
                    if (record.Status == CalendarInviteStatus.Cancelled || !record.SendPending) record.Sequence++;
                }
                record.Status = CalendarInviteStatus.Active;
                record.DueDate = task.DueDate!.Value;
                record.SendPending = true;
                record.UpdatedAtUtc = now;
                await db.SaveChangesAsync();
                var invite = BuildRequestData(task, user, record, now) with { Resent = force };
                await SendAsync(record, invite, CalendarMethod.Request);
                requests++;
                record.SendPending = false;
                await db.SaveChangesAsync();
            }
        }

        private sealed record CalendarTask(int Id, string Text, string? AssigneeUserId, DateOnly? DueDate,
            DateTime? CompletedAtUtc, string MeetingTitle, DateTime CreatedAtUtc, int WorkspaceId);

        private Task<CalendarTask?> ReadTaskAsync(int actionItemId) => db.ActionItems.AsNoTracking()
            .Where(a => a.Id == actionItemId)
            .Select(a => new CalendarTask(a.Id, a.Text, a.AssigneeUserId, a.DueDate, a.CompletedAtUtc,
                a.Meeting!.Title, a.Meeting.CreatedAtUtc, a.Meeting.WorkspaceId))
            .SingleOrDefaultAsync();

        private async Task<ApplicationUser?> TargetUserAsync(CalendarTask? task)
        {
            if (task?.AssigneeUserId == null || task.DueDate == null || task.CompletedAtUtc != null
                || !await db.WorkspaceMembers.AnyAsync(m => m.UserId == task.AssigneeUserId && m.WorkspaceId == task.WorkspaceId))
                return null;
            return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == task.AssigneeUserId
                && u.DeletionRequestedAtUtc == null && u.Email != null && u.Email != "");
        }

        private CalendarInviteData BuildRequestData(CalendarTask task, ApplicationUser user, CalendarInvite record, DateTime now)
        {
            var url = string.IsNullOrWhiteSpace(appOptions.Value.PublicUrl) ? null : appOptions.Value.PublicUrl.TrimEnd('/') + "/MyActions";
            var description = $"Toplantı: {task.MeetingTitle} ({DisplayTime.Format(task.CreatedAtUtc, "d MMMM yyyy")})"
                + (url == null ? "" : $"\nGörevlerinizi görmek için: {url}");
            return new CalendarInviteData(record.Uid, record.Sequence, task.DueDate!.Value, task.Text, description, url,
                emailSettings.Value.SenderName, emailSettings.Value.SenderEmail,
                string.IsNullOrWhiteSpace(user.FullName) ? UserDisplay.UnknownName : user.FullName, user.Email!, now);
        }

        private static string NewUid() => Guid.NewGuid().ToString("N") + "@briefyn.ai";

        private static bool InFlight(CalendarInvite record, DateTime now) => record.SendPending && record.UpdatedAtUtc > now - SendClaim;

        private async Task SendAsync(CalendarInvite record, CalendarInviteData invite, CalendarMethod method)
        {
            try
            {
                await email.SendCalendarInviteAsync(invite, method);
            }
            catch (Exception ex)
            {
                record.UpdatedAtUtc = DateTime.MinValue;
                await db.SaveChangesAsync();
                logger.LogError("Takvim daveti gönderilemedi. Görev={ActionItemId}, HataTürü={ErrorType}", record.ActionItemId, ex.GetType().Name);
                throw;
            }
        }
    }
}
