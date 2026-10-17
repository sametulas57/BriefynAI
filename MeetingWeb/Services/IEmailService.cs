namespace MeetingWeb.Services
{
    // Abstraction for the email dispatching module to support Dependency Injection.
    public interface IEmailService
    {
        Task SendMeetingSummaryAsync(int meetingId, string toEmail);

        // Aksiyon takibi: reminder for one task; reminderKey is "assigneeId|yyyy-MM-dd" as claimed by the scan.
        Task SendActionReminderAsync(int actionItemId, string reminderKey);
    }
}