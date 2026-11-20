namespace MeetingWeb.Models
{
    public class PasswordResetOptions
    {
        public int LinkLifetimeMinutes { get; set; } = 60;
    }
}
