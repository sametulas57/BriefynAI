// test_answer_endpoint_needs_the_key is not ported: no inter-service key exists inside .NET.
// The extra JSON field rejection case has no counterpart in the typed IAiEngine request.
using System.Net;
using System.Text.Json;
using MeetingWeb.Services.Ai;
using MeetingWeb.Services.ArchiveSearch;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static MeetingWeb.Tests.Ai.ArchiveAnswererTests;

namespace MeetingWeb.Tests.Ai;

public class DotNetAiEngineAnswerTests
{
    [Fact]
    public async Task Answer_endpoint_returns_the_checked_answer()
    {
        var llm = new FakeLlm();
        var factory = new FakeFactory(llm);
        using var logs = new LoggerFactory();
        var engine = new DotNetAiEngine(factory, logs.CreateLogger<DotNetAiEngine>());
        EqualJson(Good, await engine.AnswerAsync(" \n" + Question + "\t ", Parts));
        Assert.Equal(LlmTask.Answer, Assert.Single(factory.Tasks));
        using var payload = JsonDocument.Parse(Assert.Single(llm.Messages!).Content);
        Assert.Equal(Question, payload.RootElement.GetProperty("soru").GetString());
    }

    public static IEnumerable<object[]> BadInputs()
    {
        yield return [new string('x', 501), Parts];
        yield return [string.Concat(Enumerable.Repeat("😀", 501)), Parts];
        yield return ["", Parts];
        yield return [" \t\n ", Parts];
        foreach (var part in new[]
        {
            Parts[0] with { Id = "../etc" }, Parts[0] with { Id = "T1-R1\n" },
            Parts[0] with { Id = "T1234567890-R1" }, Parts[0] with { Id = "T1-R12345" },
            Parts[0] with { Id = "T1-X1" }, Parts[0] with { Id = "T-R1" },
            Parts[0] with { Tarih = "2026-1-01" }, Parts[0] with { Tarih = "2026-01-01\n" },
            Parts[0] with { Metin = "" }, Parts[0] with { Metin = new string('x', 4001) },
            Parts[0] with { Metin = string.Concat(Enumerable.Repeat("😀", 4001)) },
            Parts[0] with { Toplanti = new string('x', 301) }, Parts[0] with { Tur = new string('x', 21) }
        }) yield return [Question, new[] { part }];
        yield return [Question, Enumerable.Repeat(Parts[0], 41).ToArray()];
        yield return [Question, Enumerable.Repeat(Parts[0] with { Metin = new string('x', 3000) }, 12).ToArray()];
        yield return [Question, Enumerable.Repeat(Parts[0] with { Metin = new string('x', 4000) }, 6)
            .Append(Parts[0] with { Metin = "x" }).ToArray()];
    }

