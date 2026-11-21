using Hangfire;

namespace MeetingWeb.Services
{
    public static class CalendarSyncJobs
    {
        // Calendar sync (SPEC.md): only the task id goes to Hangfire; the job reads the current state when it runs. A queue
        // failure must not undo or fail the user's saved change, so it is logged and the caller carries on.
        public static void QueueCalendarSync(this IBackgroundJobClient jobs, int actionItemId, ILogger logger)
        {
            try { jobs.Enqueue<ICalendarSyncService>(JobQueues.Email, s => s.SyncAsync(actionItemId)); }
            catch (Exception ex) { logger.LogWarning("Takvim eşitleme işi kuyruğa alınamadı. Görev={ActionItemId}, HataTürü={ErrorType}", actionItemId, ex.GetType().Name); }
        }
    }
}
