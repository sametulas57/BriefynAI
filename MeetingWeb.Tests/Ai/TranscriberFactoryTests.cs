// Ports the final build_transcriber tests in test_azure_transcriber.py; no HTTP or ffmpeg is run.
using MeetingWeb.Services.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetingWeb.Tests.Ai;

public class TranscriberFactoryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Default_transcriber_is_groq(string? configured)
    {
        var factory = Create(new AiOptions { GroqApiKey = "test-key", Transcriber = configured });
        Assert.IsType<GroqTranscriber>(factory.Create(null));
        Assert.IsType<GroqTranscriber>(factory.Create(""));
    }

    [Theory]
    [InlineData(null, null, "AZURE_SPEECH_KEY is not set")]
    [InlineData("", "https://speech.example.test/", "AZURE_SPEECH_KEY is not set")]
    [InlineData("test-key", null, "AZURE_SPEECH_ENDPOINT is not set")]
    [InlineData("test-key", "", "AZURE_SPEECH_ENDPOINT is not set")]
    public void Build_azure_requires_key_and_endpoint_in_that_order(string? key, string? endpoint, string expected)
    {
        var factory = Create(new AiOptions { Transcriber = "azure", AzureSpeechKey = key, AzureSpeechEndpoint = endpoint });
        Assert.Equal(expected, Assert.Throws<InvalidOperationException>(() => factory.Create(null)).Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7")]
    public void Build_azure_from_environment_configuration(string? maxSpeakers)
    {
        var http = new RecordingHttpFactory();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TRANSCRIBER"] = "azure",
            ["AZURE_SPEECH_KEY"] = "test-key",
            ["AZURE_SPEECH_ENDPOINT"] = "https://speech.example.test/",
            ["AZURE_SPEECH_MAX_SPEAKERS"] = maxSpeakers
        }).Build();
        var factory = Create(AiOptions.FromConfiguration(configuration), http);
        Assert.IsType<AzureTranscriber>(factory.Create(null));
        Assert.Equal("AzureSpeech", Assert.Single(http.Names));
    }

    [Fact]
    public void Explicit_groq_overrides_configured_azure()
    {
        var http = new RecordingHttpFactory();
        var factory = Create(new AiOptions { Transcriber = "azure", GroqApiKey = "test-key" }, http);
        Assert.IsType<GroqTranscriber>(factory.Create("groq"));
        Assert.Equal("Groq", Assert.Single(http.Names));
    }

    [Theory]
    [InlineData("rust")]
    [InlineData("Groq")]
    [InlineData(" groq ")]
    public void Unknown_kind_is_not_trimmed_or_case_folded(string kind)
    {
        var factory = Create(new AiOptions { Transcriber = kind });
        Assert.Equal($"Unknown TRANSCRIBER: {kind}", Assert.Throws<InvalidOperationException>(() => factory.Create(null)).Message);
        Assert.Equal($"Unknown TRANSCRIBER: {kind}", Assert.Throws<InvalidOperationException>(() => factory.Create(kind)).Message);
    }

    [Fact]
    public void Missing_groq_key_is_checked_on_create()
    {
        var factory = Create(new AiOptions());
        Assert.Equal("GROQ_API_KEY is not set", Assert.Throws<InvalidOperationException>(() => factory.Create(null)).Message);
    }

    [Fact]
    public void Invalid_azure_max_speakers_is_rejected()
    {
        var factory = Create(new AiOptions
        {
            Transcriber = "azure",
            AzureSpeechKey = "test-key",
            AzureSpeechEndpoint = "https://speech.example.test/",
            AzureSpeechMaxSpeakers = "many"
        });
        Assert.Throws<FormatException>(() => factory.Create(null));
    }

    private static TranscriberFactory Create(AiOptions options, RecordingHttpFactory? http = null) =>
        new(http ?? new RecordingHttpFactory(), options, new FfmpegAudioTools(), NullLoggerFactory.Instance);

    private sealed class RecordingHttpFactory : IHttpClientFactory
    {
        public List<string> Names { get; } = [];
        public HttpClient CreateClient(string name)
        {
            Names.Add(name);
            return new HttpClient();
        }
    }
}
