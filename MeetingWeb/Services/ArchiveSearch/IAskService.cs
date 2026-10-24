namespace MeetingWeb.Services.ArchiveSearch;

public interface IAskService
{
    Task<AskResult> AskAsync(string? userId, int workspaceId, AskRequest request, CancellationToken ct = default);
}