    [Theory]
    [MemberData(nameof(BadInputs))]
    public async Task Answer_endpoint_rejects_bad_input_before_the_model(string question, AiAnswerPassage[] parts)
    {
        var factory = new FakeFactory(new FakeLlm());
        using var logs = new LoggerFactory();
        var engine = new DotNetAiEngine(factory, logs.CreateLogger<DotNetAiEngine>());
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => engine.AnswerAsync(question, parts));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, error.StatusCode);
        Assert.Equal("AI engine rejected the request", error.Message);
        Assert.Null(error.InnerException);
        Assert.Empty(factory.Tasks);
    }

    public static IEnumerable<object[]> BoundaryInputs()
    {
        yield return [new string('x', 500), Parts];
        yield return [string.Concat(Enumerable.Repeat("😀", 500)), Parts];
        yield return [Question, Enumerable.Repeat(Parts[0], 40).ToArray()];
        yield return [Question, Enumerable.Repeat(Parts[0] with { Metin = new string('x', 4000) }, 6).ToArray()];
        yield return [Question, Enumerable.Repeat(Parts[0] with { Metin = string.Concat(Enumerable.Repeat("😀", 4000)) }, 6).ToArray()];
        yield return [Question, new[] { Parts[0] with { Id = "T123456789-D1234", Toplanti = string.Concat(Enumerable.Repeat("😀", 300)), Tur = string.Concat(Enumerable.Repeat("😀", 20)), Metin = "x" } }];
        yield return [Question, new[] { Parts[0] with { Toplanti = "", Tur = "", Tarih = "9999-99-99" } }];
    }

    [Theory]
    [MemberData(nameof(BoundaryInputs))]
    public async Task Answer_endpoint_accepts_boundaries_counting_unicode_code_points(string question, AiAnswerPassage[] parts)
    {
        var llm = new FakeLlm();
        using var logs = new LoggerFactory();
        await new DotNetAiEngine(new FakeFactory(llm), logs.CreateLogger<DotNetAiEngine>()).AnswerAsync(question, parts);
        Assert.Equal(1, llm.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_failure_is_a_plain_busy_reply_and_nothing_private_is_logged(bool missingKey)
    {
        var capture = new CapturingLoggerProvider();
        using var logs = new LoggerFactory([capture]);
        var llm = new FakeLlm { Error = new IOException(Question + Parts[1].Metin + Good) };
        var factory = new FakeFactory(llm) { Error = missingKey ? new InvalidOperationException(Question + Good) : null };
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new DotNetAiEngine(factory, logs.CreateLogger<DotNetAiEngine>()).AnswerAsync(Question, Parts));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Equal("AI engine answer failed", error.Message);
        Assert.Null(error.InnerException);
        Assert.Equal("Answer failed: " + (missingKey ? "InvalidOperationException" : "IOException"), Assert.Single(capture.Lines).Trim());
        AssertPrivateAbsent(capture, error.ToString());
    }

    [Fact]
    public async Task Successful_answer_logs_nothing_private()
    {
        var capture = new CapturingLoggerProvider();
        using var logs = new LoggerFactory([capture]);
        EqualJson(Good, await new DotNetAiEngine(new FakeFactory(new FakeLlm()), logs.CreateLogger<DotNetAiEngine>()).AnswerAsync(Question, Parts));
        AssertPrivateAbsent(capture);
        Assert.Empty(capture.Lines);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancellation_only_propagates_when_the_callers_token_is_cancelled(bool cancelled)
    {
        using var source = new CancellationTokenSource();
        if (cancelled) source.Cancel();
        var llm = new FakeLlm { Error = new OperationCanceledException("private", source.Token) };
        var capture = new CapturingLoggerProvider();
        using var logs = new LoggerFactory([capture]);
        var engine = new DotNetAiEngine(new FakeFactory(llm), logs.CreateLogger<DotNetAiEngine>());
        if (cancelled)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.AnswerAsync(Question, Parts, source.Token));
            Assert.Empty(capture.Lines);
        }
        else
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Assert.ThrowsAsync<HttpRequestException>(() => engine.AnswerAsync(Question, Parts, source.Token))).StatusCode);
        Assert.Equal(source.Token, llm.Token);
    }

    [Theory]
    [InlineData(false, AskStatus.Answered)]
    [InlineData(true, AskStatus.Busy)]
    public async Task AskService_and_dotnet_engine_work_together(bool failure, AskStatus status)
    {
        var capture = new CapturingLoggerProvider();
        using var logs = new LoggerFactory([capture]);
        var llm = new FakeLlm { Error = failure ? new IOException(Question + Good) : null };
        var engine = new DotNetAiEngine(new FakeFactory(llm), logs.CreateLogger<DotNetAiEngine>());
        var settings = Options.Create(new AskOptions());
        var ask = new AskService(new FakeSearch(), new AskRateLimiter(TimeProvider.System, settings), engine, settings, logs.CreateLogger<AskService>());
        var result = await ask.AskAsync("user", 1, new AskRequest(Question));
        Assert.Equal(status, result.Status);
        if (failure)
        {
            Assert.Null(result.Answer);
            Assert.Empty(result.Sources);
            Assert.Contains(capture.Lines, line => line.Trim() == "Archive answer failed: HTTP 503");
        }
        else
        {
            Assert.Equal("Aylık fiyat 129 TL.", result.Answer);
            Assert.Equal("T3-R1", Assert.Single(result.Sources).PassageId);
        }
        AssertPrivateAbsent(capture);
    }

    private static void AssertPrivateAbsent(CapturingLoggerProvider logs, string extra = "")
    {
        var text = string.Join("\n", logs.Lines) + extra;
        foreach (var secret in new[] { Question, Parts[0].Metin, Parts[1].Metin, "Aylık fiyat 129 TL.", "Ayşe Kaya" })
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

    private sealed class FakeSearch : IArchiveSearchService
    {
        public Task<ArchiveSearchResult> SearchAsync(string? userId, int workspaceId, ArchiveQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ArchiveSearchResult(1, [new("T3-R1", 3, Parts[0].Toplanti, new DateOnly(2026, 9, 21), ArchivePassageKind.Decision, Parts[0].Metin, 1)]));
    }
}
