namespace MeetingWeb.Models
{
    // Kinds of summary lines kept in MeetingPoints. Stored as a number, so existing values must not change.
    public enum MeetingPointKind
    {
        Topic = 1,
        Decision = 2,
        // Discussed but not settled: postponed, or waiting for an answer or information.
        Pending = 3
    }
}
