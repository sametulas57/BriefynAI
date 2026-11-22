using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingWeb.Services.Ai;

public sealed class GroqException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record TranscriptSegment(double Start, double End, string Text);

public sealed class GroqClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<GroqClient> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _now;
    private readonly int _maxAttempts;

    public GroqClient(HttpClient http, AiOptions options, ILogger<GroqClient> logger)
        : this(http, options.GroqApiKey, logger, Task.Delay, () => DateTimeOffset.UtcNow) { }

    public GroqClient(HttpClient http, string apiKey, ILogger<GroqClient> logger,
        Func<TimeSpan, CancellationToken, Task> delay, Func<DateTimeOffset> now, int maxAttempts = 5)
    {
        if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("GROQ_API_KEY is not set");
        _http = http;
        _apiKey = apiKey;
        _logger = logger;
        _delay = delay;
        _now = now;
        _maxAttempts = maxAttempts;
    }

    private async Task<HttpResponseMessage> PostAsync(string path, Func<HttpContent> content, CancellationToken ct)
    {
        HttpResponseMessage? last = null;
        // Free tier has per-minute limits: on 429 wait as long as Groq asks (capped), on 5xx back off briefly.
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content() };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            last = await _http.SendAsync(request, ct);
            if ((int)last.StatusCode == 429 && attempt < _maxAttempts)
            {
                var header = last.Headers.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null;
                var wait = RetryAfter.Seconds(header, _now());
                last.Dispose();
                _logger.LogWarning("Groq rate limit, waiting {Seconds:F0}s (attempt {Attempt})", wait.TotalSeconds, attempt);
                await _delay(wait, ct);
                continue;
            }
            if ((int)last.StatusCode >= 500 && attempt < _maxAttempts)
            {
                last.Dispose();
                await _delay(TimeSpan.FromSeconds(2 * attempt), ct);
                continue;
            }
            return last;
        }
        // A positive attempt count yields a response; keep the legacy zero-attempt return unchanged.
        return last!;
    }

    private async Task<JsonDocument> TranscriptionAsync(string audioPath, string model, string language, string prompt, string format, CancellationToken ct)
    {
        HttpContent Content()
        {
            var multipart = new MultipartFormDataContent();
            try
            {
                multipart.Add(new StringContent(model), "model");
                multipart.Add(new StringContent(language), "language");
                multipart.Add(new StringContent(prompt), "prompt");
                multipart.Add(new StringContent(format), "response_format");
                multipart.Add(new StringContent("0"), "temperature");
                var file = new StreamContent(File.OpenRead(audioPath));
                file.Headers.ContentType = new MediaTypeHeaderValue("audio/flac");
                multipart.Add(file, "file", Path.GetFileName(audioPath));
                return multipart;
            }
            catch
            {
                multipart.Dispose();
                throw;
            }
        }

        using var response = await PostAsync("audio/transcriptions", Content, ct);
        if ((int)response.StatusCode != 200) throw new GroqException($"transcription failed: HTTP {(int)response.StatusCode}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<string> TranscribeAsync(string audioPath, string model, string language, string prompt, CancellationToken ct)
    {
        using var result = await TranscriptionAsync(audioPath, model, language, prompt, "json", ct);
        return result.RootElement.TryGetProperty("text", out var text) ? text.GetString()!.Trim() : "";
    }

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeSegmentsAsync(string audioPath, string model, string language, string prompt, CancellationToken ct)
    {
        using var result = await TranscriptionAsync(audioPath, model, language, prompt, "verbose_json", ct);
        if (!result.RootElement.TryGetProperty("segments", out var segments)) return [];
        return segments.EnumerateArray().Select(segment => new TranscriptSegment(
            segment.GetProperty("start").GetDouble(), segment.GetProperty("end").GetDouble(), segment.GetProperty("text").GetString()!.Trim())).ToArray();
    }

    public async Task<JsonElement> ChatJsonAsync(string model, IReadOnlyList<LlmMessage> messages, JsonObject schema, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = JsonSerializer.SerializeToNode(messages),
            ["temperature"] = 0.2,
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "result", ["schema"] = schema.DeepClone(), ["strict"] = true }
            }
        };
        if (model.StartsWith("qwen/", StringComparison.Ordinal))
            body["reasoning_format"] = "hidden"; // qwen3 thinks first; keep only the answer

        HttpContent Content() => new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await PostAsync("chat/completions", Content, ct);
        if ((int)response.StatusCode == 400)
        {
            response.Dispose();
            // Some models do not support strict schemas: fall back to JSON mode with the schema in the prompt.
            body["response_format"] = new JsonObject { ["type"] = "json_object" };
            body["messages"]![0]!["content"] = messages[0].Content
                + "\nYalnız şu JSON şemasına uyan bir nesne döndür: " + PythonJson.Dumps(schema);
            response = await PostAsync("chat/completions", Content, ct);
        }
        using (response)
        {
            if ((int)response.StatusCode != 200) throw new GroqException($"chat failed: HTTP {(int)response.StatusCode}");
            try
            {
                using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var content = envelope.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                using var result = JsonDocument.Parse(content!);
                return result.RootElement.Clone();
            }
            catch (Exception error) when (error is KeyNotFoundException or IndexOutOfRangeException or JsonException or InvalidOperationException or ArgumentNullException)
            {
                throw new GroqException("chat returned no valid JSON", error);
            }
        }
    }
}

