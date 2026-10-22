using MeetingWeb.Models;

namespace MeetingWeb.Services
{
    // Abstraction for the email dispatching module to support Dependency Injection.
    public interface IEmailService
    {
        // ADR-0009: the recipient is passed encrypted (EmailService.ProtectRecipient), never as plain text in Hangfire.
        Task SendMeetingSummaryAsync(int meetingId, string protectedRecipient);
        Task SendSeriesAgendaAsync(int seriesId, string protectedRecipient);
        Task SendOverdueReminderAsync(string toEmail, string fullName, IReadOnlyList<OverdueTask> tasks);
    }
}
