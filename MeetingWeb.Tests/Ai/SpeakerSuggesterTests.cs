using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeetingWeb.Services.Ai;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetingWeb.Tests.Ai;

public class SpeakerSuggesterTests
{
    public static IEnumerable<object[]> Cases()
    {
        var inputs = JsonSerializer.Deserialize<JsonElement>(TranscriptTextTests.Golden("speaker_inputs.json"));
        var expected = JsonSerializer.Deserialize<JsonElement>(TranscriptTextTests.Golden("speaker_expected.json"));
        return inputs.EnumerateArray().Select((input, i) => new object[]
        {
            input.GetProperty("op").GetString() + "/" + input.GetProperty("id").GetString(), input, expected[i]
        }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Matches_python_golden(string name, JsonElement input, JsonElement expected)
    {
        var transcript = input.GetProperty("transcript").GetString()!;
        if (input.GetProperty("op").GetString() == "labels")
        {
            Assert.Equal(expected.EnumerateArray().Select(e => e.GetString()), SpeakerSuggester.Labels(transcript));
            return;
        }
        var llm = new MeetingSummarizerTests.FakeLlm { Reply = _ => input.GetProperty("reply").GetRawText() };
        using var source = new CancellationTokenSource();
        var suggester = new SpeakerSuggester(llm, input.GetProperty("max_chars").GetInt32());
        if (expected.GetProperty("error").GetBoolean())
            await Assert.ThrowsAsync<JsonException>(() => suggester.SuggestAsync(transcript, source.Token));
        else
        {
            var result = await suggester.SuggestAsync(transcript, source.Token);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetProperty("result").GetRawText()), JsonSerializer.SerializeToNode(result)), name);
            Assert.Equal(expected.GetProperty("result").EnumerateObject().Select(p => p.Name), result.Keys);
        }
        var content = expected.GetProperty("content");
        if (content.ValueKind == JsonValueKind.Null) Assert.Empty(llm.Calls);
        else
        {
            var call = Assert.Single(llm.Calls);
            Assert.Equal(new LlmMessage("user", content.GetString()!), Assert.Single(call.Messages));
            Assert.Equal(SpeakerSuggester.System, call.System);
            Assert.Equal(PythonJson.Dumps(SpeakerSuggester.Schema()), PythonJson.Dumps(call.Schema));
            Assert.Equal(source.Token, call.Token);
        }
    }

    [Fact]
    public void Prompt_and_schema_match_python_bytes()
    {
        Assert.Equal(Encoding.UTF8.GetBytes(TranscriptTextTests.Golden("speaker_system.txt")), Encoding.UTF8.GetBytes(SpeakerSuggester.System));
        Assert.Equal(Encoding.UTF8.GetBytes(TranscriptTextTests.Golden("speaker_schema.json")), Encoding.UTF8.GetBytes(PythonJson.Dumps(SpeakerSuggester.Schema())));
        Assert.Contains("hitap edilen kişi", SpeakerSuggester.System);
        var schema = SpeakerSuggester.Schema();
        schema["type"] = "number";
        Assert.Equal("object", SpeakerSuggester.Schema()["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Engine_builds_the_speakers_task_and_returns_proposals()
    {
        var fake = new MeetingSummarizerTests.FakeLlm
        {
            Reply = _ => """{"oneriler":[{"konusmaci":"Konuşmacı 2","ad":"Murat","gerekce":"ipucu"}]}"""
        };
        var factory = new FakeFactory { Client = fake };
        var engine = new DotNetAiEngine(factory, null!, NullLogger<DotNetAiEngine>.Instance);
        var result = await engine.SuggestSpeakerNamesAsync(Transcript);
        Assert.Equal("Murat", Assert.Single(result).Value.Ad);
        Assert.Equal(new[] { LlmTask.Speakers }, factory.Tasks);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Engine_returns_empty_on_factory_or_provider_failure_without_logging_content(bool factoryFails)
    {
        var error = new InvalidOperationException(Transcript + " secret provider response");
        var fake = new MeetingSummarizerTests.FakeLlm { Error = error };
        var factory = new FakeFactory { Client = fake, Error = factoryFails ? error : null };
        using var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var engine = new DotNetAiEngine(factory, null!, loggerFactory.CreateLogger<DotNetAiEngine>());
        Assert.Empty(await engine.SuggestSpeakerNamesAsync(Transcript));
        Assert.Equal("Speaker name suggestions failed; continuing without proposals. ", Assert.Single(logs.Lines));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    public async Task Engine_returns_empty_for_non_object_model_results(string reply)
    {
        var engine = new DotNetAiEngine(new FakeFactory { Client = new MeetingSummarizerTests.FakeLlm { Reply = _ => reply } }, null!, NullLogger<DotNetAiEngine>.Instance);
        Assert.Empty(await engine.SuggestSpeakerNamesAsync(Transcript));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Engine_propagates_requested_cancellation_even_during_factory_creation(bool factoryFails)
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var error = new OperationCanceledException(Transcript, source.Token);
        var factory = new FakeFactory
        {
            Client = new MeetingSummarizerTests.FakeLlm { Error = error },
            Error = factoryFails ? error : null
        };
        using var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var engine = new DotNetAiEngine(factory, null!, loggerFactory.CreateLogger<DotNetAiEngine>());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.SuggestSpeakerNamesAsync(Transcript, source.Token));
        Assert.Empty(logs.Lines);
    }

    [Fact]
    public async Task Engine_treats_unrequested_cancellation_as_optional_failure()
    {
        var factory = new FakeFactory { Client = new MeetingSummarizerTests.FakeLlm { Error = new OperationCanceledException(Transcript) } };
        var engine = new DotNetAiEngine(factory, null!, NullLogger<DotNetAiEngine>.Instance);
        Assert.Empty(await engine.SuggestSpeakerNamesAsync(Transcript));
    }

    [Fact]
    public void Shared_python_operations_apply_to_answer_and_summary_paths()
    {
        Assert.Equal(new[] { "iş." }, MeetingSummarizer.SplitText("\x1ciş\x1f", 10));
        var summary = MeetingSummarizer.ToBackendFormat(JsonSerializer.Deserialize<JsonElement>(
            """{"toplanti_konusu":"\u001cİş\u001f","aksiyon_maddeleri":[{"ne":"\u001cyap\u001f"}]}"""));
        Assert.Equal("İş", summary.ToplantiKonusu);
        Assert.Equal(new[] { "yap" }, summary.AksiyonMaddeleri);
        var answer = ArchiveAnswerer.Checked(JsonSerializer.Deserialize<JsonElement>(
            """{"yeterli_bilgi":true,"kanitlar":[{"id":"T1-K1","alinti":"\u001c\u001f"}],"cevap":"evet"}"""), new HashSet<string> { "T1-K1" });
        Assert.False(answer.GetProperty("yeterli_bilgi").GetBoolean());
    }

    [Fact]
    public async Task Engine_rejects_python_whitespace_only_question()
    {
        var factory = new FakeFactory { Client = new MeetingSummarizerTests.FakeLlm() };
        var engine = new DotNetAiEngine(factory, null!, NullLogger<DotNetAiEngine>.Instance);
        await Assert.ThrowsAsync<HttpRequestException>(() => engine.AnswerAsync("\x1c\x1f", []));
        Assert.Empty(factory.Tasks);
    }

    private const string Transcript = "Konuşmacı 1: Murat, durum ne?\nKonuşmacı 2: İyi.";

    private sealed class FakeFactory : ILlmClientFactory
    {
        public required ILlmClient Client { get; init; }
        public Exception? Error { get; init; }
        public List<LlmTask> Tasks { get; } = [];
        public ILlmClient Create(LlmTask task)
        {
            Tasks.Add(task);
            if (Error != null) throw Error;
            return Client;
        }
    }
}
