namespace MeetingWeb.Services.ArchiveSearch;

public record AskRequest(string? Question, ArchiveScope Scope = ArchiveScope.Archive, int? TargetId = null,
    DateOnly? From = null, DateOnly? To = null);
