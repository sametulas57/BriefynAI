namespace MeetingWeb.Services.ArchiveSearch
{
    // Letter in the passage id: T{meetingId}-K1 topic, -R decision, -B pending, -A task, -D transcript chunk.
    public enum ArchivePassageKind
    {
        Topic,
        Decision,
        Pending,
        Task,
        Transcript
    }
}
