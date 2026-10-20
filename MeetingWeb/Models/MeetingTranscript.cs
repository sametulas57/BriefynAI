namespace MeetingWeb.Models
{
    // Full speech-to-text output of a meeting (1:1). Encrypted at rest; lets a retry regenerate only the summary (ADR-0002).
    public class MeetingTranscript
    {
        public int MeetingId { get; set; }
        public Meeting? Meeting { get; set; }

        public string Text { get; set; } = string.Empty;
    }
}
