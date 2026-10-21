using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace MeetingWeb.Tests.Infrastructure
{
    // Stands in for zoom.us and api.zoom.us. Tests register a response per "METHOD path" and read back what was sent.
    public class StubZoomHandler : HttpMessageHandler
    {
        public record Sent(HttpMethod Method, Uri Uri, string? Authorization, string Body);

        private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();
        public ConcurrentQueue<Sent> Requests { get; } = new();

        public const string AccessToken = "zoom-access-token-PLAIN-1";
        public const string RefreshToken = "zoom-refresh-token-PLAIN-1";
        public const string NewAccessToken = "zoom-access-token-PLAIN-2";
        public const string NewRefreshToken = "zoom-refresh-token-PLAIN-2";
        public const string Scopes = "cloud_recording:read:list_user_recordings cloud_recording:read:list_recording_files user:read:user";

        public StubZoomHandler()
        {
            Json("POST", "/oauth/token", TokenJson(AccessToken, RefreshToken));
            Json("GET", "/v2/users/me", """{"id":"zoom-user-1","email":"ulas@zoom.test","first_name":"Ulaş","last_name":"Karasu"}""");
            Json("POST", "/oauth/revoke", """{"status":"success"}""");
        }

        public static string TokenJson(string access, string refresh, string scope = Scopes) =>
            $$"""{"access_token":"{{access}}","token_type":"bearer","refresh_token":"{{refresh}}","expires_in":3600,"scope":"{{scope}}"}""";

        public void On(string method, string path, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            _routes[method + " " + path] = respond;

        public void Json(string method, string path, string body, HttpStatusCode status = HttpStatusCode.OK) =>
            On(method, path, _ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

        public IEnumerable<Sent> To(string method, string path) =>
            Requests.Where(r => r.Method.Method == method && r.Uri.AbsolutePath == path);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Enqueue(new Sent(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
            return _routes.TryGetValue(request.Method.Method + " " + request.RequestUri!.AbsolutePath, out var respond)
                ? respond(request)
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"code":3301,"message":"not found"}""") };
        }
    }
}
