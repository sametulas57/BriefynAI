namespace MeetingWeb.Services;

public static class JobQueues
{
    // Emails a person waits for run on their own worker so a long analysis cannot delay them.
    public const string Email = "eposta";
}
