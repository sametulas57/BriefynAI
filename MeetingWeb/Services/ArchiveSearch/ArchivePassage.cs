namespace MeetingWeb.Services.ArchiveSearch
{
    // One searchable piece of a meeting. Text is the piece itself (a task includes its assignee, due date and state);
    // the meeting title and date are indexed with it but kept in their own fields.
    public record ArchivePassage(string Id, int MeetingId, string MeetingTitle, DateOnly MeetingDate,
        ArchivePassageKind Kind, string Text, double Score);
}
