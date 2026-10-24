namespace MeetingWeb.Services.ArchiveSearch;

public record AskSource(string PassageId, int MeetingId, string MeetingTitle, DateOnly MeetingDate,
    ArchivePassageKind Kind, string Quote);
