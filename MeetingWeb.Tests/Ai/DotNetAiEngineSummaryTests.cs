using System.Net;
using MeetingWeb.Models;
using MeetingWeb.Services.Ai;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using static MeetingWeb.Tests.Ai.MeetingSummarizerTests;

namespace MeetingWeb.Tests.Ai;

public class DotNetAiEngineSummaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successful_summary_returns_backend_data_and_logs_nothing_private(bool longTranscript)
    {
        var transcript = longTranscript ? string.Concat(Enumerable.Repeat(Transcript, 30)) : Transcript;
        var capture = new CapturingLoggerProvider();
        using var logs = new LoggerFactory([capture]);
        var llm = new FakeLlm();
        var factory = new FakeFactory(llm);
        var engine = new DotNetAiEngine(factory, null!, logs.CreateLogger<DotNetAiEngine>());

        var result = Assert.IsType<AiSummaryData>(await engine.SummarizeAsync(transcript));

        Assert.Equal("İş akışı", result.ToplantiKonusu);
        Assert.Equal(["Ölçüm"], result.GorusulenKonular);
        Assert.Equal(["Çarşamba görüşülecek"], result.AlinanKararlar);
        Assert.Equal(["Ücret yanıtı"], result.BekleyenKonular);
        Assert.Equal(["Şule: taslağı hazırla"], result.AksiyonMaddeleri);
        Assert.Equal(LlmTask.Summary, Assert.Single(factory.Tasks));
        if (longTranscript)
        {
            var count = MeetingSummarizer.SplitText(transcript, 12000).Count;
            Assert.Equal(count + 1, llm.Calls.Count);
            Assert.Equal($"Long transcript: summarising {count} parts, then merging.", Assert.Single(capture.Lines).Trim());
        }
        else
        {
            Assert.Single(llm.Calls);
            Assert.Empty(capture.Lines);
        }
        AssertPrivateAbsent(capture);
    }

    [Fact]
    public async Task Empty_object_still_returns_a_complete_summary()
    {
        var llm = new FakeLlm { Reply = _ => "{}" };
        using var logs = new LoggerFactory();
        var result = Assert.IsType<AiSummaryData>(await new DotNetAiEngine(new FakeFactory(llm), null!, logs.CreateLogger<DotNetAiEngine>()).SummarizeAsync(Transcript));
        Assert.Equal("", result.ToplantiKonusu);
        Assert.Empty(result.GorusulenKonular);
        Assert.Empty(result.AlinanKararlar);
        Assert.Empty(result.BekleyenKonular!);
        Assert.Empty(result.AksiyonMaddeleri);
    }

    [Theory]
    [InlineData("provider", "IOException")]
    [InlineData("key", "InvalidOperationException")]
    [InlineData("json", "JsonException")]
    public async Task Summary_failure_returns_500_and_logs_only_the_error_type(string failure, string expectedType)
    {
        var capture = new CapturingLoggerProvider();
        using var logs = new LoggerFactory([capture]);
        var llm = new FakeLlm
        {
            Error = failure == "provider" ? new IOException(Transcript + Partial) : null,
            Reply = _ => failure == "json" ? """{"toplanti_konusu":null}""" : Partial
        };
        var factory = new FakeFactory(llm) { Error = failure == "key" ? new InvalidOperationException(Transcript + Partial) : null };
        var engine = new DotNetAiEngine(factory, null!, logs.CreateLogger<DotNetAiEngine>());

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => engine.SummarizeAsync(Transcript));

        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
        Assert.Equal("AI engine summary failed", error.Message);
        Assert.Null(error.InnerException);
        Assert.Equal("Summarization failed: " + expectedType, Assert.Single(capture.Lines).Trim());
        Assert.Equal(LlmTask.Summary, Assert.Single(factory.Tasks));
        AssertPrivateAbsent(capture, error.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancellation_only_propagates_when_the_callers_token_is_cancelled(bool cancelled)
    {
        using var source = new CancellationTokenSource();
        if (cancelled) source.Cancel();
        var original = new OperationCanceledException(Transcript + Partial, source.Token);
        var llm = new FakeLlm { Error = original };
        var capture = new CapturingLoggerProvider();
        using var logs = new LoggerFactory([capture]);
        var engine = new DotNetAiEngine(new FakeFactory(llm), null!, logs.CreateLogger<DotNetAiEngine>());
        if (cancelled)
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.SummarizeAsync(Transcript, source.Token));
            Assert.Same(original, error);
            Assert.Empty(capture.Lines);
        }
        else
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => engine.SummarizeAsync(Transcript, source.Token));
            Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
            Assert.Equal("Summarization failed: OperationCanceledException", Assert.Single(capture.Lines).Trim());
            AssertPrivateAbsent(capture, error.ToString());
        }
        Assert.Equal(source.Token, Assert.Single(llm.Calls).Token);
    }

    private static void AssertPrivateAbsent(CapturingLoggerProvider capture, string extra = "")
    {
        var text = string.Join("\n", capture.Lines) + extra;
        foreach (var secret in new[] { Transcript, Partial, "Dr. Ayşe", "taslağı hazırla", "İş akışı", "Ölçüm", "Ücret yanıtı", "Çarşamba görüşülecek" })
            Assert.DoesNotContain(secret, text);
    }

    private sealed class FakeFactory(ILlmClient llm) : ILlmClientFactory
    {
        public List<LlmTask> Tasks { get; } = [];
        public Exception? Error { get; init; }
        public ILlmClient Create(LlmTask task)
        {
            Tasks.Add(task);
            if (Error != null) throw Error;
            return llm;
        }
    }
}
