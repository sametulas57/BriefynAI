using System.Text.Json.Nodes;
using MeetingWeb.Services.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetingWeb.Tests.Ai;

public class LlmClientTests
{
    [Fact]
    public async Task Groq_llm_puts_the_system_text_first_and_uses_its_model()
    {
        using var handler = new GroqTestHandler((_, _) => GroqClientTests.ChatReply());
        using var http = GroqClientTests.NewHttp(handler);
        var client = new GroqClient(http, "k", NullLogger<GroqClient>.Instance, (_, _) => Task.CompletedTask, () => DateTimeOffset.UtcNow);
        ILlmClient llm = new GroqLlm(client, "openai/gpt-oss-120b");
        LlmMessage[] messages = [new("user", "soru")];
        var result = await llm.CompleteJsonAsync("sistem", messages, new JsonObject { ["type"] = "object" });

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("openai/gpt-oss-120b", llm.Model);
        var body = JsonNode.Parse(Assert.Single(handler.Requests).Body)!;
        Assert.Equal(llm.Model, body["model"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""[{"role":"system","content":"sistem"},{"role":"user","content":"soru"}]"""), body["messages"]));
        Assert.Single(messages);
        Assert.Equal("soru", messages[0].Content);
    }

    [Theory]
    [InlineData(LlmTask.Summary, "SUMMARIZER", "AI_SUMMARY_MODEL", "ozet-modeli")]
    [InlineData(LlmTask.Speakers, "SPEAKER_SUGGESTER", "AI_SPEAKERS_MODEL", "konusmaci-modeli")]
    [InlineData(LlmTask.Answer, "ANSWERER", "AI_ANSWER_MODEL", "cevap-modeli")]
    public void Each_task_reads_its_own_provider_and_model(LlmTask task, string providerKey, string modelKey, string model)
    {
        var settings = new Dictionary<string, string?>
        {
            ["GROQ_API_KEY"] = "k",
            ["SUMMARIZER"] = "other-summary",
            ["SPEAKER_SUGGESTER"] = "other-speakers",
            ["ANSWERER"] = "other-answer",
            ["AI_SUMMARY_MODEL"] = "other-summary-model",
            ["AI_SPEAKERS_MODEL"] = "other-speakers-model",
            ["AI_ANSWER_MODEL"] = "other-answer-model"
        };
        settings[providerKey] = "groq";
        settings[modelKey] = model;
        using var httpFactory = new RecordingHttpFactory();
        ILlmClientFactory factory = new LlmClientFactory(AiOptions.FromConfiguration(Configuration(settings)), httpFactory, NullLogger<GroqClient>.Instance);
        Assert.Empty(httpFactory.Names);
        Assert.Equal(model, factory.Create(task).Model);
        Assert.Equal(["Groq"], httpFactory.Names);
    }

    [Theory]
    [InlineData(LlmTask.Summary, "qwen/qwen3.8-27b", false)]
    [InlineData(LlmTask.Speakers, "openai/gpt-oss-120b", false)]
    [InlineData(LlmTask.Answer, "openai/gpt-oss-20b", false)]
    [InlineData(LlmTask.Summary, "qwen/qwen3.8-27b", true)]
    [InlineData(LlmTask.Speakers, "openai/gpt-oss-120b", true)]
    [InlineData(LlmTask.Answer, "openai/gpt-oss-20b", true)]
    public void Missing_and_empty_compose_settings_fall_back_to_each_task_default(LlmTask task, string model, bool empty)
    {
        var settings = new Dictionary<string, string?> { ["GROQ_API_KEY"] = "test" };
        if (empty)
            foreach (var key in new[] { "SUMMARIZER", "AI_SUMMARY_MODEL", "SPEAKER_SUGGESTER", "AI_SPEAKERS_MODEL", "ANSWERER", "AI_ANSWER_MODEL" })
                settings[key] = "";
        using var httpFactory = new RecordingHttpFactory();
        var factory = new LlmClientFactory(AiOptions.FromConfiguration(Configuration(settings)), httpFactory, NullLogger<GroqClient>.Instance);
        Assert.Equal(model, factory.Create(task).Model);
    }

    [Theory]
    [InlineData(LlmTask.Summary, "SUMMARIZER")]
    [InlineData(LlmTask.Speakers, "SPEAKER_SUGGESTER")]
    [InlineData(LlmTask.Answer, "ANSWERER")]
    public void Unknown_provider_fails_clearly_before_constructing_groq(LlmTask task, string key)
    {
        using var httpFactory = new RecordingHttpFactory();
        var options = AiOptions.FromConfiguration(Configuration(new() { [key] = "bilinmeyen" }));
        var factory = new LlmClientFactory(options, httpFactory, NullLogger<GroqClient>.Instance);
        Assert.Equal($"Unknown {key}: bilinmeyen", Assert.Throws<InvalidOperationException>(() => factory.Create(task)).Message);
        Assert.Empty(httpFactory.Names);
    }

    [Fact]
    public void Unknown_task_fails_clearly_before_constructing_groq()
    {
        using var httpFactory = new RecordingHttpFactory();
        var factory = new LlmClientFactory(new AiOptions(), httpFactory, NullLogger<GroqClient>.Instance);
        Assert.Equal("Unknown LLM task: 123", Assert.Throws<InvalidOperationException>(() => factory.Create((LlmTask)123)).Message);
        Assert.Empty(httpFactory.Names);
    }

    [Fact]
    public void Whitespace_is_preserved_like_python_environment_values()
    {
        using var httpFactory = new RecordingHttpFactory();
        var options = AiOptions.FromConfiguration(Configuration(new() { ["GROQ_API_KEY"] = "k", ["AI_ANSWER_MODEL"] = " " }));
        Assert.Equal(" ", new LlmClientFactory(options, httpFactory, NullLogger<GroqClient>.Instance).Create(LlmTask.Answer).Model);
        options = AiOptions.FromConfiguration(Configuration(new() { ["GROQ_API_KEY"] = "k", ["ANSWERER"] = " " }));
        Assert.Equal("Unknown ANSWERER:  ", Assert.Throws<InvalidOperationException>(() =>
            new LlmClientFactory(options, httpFactory, NullLogger<GroqClient>.Instance).Create(LlmTask.Answer)).Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Registration_and_factory_resolution_without_a_key_succeed_until_create(string? key)
    {
        var services = new ServiceCollection();
        services.AddGroqAi(Configuration(new() { ["GROQ_API_KEY"] = key }));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var factory = provider.GetRequiredService<ILlmClientFactory>();
        Assert.Same(factory, provider.GetRequiredService<ILlmClientFactory>());
        Assert.Equal("GROQ_API_KEY is not set", Assert.Throws<InvalidOperationException>(() => factory.Create(LlmTask.Answer)).Message);
    }

    [Fact]
    public async Task Registration_uses_flat_configuration_singleton_options_and_the_named_http_client()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new() { ["GROQ_API_KEY"] = "configured-key", ["AI_ANSWER_MODEL"] = "configured-model" });
        services.AddGroqAi(configuration);
        var handler = new GroqTestHandler((_, _) => GroqClientTests.ChatReply());
        services.AddHttpClient("Groq").ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AiOptions>();
        Assert.Same(options, provider.GetRequiredService<AiOptions>());
        Assert.Equal("configured-key", options.GroqApiKey);
        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Groq");
        Assert.Equal(new Uri("https://api.groq.com/openai/v1/"), http.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(300), http.Timeout);
        Assert.Null(http.DefaultRequestHeaders.Authorization);

        var factory = provider.GetRequiredService<ILlmClientFactory>();
        var llm = factory.Create(LlmTask.Answer);
        Assert.Equal("configured-model", llm.Model);
        await llm.CompleteJsonAsync("s", [new("user", "q")], new JsonObject { ["type"] = "object" });
        var sent = Assert.Single(handler.Requests);
        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", sent.Uri);
        Assert.Equal("Bearer configured-key", sent.Authorization);
    }

    [Fact]
    public void Factory_creates_a_fresh_groq_client_per_create_from_the_named_http_client()
    {
        using var httpFactory = new RecordingHttpFactory();
        var factory = new LlmClientFactory(new AiOptions { GroqApiKey = "k" }, httpFactory, NullLogger<GroqClient>.Instance);
        Assert.Empty(httpFactory.Names);
        foreach (var task in Enum.GetValues<LlmTask>()) factory.Create(task);
        Assert.Equal(["Groq", "Groq", "Groq"], httpFactory.Names);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private sealed class RecordingHttpFactory : IHttpClientFactory, IDisposable
    {
        private readonly HttpClient _http = GroqClientTests.NewHttp(new GroqTestHandler((_, _) => throw new InvalidOperationException("Unexpected HTTP request")));
        public List<string> Names { get; } = [];
        public HttpClient CreateClient(string name) { Names.Add(name); return _http; }
        public void Dispose() => _http.Dispose();
    }
}
