namespace MeetingWeb.Services
{
    // Abstraction for the email dispatching module to support Dependency Injection.
    public interface IEmailService
    {
        Task SendMeetingSummaryAsync(int meetingId, string toEmail);
    }
}