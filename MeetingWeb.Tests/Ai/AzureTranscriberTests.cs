using System.Net;
using System.Text;
using MeetingWeb.Services.Ai;
using Microsoft.Extensions.Configuration;

namespace MeetingWeb.Tests.Ai;

public class AzureTranscriberTests
{
    private const string Endpoint = "https://speech.example.test///";
    private const string Key = "test-azure-key";
    private const string Definition = "{\"locales\": [\"tr-TR\"], \"diarization\": {\"enabled\": true, \"maxSpeakers\": 10}}";

    [Fact]
    public async Task Request_uses_azure_fast_transcription_multipart()
    {
        using var fake = new Fixture((_, _) => Reply(200, "{\"phrases\":[]}"));
        ITranscriber transcriber = fake.Transcriber;
        Assert.Equal("", await transcriber.TranscribeAsync("input.wav", null, default));
        var request = Assert.Single(fake.Handler.Requests);
        Assert.Equal("https://speech.example.test/speechtotext/transcriptions:transcribe?api-version=2025-10-15", request.Uri);
        Assert.Equal([Key], fake.Keys);
        Assert.False(fake.Http.DefaultRequestHeaders.Contains("Ocp-Apim-Subscription-Key"));
        Assert.StartsWith("multipart/form-data; boundary=", request.ContentType);
        var parts = request.Parts!.ToDictionary(part => part.Name);
        Assert.Equal(2, parts.Count);
        Assert.Equal("mono.flac", parts["audio"].FileName);
        Assert.Equal("audio/flac", parts["audio"].ContentType);
        Assert.Equal("mono", Encoding.UTF8.GetString(parts["audio"].Bytes));
        Assert.Null(parts["definition"].FileName);
        Assert.StartsWith("text/plain", parts["definition"].ContentType);
        // Python json.dumps with its default ensure_ascii=True, for the ASCII locale.
        Assert.Equal(Encoding.UTF8.GetBytes(Definition), parts["definition"].Bytes);
        Assert.Equal("input.wav", fake.Audio.Source);
        AssertCleaned(fake.Audio);
    }

