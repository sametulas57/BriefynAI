using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics;
using MeetingWeb.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Services
{
    // ADR-0012: Zoom's notifications. Public, without login or cookies; Zoom's signature over the raw body is the only
    // gate. Answers within Zoom's three seconds: nothing but the database and the job queue is touched here.
    public class ZoomWebhookHandler
    {
        public const string Path = "/Zoom/Webhook";
        public const int MaxBodyBytes = 256 * 1024;
        private const long MaxClockSkewSeconds = 5 * 60;

        private readonly ZoomOptions _options;
        private readonly ApplicationDbContext _db;
        private readonly IBackgroundJobClient _jobs;
        private readonly ILogger<ZoomWebhookHandler> _logger;

        public ZoomWebhookHandler(IOptions<ZoomOptions> options, ApplicationDbContext db, IBackgroundJobClient jobs, ILogger<ZoomWebhookHandler> logger)
        {
            _options = options.Value;
            _db = db;
            _jobs = jobs;
            _logger = logger;
        }

        public async Task<IResult> HandleAsync(HttpRequest request)
        {
            SkipStatusPage(request.HttpContext);
            if (!_options.IsWebhookEnabled) return Results.NotFound();

            var body = await ReadBodyAsync(request);
            if (body == null) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            // 4xx: Zoom does not send it again.
            if (!IsSigned(request.Headers["x-zm-request-timestamp"], request.Headers["x-zm-signature"], body)) return Results.Unauthorized();

            JsonElement root;
            try
            {
                using var doc = JsonDocument.Parse(body);
                root = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return Results.BadRequest();
            }
            var payload = Child(root, "payload");
            // Only the event name: the body carries a download token and personal data.
            _logger.LogInformation("Zoom notification received: {Event}.", Text(root, "event"));

            switch (Text(root, "event"))
            {
                case "endpoint.url_validation":
                    var plainToken = Text(payload, "plainToken");
                    if (string.IsNullOrEmpty(plainToken)) return Results.BadRequest();
                    return Results.Json(new { plainToken, encryptedToken = Hmac(Encoding.UTF8.GetBytes(plainToken)) });
                case "recording.completed":
                    // Zoom's transcript is usually ready a few minutes later; its own event (below) starts the job sooner.
                    Queue(Child(payload, "object"), TimeSpan.FromMinutes(_options.TranscriptWaitMinutes));
                    return Results.Ok();
                case "recording.transcript_completed":
                    Queue(Child(payload, "object"), TimeSpan.Zero);
                    return Results.Ok();
                case "app_deauthorized":
                    await RemoveConnectionsAsync(payload);
                    return Results.Ok();
                default:
                    return Results.Ok();
            }
        }

        // Zoom reads only the status code; the friendly error page would be re-run as a POST and fail on antiforgery.
        public static void SkipStatusPage(HttpContext http)
        {
            if (http.Features.Get<IStatusCodePagesFeature>() is { } statusPages) statusPages.Enabled = false;
        }

        // Only Zoom's ids go to the job; download_token and everything else in the event is left behind.
        private void Queue(JsonElement recording, TimeSpan delay)
        {
            var hostId = Text(recording, "host_id");
            var uuid = Text(recording, "uuid");
            if (string.IsNullOrEmpty(hostId) || hostId.Length > 100 || string.IsNullOrEmpty(uuid) || uuid.Length > 200) return;
            if (delay > TimeSpan.Zero)
                _jobs.Schedule<IZoomService>(zoom => zoom.AutoImportAsync(hostId, uuid), delay);
            else
                _jobs.Enqueue<IZoomService>(zoom => zoom.AutoImportAsync(hostId, uuid));
        }

        // The app was removed in Zoom, so its tokens are already revoked there; the rows go too.
        private async Task RemoveConnectionsAsync(JsonElement payload)
        {
            var zoomUserId = Text(payload, "user_id");
            var clientId = Text(payload, "client_id");
            if (string.IsNullOrEmpty(zoomUserId) || (!string.IsNullOrEmpty(clientId) && clientId != _options.ClientId)) return;
            // A late notice must not remove a connection made again after the removal.
            var removedAt = DateTime.TryParse(Text(payload, "deauthorization_time"), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : DateTime.UtcNow;
            var removed = await _db.ZoomConnections.Where(z => z.ZoomUserId == zoomUserId && z.ConnectedAtUtc <= removedAt).ExecuteDeleteAsync();
            if (removed > 0) _logger.LogWarning("Zoom connection removed: the app was removed in Zoom ({Count}).", removed);
        }

        // v0=hex(HMAC-SHA256(secret, "v0:{timestamp}:{raw body}")), compared in constant time; an old or future
        // timestamp is refused so a captured request cannot be played again later.
        private bool IsSigned(string? timestamp, string? signature, byte[] body)
        {
            if (string.IsNullOrEmpty(signature) || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) return false;
            if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds) > MaxClockSkewSeconds) return false;
            var expected = "v0=" + Hmac([.. Encoding.UTF8.GetBytes($"v0:{timestamp}:"), .. body]);
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature));
        }

        private string Hmac(byte[] message) =>
            Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.WebhookSecret), message));

        // Null when the body is larger than MaxBodyBytes.
        private static async Task<byte[]?> ReadBodyAsync(HttpRequest request)
        {
            if (request.ContentLength > MaxBodyBytes) return null;
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await request.Body.ReadAsync(chunk)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }

        private static JsonElement Child(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child) ? child : default;

        private static string? Text(JsonElement element, string name) =>
            Child(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
    }
}
