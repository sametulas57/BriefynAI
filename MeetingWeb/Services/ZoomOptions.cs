namespace MeetingWeb.Services
{
    // Zoom app settings (ADR-0011). Client ID / Secret come only from .env (ZOOM_CLIENT_ID / ZOOM_CLIENT_SECRET);
    // without them the Zoom feature is off.
    public class ZoomOptions
    {
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public string RedirectUri { get; set; } = "";
        public string AuthBaseUrl { get; set; } = "https://zoom.us/";
        public string ApiBaseUrl { get; set; } = "https://api.zoom.us/v2/";
        // Upper limit for one token request (connect, refresh); a refresh mark lives twice as long.
        public int TokenTimeoutSeconds { get; set; } = 30;
        // Upper limit for downloading one recording; a download that stalls is cut off after this.
        public int DownloadTimeoutSeconds { get; set; } = 1800;

        public bool IsEnabled => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);
    }
}