    [Fact]
    public async Task Labels_follow_first_appearance_and_merge_consecutive_phrases()
    {
        using var fake = new Fixture((_, _) => Reply(200, """
            {"phrases":[
              {"offsetMilliseconds":3000,"speaker":2,"text":"D."},
              {"offsetMilliseconds":1000,"speaker":2,"text":" B. "},
              {"offsetMilliseconds":500,"speaker":9,"text":"  "},
              {"offsetMilliseconds":2000,"speaker":1,"text":"C."},
              {"offsetMilliseconds":0,"speaker":2,"text":"A."}]}
            """));
        Assert.Equal("Konuşmacı 1: A. B.\nKonuşmacı 2: C.\nKonuşmacı 1: D.",
            await fake.Transcriber.TranscribeAsync("input.wav", null, default));
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"speaker\":null")]
    [InlineData(",\"speaker\":0")]
    [InlineData(",\"speaker\":2")]
    public async Task Missing_or_single_speaker_returns_plain_text(string speaker)
    {
        using var fake = new Fixture((_, _) => Reply(200,
            "{\"phrases\":[{\"offsetMilliseconds\":1000,\"text\":\" B. \"" + speaker
            + "},{\"offsetMilliseconds\":0,\"text\":\" A. \"" + speaker + "}]}"));
        Assert.Equal("A. B.", await fake.Transcriber.TranscribeAsync("input.wav", null, default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"phrases\":[],")]
    [InlineData("\"phrases\":null,")]
    public async Task Combined_phrases_are_used_when_phrases_are_missing_or_empty(string phrases)
    {
        using var fake = new Fixture((_, _) => Reply(200, "{" + phrases
            + "\"combinedPhrases\":[{\"text\":\" A. \"},{\"text\":\" \"},{},{\"text\":\"B.\"}]}"));
        Assert.Equal("A. B.", await fake.Transcriber.TranscribeAsync("input.wav", null, default));
    }

    [Fact]
    public async Task Equal_or_missing_offsets_keep_input_order_and_unlabelled_phrases()
    {
        using var fake = new Fixture((_, _) => Reply(200, """
            {"phrases":[
              {"speaker":"b","text":"\u001c A. \u001f"},
              {"offsetMilliseconds":0,"speaker":"b","text":"B."},
              {"speaker":"skip"},
              {"speaker":null,"text":"C."},
              {"speaker":0,"text":"D."},
              {"text":"E."}]}
            """));
        Assert.Equal("Konuşmacı 1: A. B.\nC.\nKonuşmacı 2: D.\nE.",
            await fake.Transcriber.TranscribeAsync("input.wav", null, default));
    }

    [Fact]
    public async Task Missing_combined_phrases_returns_empty_text()
    {
        using var fake = new Fixture((_, _) => Reply(200, "{}"));
        Assert.Equal("", await fake.Transcriber.TranscribeAsync("input.wav", null, default));
    }

    [Theory]
    [InlineData(18000)]
    [InlineData(18001)]
    public async Task Five_hour_limit_prevents_http_requests(double duration)
    {
        using var fake = new Fixture((_, _) => throw new InvalidOperationException("no HTTP request expected"));
        fake.Audio.Duration = duration;
        var error = await Assert.ThrowsAsync<AzureSpeechException>(() => fake.Transcriber.TranscribeAsync("input.wav", null, default));
        Assert.Equal("recording is longer than Azure's 5 hour limit", error.Message);
        Assert.Empty(fake.Handler.Requests);
        AssertCleaned(fake.Audio);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(201)]
    public async Task Non_retryable_error_fails_immediately_without_leaking_body(int status)
    {
        using var fake = new Fixture((_, _) => Reply(status, Key));
        var error = await Assert.ThrowsAsync<AzureSpeechException>(() => fake.Transcriber.TranscribeAsync("input.wav", null, default));
        Assert.Equal($"transcription failed: HTTP {status}", error.Message);
        Assert.DoesNotContain(Key, error.ToString());
        Assert.Single(fake.Handler.Requests);
        Assert.Empty(fake.Delays);
        AssertCleaned(fake.Audio);
    }

    [Theory]
    [InlineData("7", 7)]
    [InlineData("120", 60)]
    [InlineData(null, 10)]
    [InlineData("Wed, 21 Oct 2015 07:28:00 GMT", 0)]
    [InlineData("Sat, 21 Nov 2026 00:00:30 GMT", 30)]
    public async Task Rate_limit_retries_with_the_same_audio(string? retryAfter, double wait)
    {
        using var fake = new Fixture((_, attempt) => attempt == 1 ? RateLimit(retryAfter) : Reply(200, "{\"combinedPhrases\":[{\"text\":\"A.\"}]}"));
        Assert.Equal("A.", await fake.Transcriber.TranscribeAsync("input.wav", null, default));
        Assert.Equal([TimeSpan.FromSeconds(wait)], fake.Delays);
        Assert.Equal(2, fake.Handler.Requests.Count);
        Assert.NotSame(fake.Handler.Contents[0], fake.Handler.Contents[1]);
        Assert.All(fake.Handler.Requests, request =>
        {
            Assert.Contains("mono\r\n", request.Body);
            Assert.Equal("mono", Encoding.UTF8.GetString(Assert.Single(request.Parts!, part => part.Name == "audio").Bytes));
            Assert.Equal(Definition, Encoding.UTF8.GetString(Assert.Single(request.Parts!, part => part.Name == "definition").Bytes));
        });
        Assert.Equal([Key, Key], fake.Keys);
        AssertCleaned(fake.Audio);
    }

    [Fact]
    public async Task Server_errors_back_off_then_fail_without_leaking_the_key()
    {
        using var fake = new Fixture((_, _) => Reply(500, Key));
        var error = await Assert.ThrowsAsync<AzureSpeechException>(() => fake.Transcriber.TranscribeAsync("input.wav", null, default));
        Assert.Equal("transcription failed: HTTP 500", error.Message);
        Assert.DoesNotContain(Key, error.ToString());
        Assert.Equal(5, fake.Handler.Requests.Count);
        Assert.Equal(new[] { 2, 4, 6, 8 }.Select(seconds => TimeSpan.FromSeconds(seconds)), fake.Delays);
        Assert.All(fake.Handler.Requests, request => Assert.Contains("mono\r\n", request.Body));
        AssertCleaned(fake.Audio);
    }

    [Fact]
    public async Task Final_rate_limit_raises_after_max_attempts()
    {
        using var fake = new Fixture((_, _) => RateLimit("7"), maxAttempts: 2);
        var error = await Assert.ThrowsAsync<AzureSpeechException>(() => fake.Transcriber.TranscribeAsync("input.wav", null, default));
        Assert.Equal("transcription failed: HTTP 429", error.Message);
        Assert.Equal(2, fake.Handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(7)], fake.Delays);
        AssertCleaned(fake.Audio);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Attempt_count_must_be_positive(int maxAttempts)
    {
        using var http = new HttpClient();
        Assert.Equal("maxAttempts", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AzureTranscriber(http, new FakeAudio(), Endpoint, Key, maxAttempts: maxAttempts)).ParamName);
    }

    [Fact]
    public async Task Vtt_is_ignored_and_custom_definition_is_preserved()
    {
        using var fake = new Fixture((_, _) => Reply(200, "{\"combinedPhrases\":[{\"text\":\"A.\"}]}"));
        var transcriber = new AzureTranscriber(fake.Http, fake.Audio, Endpoint, Key, locale: "en-US", maxSpeakers: 4);
        const string vtt = "WEBVTT\n\n00:00:00.000 --> 00:00:04.000\nAli: söz";
        Assert.Equal("A.", await transcriber.TranscribeAsync("input.wav", null, default));
        Assert.Equal("A.", await transcriber.TranscribeAsync("input.wav", vtt, default));
        Assert.Equal(2, fake.Handler.Requests.Count);
        foreach (var request in fake.Handler.Requests)
        {
            Assert.Equal("https://speech.example.test/speechtotext/transcriptions:transcribe?api-version=2025-10-15", request.Uri);
            var parts = request.Parts!.ToDictionary(part => part.Name);
            Assert.Equal(2, parts.Count);
            Assert.Equal("mono", Encoding.UTF8.GetString(parts["audio"].Bytes));
            Assert.Equal("{\"locales\": [\"en-US\"], \"diarization\": {\"enabled\": true, \"maxSpeakers\": 4}}",
                Encoding.UTF8.GetString(parts["definition"].Bytes));
        }
        AssertCleaned(fake.Audio);
    }

    [Fact]
    public async Task Cancelled_token_raises_operation_cancelled_and_removes_temporary_files()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var fake = new Fixture((_, _) => throw new InvalidOperationException("no HTTP request expected"));
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fake.Transcriber.TranscribeAsync("input.wav", null, source.Token));
        Assert.Equal(source.Token, error.CancellationToken);
        Assert.Empty(fake.Handler.Requests);
        AssertCleaned(fake.Audio);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Cancellation_during_retry_stops_before_the_next_request(int status)
    {
        using var source = new CancellationTokenSource();
        using var fake = new Fixture((_, _) => Reply(status, Key));
        var transcriber = new AzureTranscriber(fake.Http, fake.Audio, Endpoint, Key, delay: (_, ct) =>
        {
            Assert.Equal(source.Token, ct);
            source.Cancel();
            return Task.FromCanceled(ct);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transcriber.TranscribeAsync("input.wav", null, source.Token));
        Assert.Single(fake.Handler.Requests);
        Assert.All(fake.Audio.Tokens, token => Assert.Equal(source.Token, token));
        AssertCleaned(fake.Audio);
    }

    [Fact]
    public async Task Temporary_directory_is_removed_when_audio_preparation_throws()
    {
        using var fake = new Fixture((_, _) => throw new InvalidOperationException("no HTTP request expected"));
        fake.Audio.Error = new AudioToolException("ffmpeg failed: exit code 1");
        var error = await Assert.ThrowsAsync<AudioToolException>(() => fake.Transcriber.TranscribeAsync("input.wav", null, default));
        Assert.Same(fake.Audio.Error, error);
        AssertCleaned(fake.Audio);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("not-a-number")]
    public void Configuration_keeps_azure_settings_raw_for_factory_validation(string? maxSpeakers)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TRANSCRIBER"] = "azure",
            ["AZURE_SPEECH_KEY"] = Key,
            ["AZURE_SPEECH_ENDPOINT"] = Endpoint,
            ["AZURE_SPEECH_MAX_SPEAKERS"] = maxSpeakers
        }).Build();
        var options = AiOptions.FromConfiguration(configuration);
        Assert.Equal("azure", options.Transcriber);
        Assert.Equal(Key, options.AzureSpeechKey);
        Assert.Equal(Endpoint, options.AzureSpeechEndpoint);
        Assert.Equal(maxSpeakers, options.AzureSpeechMaxSpeakers);
    }

    private static HttpResponseMessage RateLimit(string? retryAfter)
    {
        var response = Reply(429, Key);
        if (retryAfter != null) response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return response;
    }

    private static HttpResponseMessage Reply(int status, string body) => new((HttpStatusCode)status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static void AssertCleaned(FakeAudio audio)
    {
        Assert.NotNull(audio.Mono);
        var directory = Path.GetDirectoryName(audio.Mono)!;
        Assert.StartsWith("briefyn_azure_", Path.GetFileName(directory));
        Assert.False(Directory.Exists(directory));
    }

    private sealed class Fixture : IDisposable
    {
        public FakeAudio Audio { get; } = new();
        public GroqTestHandler Handler { get; }
        public HttpClient Http { get; }
        public AzureTranscriber Transcriber { get; }
        public List<string> Keys { get; } = [];
        public List<TimeSpan> Delays { get; } = [];

        public Fixture(Func<HttpRequestMessage, int, HttpResponseMessage> respond, int maxAttempts = 5)
        {
            Handler = new((request, attempt) =>
            {
                Keys.Add(Assert.Single(request.Headers.GetValues("Ocp-Apim-Subscription-Key")));
                Assert.True(File.Exists(Audio.Mono));
                return respond(request, attempt);
            });
            Http = new(Handler);
            Transcriber = new(Http, Audio, Endpoint, Key, maxAttempts: maxAttempts, delay: (wait, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                Delays.Add(wait);
                return Task.CompletedTask;
            }, now: () => new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero));
        }

        public void Dispose() => Http.Dispose();
    }

    private sealed class FakeAudio : IAudioTools
    {
        public double Duration { get; set; } = 60;
        public Exception? Error { get; set; }
        public List<CancellationToken> Tokens { get; } = [];
        public string? Source { get; private set; }
        public string? Mono { get; private set; }

        public async Task<string> ToMonoFlacAsync(string source, string target, CancellationToken ct)
        {
            Tokens.Add(ct);
            Source = source;
            Mono = target;
            await File.WriteAllTextAsync(target, "mono", ct);
            if (Error != null) throw Error;
            return target;
        }

        public Task<double> DurationSecondsAsync(string audio, CancellationToken ct)
        {
            Tokens.Add(ct);
            ct.ThrowIfCancellationRequested();
            Assert.Equal(Mono, audio);
            return Task.FromResult(Duration);
        }

        public Task<IReadOnlyList<double>> SilenceMidpointsAsync(string audio, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CutAsync(string audio, double start, double end, string target, CancellationToken ct) => throw new NotSupportedException();
    }
}
