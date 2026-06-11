namespace MeetingWeb.Models
{
    // Strongly-typed representation of the "EmailSettings" section in appsettings.json.
    // Utilized via the Options Pattern to inject configuration cleanly into services.
    public class EmailSettings
    {
        public string SmtpServer { get; set; } = string.Empty;
        public int Port { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public string SenderEmail { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}