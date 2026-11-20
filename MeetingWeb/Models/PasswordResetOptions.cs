namespace MeetingWeb.Models
{
    public class PasswordResetOptions
    {
        public int RequestsPerIpPer15Minutes { get; set; } = 5;
        public int ResetsPerIpPer15Minutes { get; set; } = 10;
        public int EmailsPerAccountPerHour { get; set; } = 3;
        public int LinkLifetimeMinutes { get; set; } = 60;
    }
}
