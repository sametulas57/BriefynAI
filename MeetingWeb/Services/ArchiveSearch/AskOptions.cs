namespace MeetingWeb.Services.ArchiveSearch;

public class AskOptions
{
    public int QuestionsPerHour { get; set; } = 30;
    public int MaxPassages { get; set; } = 12;
    public int ContextCharacters { get; set; } = 14000;
    public int TimeoutSeconds { get; set; } = 90;
}
