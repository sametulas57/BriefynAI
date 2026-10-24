using System.Net;
using System.Text;

namespace MeetingWeb.Tests.Infrastructure
{
    // Stands in for the Python AI service; tests choose whether it succeeds or fails.
    public class StubPythonApiHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string? LastApiKey { get; private set; }
        public string? LastPath { get; private set; }
        public string? LastBody { get; private set; }
        public string? LastResponseBody { get; private set; }
        public Uri? ForwardTo { get; set; }
        public string? ForwardApiKey { get; set; }
        public HttpMessageHandler? ForwardHandler { get; set; }
        public int CallCount { get; private set; }

        // Runs while the "AI" is working, e.g. to simulate a user editing the meeting at the same time.
        public Action? BeforeRespond { get; set; }

        public string Body { get; set; } = """
            {"success": true, "transkript": "test",
             "summary": {"toplanti_konusu": "Konu", "gorusulen_konular": ["a"], "alinan_kararlar": ["b"], "aksiyon_maddeleri": ["c"]}}
            """;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastApiKey = request.Headers.TryGetValues("X-Api-Key", out var values) ? values.Single() : null;
            LastPath = request.RequestUri!.AbsolutePath.TrimStart('/');
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            BeforeRespond?.Invoke();
            if (ForwardTo != null)
            {
                var target = new Uri(ForwardTo, request.RequestUri!.PathAndQuery);
                using var forwarded = new HttpRequestMessage(request.Method, target);
                if (request.Content != null) forwarded.Content = new StringContent(LastBody!, Encoding.UTF8, request.Content.Headers.ContentType?.MediaType ?? "application/json");
                foreach (var header in request.Headers)
                    if (!header.Key.Equals("X-Api-Key", StringComparison.OrdinalIgnoreCase)) forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
                if (ForwardApiKey != null) forwarded.Headers.TryAddWithoutValidation("X-Api-Key", ForwardApiKey);
                using var client = ForwardHandler == null ? new HttpClient() : new HttpClient(ForwardHandler, disposeHandler: false);
                using var response = await client.SendAsync(forwarded, cancellationToken);
                LastResponseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(response.StatusCode) { Content = new StringContent(LastResponseBody, Encoding.UTF8, response.Content.Headers.ContentType?.MediaType ?? "application/json") };
            }
            LastResponseBody = Body;
            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }
}
