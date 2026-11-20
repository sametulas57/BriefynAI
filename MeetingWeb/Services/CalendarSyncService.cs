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

        public async Task SyncAsync(int actionItemId)
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
                var task = await db.ActionItems.AsNoTracking()
                    .Select(a => new
                    {
                        a.Id,
                        a.Text,
                        a.AssigneeUserId,
                        a.DueDate,
                        a.CompletedAtUtc,
                        MeetingTitle = a.Meeting!.Title,
                        a.Meeting.CreatedAtUtc,
                        a.Meeting.WorkspaceId
                    })
                    .SingleOrDefaultAsync(a => a.Id == actionItemId);
                ApplicationUser? user = null;
                if (task?.AssigneeUserId != null && task.DueDate != null && task.CompletedAtUtc == null
                    && await db.WorkspaceMembers.AnyAsync(m => m.UserId == task.AssigneeUserId && m.WorkspaceId == task.WorkspaceId))
                    user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == task.AssigneeUserId
                        && u.DeletionRequestedAtUtc == null && u.Email != null && u.Email != "");
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
                if ((record == null || record.Status == CalendarInviteStatus.Cancelled)
                    && (!user.CalendarInvitesEnabled || task.DueDate < DisplayTime.TodayIn(now))) return;
                if (record == null)
                {
                    record = new CalendarInvite { ActionItemId = actionItemId, UserId = user.Id, Uid = Guid.NewGuid().ToString("N") + "@briefyn.ai" };
                    db.CalendarInvites.Add(record);
                }
                else
                {
                    if (record.Status == CalendarInviteStatus.Active && record.DueDate == task.DueDate && !record.SendPending) return;
                    if (record.Status == CalendarInviteStatus.Cancelled || !record.SendPending) record.Sequence++;
                }
                record.Status = CalendarInviteStatus.Active;
                record.DueDate = task.DueDate!.Value;
                record.SendPending = true;
                record.UpdatedAtUtc = now;
                await db.SaveChangesAsync();
                var url = string.IsNullOrWhiteSpace(appOptions.Value.PublicUrl) ? null : appOptions.Value.PublicUrl.TrimEnd('/') + "/MyActions";
                var description = $"Toplantı: {task.MeetingTitle} ({DisplayTime.Format(task.CreatedAtUtc, "d MMMM yyyy")})"
                    + (url == null ? "" : $"\nGörevlerinizi görmek için: {url}");
                var invite = new CalendarInviteData(record.Uid, record.Sequence, record.DueDate, task.Text, description, url,
                    emailSettings.Value.SenderName, emailSettings.Value.SenderEmail,
                    string.IsNullOrWhiteSpace(user.FullName) ? UserDisplay.UnknownName : user.FullName, user.Email!, now);
                await SendAsync(record, invite, CalendarMethod.Request);
                requests++;
                record.SendPending = false;
                await db.SaveChangesAsync();
            }
        }

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
