namespace MeetingWeb.Models
{
    public class AppOptions
    {
        // Public address used for links in calendar invites; empty means no link (filled when the site goes public, Backlog 1).
        public string PublicUrl { get; set; } = "";
    }
}
