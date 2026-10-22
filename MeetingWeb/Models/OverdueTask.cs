namespace MeetingWeb.Models
{
    public record OverdueTask(string Text, string MeetingTitle, DateTime MeetingCreatedAtUtc, int DaysOverdue);
}
