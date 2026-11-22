using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeetingWeb.Services.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetingWeb.Tests.Ai;

public class GroqClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("5", 5)]
    [InlineData("120", 60)]
    [InlineData("-3", 0)]
    [InlineData("abc", 10)]
    [InlineData(null, 10)]
    [InlineData("", 10)]
    [InlineData(" 2.5 ", 2.5)]
    [InlineData("1e1", 10)]
    [InlineData("NaN", 0)]
    [InlineData("inf", 60)]
    [InlineData("-inf", 0)]
    [InlineData("1,5", 10)]
    public void Retry_after_handles_numbers_and_invalid_values(string? value, double expected)
    {
        Assert.Equal(TimeSpan.FromSeconds(expected), RetryAfter.Seconds(value, Now));
    }

    [Theory]
    [InlineData(30, true, 30)]
    [InlineData(30, false, 30)]
    [InlineData(-30, true, 0)]
    [InlineData(120, true, 60)]
    public void Retry_after_reads_http_dates_relative_to_now(int offset, bool withZone, double expected)
    {
        var value = Now.AddSeconds(offset).ToString(withZone ? "R" : "ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        Assert.Equal(TimeSpan.FromSeconds(expected), RetryAfter.Seconds(value, Now));
    }

    [Fact]
    public void Retry_after_clamps_custom_default_to_custom_cap()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), RetryAfter.Seconds("abc", Now, defaultSeconds: 20, capSeconds: 15));
    }

    [Fact]
    public void Python_json_matches_python_dumps_with_unicode_nested_schema_and_escapes()
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["başlık"] = "Ölçüm İstanbul 😀\u2028\u007f",
            ["properties"] = new JsonObject
            {
                ["söz"] = new JsonObject { ["type"] = "string", ["description"] = "\"ters\\ çizgi\"\n\r\t\b\f\u0001\u001f" },
                ["sayı"] = new JsonObject { ["enum"] = new JsonArray(true, false, null, 42, -7) }
            },
            ["required"] = new JsonArray("söz")
        };
        // Generated with python3 json.dumps(schema, ensure_ascii=False).
        const string expected = "{\"type\": \"object\", \"başlık\": \"Ölçüm İstanbul 😀\u2028\u007f\", \"properties\": {\"söz\": {\"type\": \"string\", \"description\": \"\\\"ters\\\\ çizgi\\\"\\n\\r\\t\\b\\f\\u0001\\u001f\"}, \"sayı\": {\"enum\": [true, false, null, 42, -7]}}, \"required\": [\"söz\"]}";
        Assert.Equal(expected, PythonJson.Dumps(schema));
        Assert.Equal(expected, PythonJson.Dumps(JsonNode.Parse(schema.ToJsonString())!));
    }

    [Fact]
    public void Python_json_matches_python_number_formatting_without_losing_large_integers()
    {
        var parsed = JsonNode.Parse("[1.00, -0.0, 0.0001, 0.00001, 1e-6, 1e15, 1e16, 1.2345678901234567, 9007199254740993, 1e+20, 1e-10]")!;
        // Generated with python3 json.dumps(json.loads(input), ensure_ascii=False).
        const string expected = "[1.0, -0.0, 0.0001, 1e-05, 1e-06, 1000000000000000.0, 1e+16, 1.2345678901234567, 9007199254740993, 1e+20, 1e-10]";
        Assert.Equal(expected, PythonJson.Dumps(parsed));
        Assert.Equal("[1.0, 1e-05, 1e+16]", PythonJson.Dumps(new JsonArray(1.0, 0.00001, 1e16)));
    }

    [Theory]
    [InlineData("qwen/qwen3.8-27b", true)]
    [InlineData("openai/gpt-oss-20b", false)]
    [InlineData("Qwen/model", false)]
    public async Task Chat_sends_the_complete_python_request_body(string model, bool reasoning)
    {
        using var fake = new ClientFixture((_, _) => ChatReply("{\"a\": 1}"));
        var schema = new JsonObject { ["type"] = "object", ["additionalProperties"] = false };
        var result = await fake.Client.ChatJsonAsync(model, [new("system", "sistem"), new("user", "soru")], schema, default);

        Assert.Equal(1, result.GetProperty("a").GetInt32());
        var sent = Assert.Single(fake.Handler.Requests);
        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", sent.Uri);
        Assert.Equal("Bearer test-key", sent.Authorization);
        Assert.StartsWith("application/json", sent.ContentType);
        Assert.Null(fake.Http.DefaultRequestHeaders.Authorization);
        var expected = JsonNode.Parse("""
            {"model":"placeholder","messages":[{"role":"system","content":"sistem"},{"role":"user","content":"soru"}],"temperature":0.2,
             "response_format":{"type":"json_schema","json_schema":{"name":"result","schema":{"type":"object","additionalProperties":false},"strict":true}}}
            """)!.AsObject();
        expected["model"] = model;
        if (reasoning) expected["reasoning_format"] = "hidden";
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(sent.Body)), sent.Body);
        Assert.Equal("object", schema["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Client_sends_the_key_and_waits_on_rate_limit()
    {
        using var fake = new ClientFixture((_, attempt) => attempt == 1 ? RateLimit("7") : ChatReply("{\"a\":1}"));
        var result = await Chat(fake.Client);
        Assert.Equal(1, result.GetProperty("a").GetInt32());
        Assert.Equal(2, fake.Handler.Requests.Count);
        Assert.All(fake.Handler.Requests, request => Assert.Equal("Bearer test-key", request.Authorization));
        Assert.Equal([TimeSpan.FromSeconds(7)], fake.Delays);
        Assert.Single(fake.Logger.Entries);
    }

    [Fact]
    public async Task Client_final_rate_limit_does_not_sleep_or_log_again()
    {
        using var fake = new ClientFixture((_, _) => RateLimit("7"), maxAttempts: 2);
        var error = await Assert.ThrowsAsync<GroqException>(() => Chat(fake.Client));
        Assert.Equal("chat failed: HTTP 429", error.Message);
        Assert.Equal(2, fake.Handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(7)], fake.Delays);
        Assert.Equal((LogLevel.Warning, "Groq rate limit, waiting 7s (attempt 1)"), Assert.Single(fake.Logger.Entries));
    }

    [Theory]
    [InlineData("Wed, 21 Oct 2015 07:28:00 GMT", 0)]
    [InlineData("Sat, 21 Nov 2026 00:00:30 GMT", 30)]
    [InlineData(null, 10)]
    [InlineData("120", 60)]
    public async Task Client_retries_rate_limit_with_raw_retry_after(string? header, double seconds)
    {
        using var fake = new ClientFixture((_, attempt) => attempt == 1 ? RateLimit(header) : ChatReply());
        Assert.True((await Chat(fake.Client)).GetProperty("ok").GetBoolean());
        Assert.Equal(2, fake.Handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(seconds)], fake.Delays);
    }

    [Fact]
    public async Task Client_gives_up_with_a_clear_error_and_linear_backoff_without_logging_content()
    {
        using var fake = new ClientFixture((_, _) => Reply(500, "private response body"));
        var error = await Assert.ThrowsAsync<GroqException>(() => Chat(fake.Client));
        Assert.Equal("chat failed: HTTP 500", error.Message);
        Assert.DoesNotContain("private response body", error.ToString());
        Assert.Equal(5, fake.Handler.Requests.Count);
        Assert.Equal(new[] { 2, 4, 6, 8 }.Select(seconds => TimeSpan.FromSeconds(seconds)), fake.Delays);
        Assert.Empty(fake.Logger.Entries);
    }

    [Fact]
    public async Task Client_disposes_intermediate_and_final_responses()
    {
        var contents = new List<TrackingContent>();
        using var fake = new ClientFixture((_, attempt) =>
        {
            var content = new TrackingContent(attempt < 3 ? "private error" : "{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}");
            contents.Add(content);
            return new HttpResponseMessage((HttpStatusCode)(attempt < 3 ? 503 : 200)) { Content = content };
        });
        await Chat(fake.Client);
        Assert.Equal(3, contents.Count);
        Assert.All(contents, content => Assert.True(content.Disposed));
    }

    [Theory]
    [InlineData("system")]
    [InlineData("user")]
    public async Task Client_falls_back_once_and_appends_python_schema_to_first_message(string firstRole)
    {
        using var fake = new ClientFixture((_, attempt) => attempt == 1 ? Reply(400, "unsupported") : ChatReply());
        LlmMessage[] messages = [new(firstRole, "ilk"), new("user", "ikinci")];
        var schema = new JsonObject { ["type"] = "object", ["başlık"] = "Türkçe" };
        await fake.Client.ChatJsonAsync("qwen/model", messages, schema, default);
        Assert.Equal(2, fake.Handler.Requests.Count);
        var first = JsonNode.Parse(fake.Handler.Requests[0].Body)!;
        var second = JsonNode.Parse(fake.Handler.Requests[1].Body)!;
        Assert.Equal("json_schema", first["response_format"]!["type"]!.GetValue<string>());
        first["response_format"] = new JsonObject { ["type"] = "json_object" };
        first["messages"]![0]!["content"] = "ilk\nYalnız şu JSON şemasına uyan bir nesne döndür: {\"type\": \"object\", \"başlık\": \"Türkçe\"}";
        Assert.True(JsonNode.DeepEquals(first, second));
        Assert.Equal("ilk", messages[0].Content);
        Assert.Empty(fake.Delays);
    }

    [Fact]
    public async Task Second_schema_rejection_does_not_fall_back_again()
    {
        using var fake = new ClientFixture((_, _) => Reply(400, "private response"));
        var error = await Assert.ThrowsAsync<GroqException>(() => Chat(fake.Client));
        Assert.Equal("chat failed: HTTP 400", error.Message);
        Assert.Equal(2, fake.Handler.Requests.Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{}]}")]
    [InlineData("{\"choices\":[{\"message\":{}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"not json\"}}]}")]
    public async Task Invalid_chat_json_has_a_clear_error_with_inner_exception(string response)
    {
        using var fake = new ClientFixture((_, _) => Reply(200, response));
        var error = await Assert.ThrowsAsync<GroqException>(() => Chat(fake.Client));
        Assert.Equal("chat returned no valid JSON", error.Message);
        Assert.NotNull(error.InnerException);
        Assert.DoesNotContain(response, error.Message);
        Assert.Single(fake.Handler.Requests);
    }

    [Theory]
    [InlineData("[]", JsonValueKind.Array)]
    [InlineData("null", JsonValueKind.Null)]
    [InlineData("42", JsonValueKind.Number)]
    public async Task Chat_returns_the_parsed_root_like_python_json_loads(string content, JsonValueKind kind)
    {
        using var fake = new ClientFixture((_, _) => ChatReply(content));
        Assert.Equal(kind, (await Chat(fake.Client)).ValueKind);
    }

    [Fact]
    public async Task Cancellation_during_retry_stops_before_the_next_request()
    {
        using var source = new CancellationTokenSource();
        using var handler = new GroqTestHandler((_, _) => RateLimit("7"));
        using var http = NewHttp(handler);
        var client = new GroqClient(http, "key", new TestLogger(), (_, token) =>
        {
            Assert.Equal(source.Token, token);
            source.Cancel();
            return Task.FromCanceled(token);
        }, () => Now);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Chat(client, source.Token));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(429, false)]
    [InlineData(503, false)]
    [InlineData(429, true)]
    [InlineData(503, true)]
    public async Task Transcription_recreates_multipart_with_all_fields_and_file_on_every_attempt(int status, bool segments)
    {
        using var fake = new ClientFixture((_, attempt) => attempt == 1
            ? status == 429 ? RateLimit("7") : Reply(status, "retry")
            : Reply(200, segments ? "{\"segments\":[{\"start\":1.2,\"end\":3.4,\"text\":\" söz \"}]}" : "{\"text\":\" söz \"}"));
        var path = Path.Combine(Path.GetTempPath(), $"groq-{Guid.NewGuid():N}.flac");
        byte[] audio = [0, 255, 1, 2, 3];
        await File.WriteAllBytesAsync(path, audio);
        try
        {
            if (segments)
                Assert.Equal([new TranscriptSegment(1.2, 3.4, "söz")], await fake.Client.TranscribeSegmentsAsync(path, "whisper", "tr", "prompt", default));
            else
                Assert.Equal("söz", await fake.Client.TranscribeAsync(path, "whisper", "tr", "prompt", default));

            Assert.Equal(2, fake.Handler.Requests.Count);
            Assert.NotSame(fake.Handler.Contents[0], fake.Handler.Contents[1]);
            foreach (var request in fake.Handler.Requests)
            {
                Assert.Equal("https://api.groq.com/openai/v1/audio/transcriptions", request.Uri);
                Assert.Equal("Bearer test-key", request.Authorization);
                Assert.StartsWith("multipart/form-data; boundary=", request.ContentType);
                var fields = request.Parts!.ToDictionary(part => part.Name);
                Assert.Equal(6, fields.Count);
                Assert.Equal("whisper", Encoding.UTF8.GetString(fields["model"].Bytes));
                Assert.Equal("tr", Encoding.UTF8.GetString(fields["language"].Bytes));
                Assert.Equal("prompt", Encoding.UTF8.GetString(fields["prompt"].Bytes));
                Assert.Equal(segments ? "verbose_json" : "json", Encoding.UTF8.GetString(fields["response_format"].Bytes));
                Assert.Equal("0", Encoding.UTF8.GetString(fields["temperature"].Bytes));
                Assert.Equal(Path.GetFileName(path), fields["file"].FileName);
                Assert.Equal("audio/flac", fields["file"].ContentType);
                Assert.Equal(audio, fields["file"].Bytes);
            }
            Assert.Equal([TimeSpan.FromSeconds(status == 429 ? 7 : 2)], fake.Delays);
            using var reopened = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transcription_missing_text_or_segments_defaults_to_empty(bool segments)
    {
        using var fake = new ClientFixture((_, _) => Reply(200, "{}"));
        var path = Path.GetTempFileName();
        try
        {
            if (segments) Assert.Empty(await fake.Client.TranscribeSegmentsAsync(path, "m", "tr", "", default));
            else Assert.Equal("", await fake.Client.TranscribeAsync(path, "m", "tr", "", default));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, 401)]
    [InlineData(true, 401)]
    [InlineData(false, 201)]
    [InlineData(true, 201)]
    public async Task Transcription_requires_status_200_and_does_not_expose_response_body(bool segments, int status)
    {
        using var fake = new ClientFixture((_, _) => Reply(status, "private transcript"));
        var path = Path.GetTempFileName();
        try
        {
            var error = await Assert.ThrowsAsync<GroqException>(async () =>
            {
                if (segments) await fake.Client.TranscribeSegmentsAsync(path, "m", "tr", "private prompt", default);
                else await fake.Client.TranscribeAsync(path, "m", "tr", "private prompt", default);
            });
            Assert.Equal($"transcription failed: HTTP {status}", error.Message);
            Assert.DoesNotContain("private transcript", error.ToString());
            Assert.DoesNotContain("private prompt", error.ToString());
            Assert.Single(fake.Handler.Requests);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_key_fails_with_a_clear_error_in_both_constructors(string? key)
    {
        using var http = new HttpClient();
        Assert.Equal("GROQ_API_KEY is not set", Assert.Throws<InvalidOperationException>(() =>
            new GroqClient(http, key!, NullLogger<GroqClient>.Instance, (_, _) => Task.CompletedTask, () => Now)).Message);
        Assert.Equal("GROQ_API_KEY is not set", Assert.Throws<InvalidOperationException>(() =>
            new GroqClient(http, new AiOptions { GroqApiKey = key! }, NullLogger<GroqClient>.Instance)).Message);
    }

    private static Task<JsonElement> Chat(GroqClient client, CancellationToken ct = default) =>
        client.ChatJsonAsync("m", [new("user", "x")], new JsonObject { ["type"] = "object" }, ct);

    internal static HttpResponseMessage Reply(int status, string body) => new((HttpStatusCode)status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    internal static HttpResponseMessage ChatReply(string content = "{\"ok\":true}") =>
        Reply(200, JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }));

    private static HttpResponseMessage RateLimit(string? value)
    {
        var response = Reply(429, "private rate limit response");
        if (value is not null) response.Headers.TryAddWithoutValidation("Retry-After", value);
        return response;
    }

    internal static HttpClient NewHttp(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.groq.com/openai/v1/")
    };

    private sealed class ClientFixture : IDisposable
    {
        public GroqTestHandler Handler { get; }
        public HttpClient Http { get; }
        public GroqClient Client { get; }
        public TestLogger Logger { get; } = new();
        public List<TimeSpan> Delays { get; } = [];

        public ClientFixture(Func<HttpRequestMessage, int, HttpResponseMessage> respond, int maxAttempts = 5)
        {
            Handler = new(respond);
            Http = NewHttp(Handler);
            Client = new(Http, "test-key", Logger, (wait, _) =>
            {
                Delays.Add(wait);
                return Task.CompletedTask;
            }, () => Now, maxAttempts);
        }

        public void Dispose() => Http.Dispose();
    }

    private sealed class TestLogger : ILogger<GroqClient>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class TrackingContent(string value) : StringContent(value)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}

internal sealed class GroqTestHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
{
    internal sealed record Part(string Name, string? FileName, string? ContentType, byte[] Bytes);
    internal sealed record CapturedRequest(string Uri, string? Authorization, string? ContentType, string Body, IReadOnlyList<Part>? Parts);
    public List<CapturedRequest> Requests { get; } = [];
    public List<HttpContent> Contents { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Contents.Add(request.Content!);
        List<Part>? parts = null;
        if (request.Content is MultipartFormDataContent multipart)
        {
            parts = [];
            foreach (var part in multipart)
                parts.Add(new(part.Headers.ContentDisposition!.Name!.Trim('"'), part.Headers.ContentDisposition.FileName?.Trim('"'),
                    part.Headers.ContentType?.ToString(), await part.ReadAsByteArrayAsync(cancellationToken)));
        }
        Requests.Add(new(request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.ToString(), await request.Content!.ReadAsStringAsync(cancellationToken), parts));
        return respond(request, Requests.Count);
    }
}
