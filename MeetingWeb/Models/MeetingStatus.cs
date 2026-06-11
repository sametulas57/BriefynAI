namespace MeetingWeb.Models
{
    // Represents the lifecycle state of an asynchronous AI processing job.
    public enum MeetingStatus
    {
        Processing = 0, // Job is enqueued or currently being executed by Hangfire.
        Completed = 1,  // AI analysis finished successfully and mapped to the database.
        Failed = 2      // Job encountered an exception, timeout, or external API failure.
    }
}