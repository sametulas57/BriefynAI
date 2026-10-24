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
            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }
}
