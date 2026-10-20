namespace MeetingWeb.Models
{
    // Who produced a topic, decision or task. Stored as a number, so existing values must not change.
    public enum EntrySource
    {
        Ai = 1,
        Manual = 2
    }
}
