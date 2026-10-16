using System.Net;
using System.Text;

namespace MeetingWeb.Tests.Infrastructure
{
    // Stands in for the Python AI service; tests choose whether it succeeds or fails.
    public class StubPythonApiHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string Body { get; set; } = """
            {"success": true, "transkript": "test",
             "summary": {"toplanti_konusu": "Konu", "gorusulen_konular": ["a"], "alinan_kararlar": ["b"], "aksiyon_maddeleri": ["c"]}}
            """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
    }
}
