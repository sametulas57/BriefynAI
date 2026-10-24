namespace MeetingWeb.Services.ArchiveSearch
{
    // TargetId is the series id for ArchiveScope.Series and the meeting id for ArchiveScope.Meeting.
    // From/To are inclusive meeting dates in Turkish time.
    public record ArchiveQuery(string Question, ArchiveScope Scope = ArchiveScope.Archive, int? TargetId = null,
        DateOnly? From = null, DateOnly? To = null, int Top = 8);
}
