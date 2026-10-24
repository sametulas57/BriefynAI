namespace MeetingWeb.Services.ArchiveSearch
{
    // MeetingsSearched is the number of completed meetings inside the scope and date range ("N toplantıya bakıldı").
    public record ArchiveSearchResult(int MeetingsSearched, IReadOnlyList<ArchivePassage> Passages)
    {
        public static readonly ArchiveSearchResult Empty = new(0, Array.Empty<ArchivePassage>());
    }
}
