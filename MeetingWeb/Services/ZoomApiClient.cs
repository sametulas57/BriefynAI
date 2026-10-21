using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Services
{
    // Zoom no longer accepts the stored grant (expired, revoked, already used); the connection must be made again.
    public class ZoomGrantRejectedException : Exception
    {
        public ZoomGrantRejectedException() : base("Zoom rejected the grant.") { }
    }

    // Zoom could not be reached or answered with an error. Carries only the status code: never a token or a body.
    public class ZoomApiException : Exception
    {
        public HttpStatusCode? Status { get; }
        public ZoomApiException(HttpStatusCode? status) : base($"Zoom request failed ({(status == null ? "no answer" : (int)status)}).") => Status = status;
    }

    public record ZoomTokens(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc, string Scope);
    public record ZoomUser(string Id, string Email);
    public record ZoomRecordingFile(string FileType, string DownloadUrl, long FileSize);
    public record ZoomRecording(string Uuid, string Topic, DateTime StartTimeUtc, int DurationMinutes, IReadOnlyList<ZoomRecordingFile> Files)
    {
        // A recording stopped and started again during the meeting has one audio file per part.
        public IReadOnlyList<ZoomRecordingFile> AudioFiles => Files.Where(f => f.FileType == "M4A" && !string.IsNullOrEmpty(f.DownloadUrl)).ToList();
    }

    // Raw HTTP calls to Zoom's OAuth and REST API; knows nothing about the database. Only read endpoints are used.
    public class ZoomApiClient
    {
        public const string HttpClientName = "Zoom";
        private const int MaxRedirects = 5;
        private const int MaxListPages = 10;
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ZoomOptions _options;

        public ZoomApiClient(IHttpClientFactory httpClientFactory, IOptions<ZoomOptions> options)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
        }

        public string AuthorizeUrl(string state) =>
            $"{Auth("oauth/authorize")}?response_type=code&client_id={Uri.EscapeDataString(_options.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(_options.RedirectUri)}&state={Uri.EscapeDataString(state)}";

        public Task<ZoomTokens> ExchangeCodeAsync(string code) => TokenAsync(new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri
        });

        public Task<ZoomTokens> RefreshAsync(string refreshToken) => TokenAsync(new()
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });

        public async Task RevokeAsync(string accessToken)
        {
            // The token travels in the form body, never in the address (addresses can end up in logs).
            using var request = new HttpRequestMessage(HttpMethod.Post, Auth("oauth/revoke"))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = accessToken })
            };
            request.Headers.Authorization = Basic();
            using var response = await SendAsync(request);
            EnsureSuccess(response);
        }

        public async Task<ZoomUser> GetMeAsync(string accessToken)
        {
            var me = await GetJsonAsync<UserDto>(accessToken, "users/me");
            if (me == null || string.IsNullOrEmpty(me.Id)) throw new ZoomApiException(null);
            return new ZoomUser(me.Id, me.Email ?? "");
        }

        // Zoom allows at most one month per request; dates are UTC days.
        public async Task<IReadOnlyList<ZoomRecording>> ListRecordingsAsync(string accessToken, DateTime fromUtc, DateTime toUtc)
        {
            var meetings = new List<MeetingDto>();
            string? next = null;
            for (var page = 0; page < MaxListPages; page++)
            {
                var path = $"users/me/recordings?from={fromUtc:yyyy-MM-dd}&to={toUtc:yyyy-MM-dd}&page_size=300";
                if (!string.IsNullOrEmpty(next)) path += "&next_page_token=" + Uri.EscapeDataString(next);
                var list = await GetJsonAsync<ListDto>(accessToken, path);
                meetings.AddRange(list?.Meetings ?? new());
                next = list?.NextPageToken;
                if (string.IsNullOrEmpty(next)) break;
            }
            return meetings.Select(ToRecording).OrderByDescending(r => r.StartTimeUtc).ToList();
        }

        // Null when Zoom does not know the recording (or it is not this user's).
        public async Task<ZoomRecording?> GetRecordingAsync(string accessToken, string uuid)
        {
            // Zoom's rule: a UUID that starts with "/" or contains "//" must be encoded twice.
            var id = Uri.EscapeDataString(uuid);
            if (uuid.StartsWith('/') || uuid.Contains("//")) id = Uri.EscapeDataString(id);
            try
            {
                var meeting = await GetJsonAsync<MeetingDto>(accessToken, $"meetings/{id}/recordings");
                return meeting == null ? null : ToRecording(meeting);
            }
            catch (ZoomApiException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                return null;
            }
        }

        // Downloads a recording file. Zoom's address redirects to a signed address; the bearer token is sent only to
        // zoom.us hosts, and the first address must be one.
        public async Task<T> DownloadAsync<T>(string accessToken, string downloadUrl, Func<Stream, Task<T>> read)
        {
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) || !IsZoomHost(uri)) throw new ZoomApiException(null);

            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                if (IsZoomHost(uri)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
                {
                    uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                    if (uri.Scheme != Uri.UriSchemeHttps) throw new ZoomApiException(null);
                    continue;
                }

                // Only Zoom's own answer says the grant is gone; the signed address refusing is just a failed download.
                if (response.StatusCode == HttpStatusCode.Unauthorized && IsZoomHost(uri)) throw new ZoomGrantRejectedException();
                EnsureSuccess(response);
                try
                {
                    // The client's timeout stops at the headers; closing the response ends a read that stalls.
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.DownloadTimeoutSeconds));
                    using var stop = timeout.Token.Register(response.Dispose);
                    await using var stream = await response.Content.ReadAsStreamAsync();
                    return await read(stream);
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException or ObjectDisposedException or OperationCanceledException)
                {
                    // The connection dropped or stalled in the middle of the file.
                    throw new ZoomApiException(null);
                }
            }
            throw new ZoomApiException(null);
        }

        private static bool IsZoomHost(Uri uri) =>
            uri.Scheme == Uri.UriSchemeHttps && (uri.Host == "zoom.us" || uri.Host.EndsWith(".zoom.us", StringComparison.OrdinalIgnoreCase));

        private async Task<ZoomTokens> TokenAsync(Dictionary<string, string> form)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Auth("oauth/token")) { Content = new FormUrlEncodedContent(form) };
            request.Headers.Authorization = Basic();
            using var response = await SendAsync(request, timeout: TimeSpan.FromSeconds(_options.TokenTimeoutSeconds));

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized && await IsInvalidGrantAsync(response))
                throw new ZoomGrantRejectedException();
            EnsureSuccess(response);

            var tokens = await ReadAsync<TokenDto>(response);
            if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken) || string.IsNullOrEmpty(tokens.RefreshToken))
                throw new ZoomApiException(response.StatusCode);
            var lifetime = tokens.ExpiresIn > 0 ? tokens.ExpiresIn : 3600;
            return new ZoomTokens(tokens.AccessToken, tokens.RefreshToken, DateTime.UtcNow.AddSeconds(lifetime), tokens.Scope ?? "");
        }

        private static async Task<bool> IsInvalidGrantAsync(HttpResponseMessage response)
        {
            try
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                // Zoom answers an unusable refresh token with invalid_grant, or with invalid_request "Invalid Token!".
                var error = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
                var reason = doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null;
                return error == "invalid_grant" || (error == "invalid_request" && reason == "Invalid Token!");
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private async Task<T?> GetJsonAsync<T>(string accessToken, string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(_options.ApiBaseUrl), path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new ZoomGrantRejectedException();
            EnsureSuccess(response);
            return await ReadAsync<T>(response);
        }

        private static async Task<T?> ReadAsync<T>(HttpResponseMessage response)
        {
            try
            {
                return await response.Content.ReadFromJsonAsync<T>(Json);
            }
            catch (JsonException)
            {
                throw new ZoomApiException(response.StatusCode);
            }
        }

        // Network failures and timeouts become ZoomApiException; their messages never contain tokens.
        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead, TimeSpan? timeout = null)
        {
            using var limit = new CancellationTokenSource(timeout ?? Timeout.InfiniteTimeSpan);
            try
            {
                return await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, completion, limit.Token).WaitAsync(limit.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                throw new ZoomApiException(null);
            }
        }

        private static void EnsureSuccess(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode) throw new ZoomApiException(response.StatusCode);
        }

        private Uri Auth(string path) => new(new Uri(_options.AuthBaseUrl), path);

        private AuthenticationHeaderValue Basic() =>
            new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));

        private static ZoomRecording ToRecording(MeetingDto m) => new(
            m.Uuid ?? "",
            m.Topic ?? "",
            m.StartTime?.ToUniversalTime() ?? DateTime.MinValue,
            m.Duration,
            (m.RecordingFiles ?? new()).Select(f => new ZoomRecordingFile(f.FileType ?? "", f.DownloadUrl ?? "", f.FileSize)).ToList());

        private sealed class TokenDto
        {
            public string? AccessToken { get; set; }
            public string? RefreshToken { get; set; }
            public int ExpiresIn { get; set; }
            public string? Scope { get; set; }
        }

        private sealed class UserDto
        {
            public string? Id { get; set; }
            public string? Email { get; set; }
        }

        private sealed class ListDto
        {
            public string? NextPageToken { get; set; }
            public List<MeetingDto>? Meetings { get; set; }
        }

        private sealed class MeetingDto
        {
            public string? Uuid { get; set; }
            public string? Topic { get; set; }
            public DateTime? StartTime { get; set; }
            public int Duration { get; set; }
            public List<FileDto>? RecordingFiles { get; set; }
        }

        private sealed class FileDto
        {
            public string? FileType { get; set; }
            public string? DownloadUrl { get; set; }
            public long FileSize { get; set; }
        }
    }
}
