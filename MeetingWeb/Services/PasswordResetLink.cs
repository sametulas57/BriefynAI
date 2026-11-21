using System.Net;

namespace MeetingWeb.Services
{
    public static class PasswordResetLink
    {
        public static bool IsValidPublicUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        public static string? BaseUrl(string? publicUrl, string scheme, HostString host)
        {
            if (!string.IsNullOrWhiteSpace(publicUrl))
            {
                return IsValidPublicUrl(publicUrl)
                    ? publicUrl.TrimEnd('/')
                    : null;
            }

            // The request's Host header is never trusted for a public link because AllowedHosts is "*";
            // it is used only when it is loopback (ADR-0016).
            var hostname = host.Host;
            var isLoopback = string.Equals(hostname, "localhost", StringComparison.OrdinalIgnoreCase) ||
                (IPAddress.TryParse(hostname.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
            return isLoopback ? $"{scheme}://{host}" : null;
        }

        public static string ResetUrl(string baseUrl, string userId, string encodedCode) =>
            $"{baseUrl}/Auth/ResetPassword?userId={Uri.EscapeDataString(userId)}&code={encodedCode}";
    }
}
