namespace MeetingWeb.Services.ArchiveSearch
{
    // Keyword search over one team's completed meetings for "Arşive soru sor" (PRD 2026-10-24 §2). The index lives only in
    // memory, holds one workspace's data and is rebuilt after any change to that data; membership is checked on every call.
    public interface IArchiveSearchService
    {
        // Empty result (no meetings, no passages) when the user is not a member of the workspace or the scope's
        // series/meeting belongs to another workspace.
        Task<ArchiveSearchResult> SearchAsync(string? userId, int workspaceId, ArchiveQuery query, CancellationToken cancellationToken = default);
    }
}