public static class RetryAfter
{
    public static TimeSpan Seconds(string? value, DateTimeOffset now, double defaultSeconds = 10, double capSeconds = 60)
    {
        var wait = defaultSeconds;
        if (value is not null)
        {
            var number = value.Trim();
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                wait = seconds;
            else if (number.Equals("inf", StringComparison.OrdinalIgnoreCase) || number.Equals("+inf", StringComparison.OrdinalIgnoreCase))
                wait = double.PositiveInfinity;
            else if (number.Equals("-inf", StringComparison.OrdinalIgnoreCase))
                wait = double.NegativeInfinity;
            else if (value.Contains(':') && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var retryAt))
                wait = (retryAt - now).TotalSeconds;
        }
        // Python's max(0, min(nan, cap)) returns zero.
        return TimeSpan.FromSeconds(double.IsNaN(wait) ? 0 : Math.Max(0, Math.Min(wait, capSeconds)));
    }
}

public static class PythonJson
{
    public static string Dumps(JsonNode node)
    {
        var result = new StringBuilder();
        Write(node, result);
        return result.ToString();
    }

    private static void Write(JsonNode? node, StringBuilder result)
    {
        switch (node)
        {
            case null:
                result.Append("null");
                break;
            case JsonObject obj:
                result.Append('{');
                var first = true;
                foreach (var (key, value) in obj)
                {
                    if (!first) result.Append(", ");
                    first = false;
                    WriteString(key, result);
                    result.Append(": ");
                    Write(value, result);
                }
                result.Append('}');
                break;
            case JsonArray array:
                result.Append('[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0) result.Append(", ");
                    Write(array[i], result);
                }
                result.Append(']');
                break;
            default:
                if (node.GetValueKind() == JsonValueKind.String)
                    WriteString(node.GetValue<string>(), result);
                else if (node.GetValueKind() == JsonValueKind.Number)
                    result.Append(Number((JsonValue)node));
                else
                    result.Append(node.ToJsonString());
                break;
        }
    }

    private static string Number(JsonValue value)
    {
        var raw = value.ToJsonString();
        var floating = value.TryGetValue<JsonElement>(out _)
            ? raw.IndexOfAny(['.', 'e', 'E']) >= 0
            : value.TryGetValue<double>(out _) || value.TryGetValue<float>(out _) || value.TryGetValue<decimal>(out _);
        if (!floating) return raw;

        var number = double.Parse(raw, CultureInfo.InvariantCulture);
        if (double.IsPositiveInfinity(number)) return "Infinity";
        if (double.IsNegativeInfinity(number)) return "-Infinity";
        var sign = double.IsNegative(number) ? "-" : "";
        var shortest = Math.Abs(number).ToString("R", CultureInfo.InvariantCulture);
        var scientific = shortest.Split('E');
        var mantissa = scientific[0];
        var exponent = scientific.Length == 2 ? int.Parse(scientific[1], CultureInfo.InvariantCulture) : 0;
        var point = mantissa.IndexOf('.');
        var digits = mantissa.Replace(".", "");
        var significant = digits.TrimStart('0');
        var position = (point < 0 ? mantissa.Length : point) + exponent - (digits.Length - significant.Length);
        digits = significant.TrimEnd('0');
        if (digits.Length == 0) return sign + "0.0";

        // Python uses scientific notation below 1e-4 and from 1e16, with a two-digit exponent.
        exponent = position - 1;
        if (exponent < -4 || exponent >= 16)
            return sign + digits[0] + (digits.Length > 1 ? "." + digits[1..] : "") + "e"
                + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("D2", CultureInfo.InvariantCulture);
        if (position <= 0) return sign + "0." + new string('0', -position) + digits;
        if (position >= digits.Length) return sign + digits + new string('0', position - digits.Length) + ".0";
        return sign + digits.Insert(position, ".");
    }

    private static void WriteString(string value, StringBuilder result)
    {
        result.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': result.Append("\\\""); break;
                case '\\': result.Append("\\\\"); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                case '\b': result.Append("\\b"); break;
                case '\f': result.Append("\\f"); break;
                default:
                    if (c < ' ') result.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else result.Append(c);
                    break;
            }
        }
        result.Append('"');
    }
}
