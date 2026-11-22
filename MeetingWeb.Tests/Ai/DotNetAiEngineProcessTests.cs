// Ports process-meeting behavior from test_api_security.py and the Zoom/provider routing test from test_azure_transcriber.py.
// Not ported: request_without_key, request_with_wrong_key, service_fails_closed_when_no_key_is_configured,
// and the key part of summarize_endpoint_requires_the_key_and_returns_a_summary: no inter-service key in .NET.
// oversized_upload and client_file_name_never_reaches_the_file_system belong to web upload/AudioStorage:
// the engine reads the existing GUID-named audio file directly, without an HTTP upload or temporary copy.
// llm_failure_is_reported_as_failure_not_as_an_empty_summary is already covered at the engine boundary by
// DotNetAiEngineSummaryTests.Summary_failure_returns_500_and_logs_only_the_error_type (no direct provider-failure test in MeetingSummarizerTests).
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeetingWeb.Services.Ai;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.Extensions.Logging;

namespace MeetingWeb.Tests.Ai;

public class DotNetAiEngineProcessTests
{
    private const string Transcript = "Konuşmacı 1: Murat, durum ne?\nKonuşmacı 2: İyi.";
    private const string Vtt = "\ufeffWEBVTT\n\n00:00.000 --> 00:02.000\nBucky Barnes: merhaba\n\n00:02.000 --> 00:04.000\nAli Veli: evet";

    [Fact]
    public async Task Correct_input_is_accepted()
    {
        using var h = new Harness();
        var result = await h.Engine.ProcessMeetingAsync("recording.wav", null);
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(Transcript, result.Transkript);
        Assert.Equal("x", result.Summary!.ToplantiKonusu);
        Assert.Equal(new[] { "transcribe", "Speakers", "Summary" }, h.Order);
        Assert.DoesNotContain("recording.wav", string.Join("\n", h.Capture.Lines));
        Assert.DoesNotContain(Transcript, string.Join("\n", h.Capture.Lines));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Process_meeting_returns_speaker_proposals_even_if_summary_fails(bool summaryFails)
    {
        using var h = new Harness();
        if (summaryFails) h.Llms.Summary.Error = new IOException("private transcript content");
        var result = (await h.Engine.ProcessMeetingAsync("recording.wav", null))!;
        Assert.Equal(!summaryFails, result.Success);
        Assert.Equal(Transcript, result.Transkript);
        Assert.Equal(summaryFails, result.Summary is null);
        Assert.Equal("Murat", Assert.Single(result.KonusmaciOnerileri!).Value.Ad);
        var suggestion = Assert.Single(h.Llms.Speakers.Calls);
        Assert.Equal("Konuşmacılar: Konuşmacı 1, Konuşmacı 2\n\nToplantı dökümü:\n\n" + Transcript,
            Assert.Single(suggestion.Messages).Content);
        Assert.Equal(new[] { LlmTask.Speakers, LlmTask.Summary }, h.Llms.Tasks);
        AssertNoPrivateLogs(h);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Speaker_suggester_failure_does_not_fail_processing(bool failInFactory)
    {
        using var h = new Harness();
        var error = new InvalidOperationException("private transcript content");
        if (failInFactory) { h.Llms.ErrorTask = LlmTask.Speakers; h.Llms.Error = error; }
        else h.Llms.Speakers.Error = error;
        var result = (await h.Engine.ProcessMeetingAsync("recording.wav", null))!;
        Assert.True(result.Success);
        Assert.Empty(result.KonusmaciOnerileri!);
        Assert.Contains(LogLevel.Error, h.Logger.Levels);
        AssertNoPrivateLogs(h);
    }

    [Fact]
    public async Task Zoom_speakers_skip_name_suggestions()
    {
        using var h = new Harness();
        h.Transcribers.Text = "Bucky Barnes: Merhaba.";
        var result = (await h.Engine.ProcessMeetingAsync("recording.wav", await h.SpeakersFile(Vtt)))!;
        Assert.True(result.Success);
        Assert.Empty(result.KonusmaciOnerileri!);
        Assert.Empty(h.Llms.Speakers.Calls);
        Assert.Equal(LlmTask.Summary, Assert.Single(h.Llms.Tasks));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n ")]
    [InlineData("Altyazı M.K.")]
    [InlineData("  altyazı   m k !! ")]
    [InlineData("İZLEDİĞİNİZ İÇİN TEŞEKKÜRLER!")]
    [InlineData("İzlediğiniz için teşekkür ederim.")]
    [InlineData("Altyazı M.K. İzlediğiniz için teşekkürler!")]
    public async Task No_speech_returns_distinct_response(string? transcript)
    {
        using var h = new Harness();
        h.Transcribers.Text = transcript;
        await Assert.ThrowsAsync<NoSpeechException>(() => h.Engine.ProcessMeetingAsync("recording.wav", null));
        Assert.Empty(h.Llms.Tasks);
        Assert.DoesNotContain(LogLevel.Error, h.Logger.Levels);
    }

    [Fact]
    public async Task Caption_with_real_speech_is_processed()
    {
        using var h = new Harness();
        h.Transcribers.Text = "Altyazı M.K. Bugün toplantıyı açıyorum.";
        var result = (await h.Engine.ProcessMeetingAsync("recording.wav", null))!;
        Assert.True(result.Success);
        Assert.Equal(h.Transcribers.Text, result.Transkript);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Optional_speakers_are_passed_to_transcriber(bool withSpeakers)
    {
        using var h = new Harness();
        var vtt = withSpeakers ? "WEBVTT\n\nAli: söz" : null;
        var file = vtt == null ? null : await h.SpeakersFile(vtt);
        await h.Engine.ProcessMeetingAsync("recording.wav", file);
        Assert.Equal(withSpeakers ? "groq" : null, Assert.Single(h.Transcribers.Kinds));
        var input = Assert.Single(h.Transcribers.Inputs);
        Assert.Equal("recording.wav", input.Path);
        Assert.Equal(vtt, input.Vtt);
    }

    [Theory]
    [InlineData(2 * 1024 * 1024, false)]
    [InlineData(2 * 1024 * 1024 + 1, true)]
    public async Task Speakers_over_two_megabytes_are_rejected(int bytes, bool tooLarge)
    {
        using var h = new Harness();
        var data = new byte[bytes];
        Array.Fill(data, (byte)'a');
        var file = await h.SpeakersFile(data);
        if (tooLarge)
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => h.Engine.ProcessMeetingAsync("recording.wav", file));
            Assert.Equal("Speaker file is too large.", error.Message);
            Assert.Empty(h.Transcribers.Kinds);
            Assert.Empty(h.Llms.Tasks);
        }
        else
        {
            Assert.True((await h.Engine.ProcessMeetingAsync("recording.wav", file))!.Success);
            Assert.Equal(bytes, Assert.Single(h.Transcribers.Inputs).Vtt!.Length);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_utf8_or_empty_speakers_are_ignored(bool empty)
    {
        using var h = new Harness();
        var file = await h.SpeakersFile(empty ? [] : [0xff]);
        Assert.True((await h.Engine.ProcessMeetingAsync("recording.wav", file))!.Success);
        Assert.Null(Assert.Single(h.Transcribers.Kinds));
        Assert.Null(Assert.Single(h.Transcribers.Inputs).Vtt);
        Assert.Single(h.Llms.Speakers.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Summary_failure_still_returns_the_transcript_without_logging_it(bool failInFactory)
    {
        using var h = new Harness();
        var error = new IOException("private transcript content");
        if (failInFactory) { h.Llms.ErrorTask = LlmTask.Summary; h.Llms.Error = error; }
        else h.Llms.Summary.Error = error;
        var result = (await h.Engine.ProcessMeetingAsync("recording.wav", null))!;
        Assert.False(result.Success);
        Assert.Null(result.Summary);
        Assert.Equal(Transcript, result.Transkript);
        Assert.Equal("Murat", Assert.Single(result.KonusmaciOnerileri!).Value.Ad);
        Assert.Contains(LogLevel.Error, h.Logger.Levels);
        Assert.Contains(h.Capture.Lines, line => line.Trim() == "Summarization failed; returning the transcript without a summary: IOException");
        AssertNoPrivateLogs(h);
    }

    [Theory]
    [InlineData("Bucky Barnes: Altyazı M.K. İzlediğiniz için teşekkürler!")]
    [InlineData("Bucky Barnes: Altyazı M.K.\nAli Veli: İzlediğiniz için teşekkürler!")]
    public async Task No_speech_with_zoom_speaker_names(string transcript)
    {
        using var h = new Harness();
        h.Transcribers.Text = transcript;
        var file = await h.SpeakersFile(Vtt);
        await Assert.ThrowsAsync<NoSpeechException>(() => h.Engine.ProcessMeetingAsync("recording.wav", file));
        Assert.Empty(h.Llms.Tasks);
        Assert.Equal(Vtt, Assert.Single(h.Transcribers.Inputs).Vtt);
    }

    [Fact]
    public async Task Speaker_named_like_a_caption_still_keeps_real_speech()
    {
        using var h = new Harness();
        h.Transcribers.Text = "Bucky Barnes: Altyazı M.K. Bütçeyi konuşalım.";
        var result = (await h.Engine.ProcessMeetingAsync("recording.wav", await h.SpeakersFile(Vtt)))!;
        Assert.True(result.Success);
        Assert.Equal(h.Transcribers.Text, result.Transkript);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Internal_errors_are_not_leaked(bool failInFactory)
    {
        using var h = new Harness();
        h.Transcribers.Error = new InvalidDataException("db parolasi: hunter2");
        h.Transcribers.FailInFactory = failInFactory;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Engine.ProcessMeetingAsync("recording.wav", null));
        Assert.Equal("AI engine processing failed.", error.Message);
        Assert.DoesNotContain("hunter2", error.ToString());
        Assert.Null(error.InnerException);
        Assert.Contains(LogLevel.Error, h.Logger.Levels);
        Assert.Empty(h.Llms.Tasks);
    }

    [Fact]
    public async Task Missing_speaker_file_is_wrapped_without_a_path_or_inner_exception()
    {
        using var h = new Harness();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Engine.ProcessMeetingAsync("recording.wav", "/private/missing-names.vtt"));
        Assert.Equal("AI engine processing failed.", error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("missing-names", error.ToString());
        Assert.Empty(h.Transcribers.Kinds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Api_keeps_zoom_on_groq_and_uses_configured_provider_for_uploads(bool withVtt)
    {
        using var h = new Harness();
        h.Transcribers.Configured = "azure";
        await h.Engine.ProcessMeetingAsync("recording.wav", withVtt ? await h.SpeakersFile(Vtt) : null);
        Assert.Equal(withVtt ? "groq" : null, Assert.Single(h.Transcribers.Kinds));
        Assert.Equal(withVtt ? "groq" : "azure", Assert.Single(h.Transcribers.Selected));
    }

    [Theory]
    [InlineData("transcriber-factory")]
    [InlineData("transcribe")]
    [InlineData("speakers-factory")]
    [InlineData("speakers")]
    [InlineData("summary-factory")]
    [InlineData("summary")]
    public async Task Requested_cancellation_propagates_without_wrapping(string stage)
    {
        using var h = new Harness();
        using var source = new CancellationTokenSource();
        var original = new OperationCanceledException("private transcript content", source.Token);
        if (stage.StartsWith("transcrib", StringComparison.Ordinal))
        {
            h.Transcribers.BeforeFailure = source.Cancel;
            h.Transcribers.Error = original;
            h.Transcribers.FailInFactory = stage == "transcriber-factory";
        }
        else if (stage.EndsWith("-factory", StringComparison.Ordinal))
        {
            h.Llms.BeforeFailure = source.Cancel;
            h.Llms.ErrorTask = stage == "speakers-factory" ? LlmTask.Speakers : LlmTask.Summary;
            h.Llms.Error = original;
        }
        else
        {
            var llm = stage == "speakers" ? h.Llms.Speakers : h.Llms.Summary;
            llm.BeforeFailure = source.Cancel;
            llm.Error = original;
        }
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Engine.ProcessMeetingAsync("recording.wav", null, source.Token));
        Assert.Same(original, error);
        Assert.DoesNotContain(LogLevel.Error, h.Logger.Levels);
        if (stage == "transcriber-factory") Assert.Empty(h.Transcribers.Inputs);
        else Assert.Equal(source.Token, Assert.Single(h.Transcribers.Inputs).Token);
        AssertNoPrivateLogs(h);
    }

    [Fact]
    public async Task Already_cancelled_token_propagates_during_speaker_read()
    {
        using var h = new Harness();
        var file = await h.SpeakersFile(Vtt);
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Engine.ProcessMeetingAsync("recording.wav", file, source.Token));
        Assert.Empty(h.Transcribers.Kinds);
    }

    private static void AssertNoPrivateLogs(Harness h)
    {
        var logs = string.Join("\n", h.Capture.Lines);
        Assert.DoesNotContain("private transcript content", logs);
        Assert.DoesNotContain(Transcript, logs);
    }

    private sealed class Harness : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("briefyn-process-tests-");
        public List<string> Order { get; } = [];
        public FakeTranscribers Transcribers { get; }
        public FakeLlms Llms { get; }
        public CapturingLoggerProvider Capture { get; } = new();
        private readonly LoggerFactory _logs;
        public RecordingLogger Logger { get; }
        public DotNetAiEngine Engine { get; }
        public Harness()
        {
            Transcribers = new FakeTranscribers(Order);
            Llms = new FakeLlms(Order);
            _logs = new LoggerFactory([Capture]);
            Logger = new RecordingLogger(_logs.CreateLogger<DotNetAiEngine>());
            Engine = new DotNetAiEngine(Llms, Transcribers, Logger);
        }
        public Task<string> SpeakersFile(string text) => SpeakersFile(Encoding.UTF8.GetBytes(text));
        public async Task<string> SpeakersFile(byte[] bytes)
        {
            var file = Path.Combine(_directory.FullName, "names.vtt");
            await File.WriteAllBytesAsync(file, bytes);
            return file;
        }
        public void Dispose() { _logs.Dispose(); _directory.Delete(recursive: true); }
    }

    private sealed class RecordingLogger(ILogger<DotNetAiEngine> inner) : ILogger<DotNetAiEngine>
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);
        public bool IsEnabled(LogLevel level) => inner.IsEnabled(level);
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Levels.Add(level);
            inner.Log(level, id, state, exception, formatter);
        }
    }

    private sealed class FakeTranscribers(List<string> order) : ITranscriberFactory, ITranscriber
    {
        public string? Configured { get; set; }
        public string? Text { get; set; } = Transcript;
        public Exception? Error { get; set; }
        public bool FailInFactory { get; set; }
        public Action? BeforeFailure { get; set; }
        public List<string?> Kinds { get; } = [];
        public List<string> Selected { get; } = [];
        public List<(string Path, string? Vtt, CancellationToken Token)> Inputs { get; } = [];
        public ITranscriber Create(string? kind)
        {
            Kinds.Add(kind);
            Selected.Add(kind ?? Configured ?? "groq");
            if (FailInFactory && Error != null) { BeforeFailure?.Invoke(); throw Error; }
            return this;
        }
        public Task<string> TranscribeAsync(string audioPath, string? speakersVtt, CancellationToken ct)
        {
            order.Add("transcribe");
            Inputs.Add((audioPath, speakersVtt, ct));
            if (Error != null) { BeforeFailure?.Invoke(); throw Error; }
            return Task.FromResult(Text!);
        }
    }

    private sealed class FakeLlms(List<string> order) : ILlmClientFactory
    {
        public FakeLlm Speakers { get; } = new("""{"oneriler":[{"konusmaci":"Konuşmacı 2","ad":"Murat","gerekce":"Cevap veriyor."}]}""");
        public FakeLlm Summary { get; } = new("""{"toplanti_konusu":"x"}""");
        public LlmTask? ErrorTask { get; set; }
        public Exception? Error { get; set; }
        public Action? BeforeFailure { get; set; }
        public List<LlmTask> Tasks { get; } = [];
        public ILlmClient Create(LlmTask task)
        {
            Tasks.Add(task);
            order.Add(task.ToString());
            if (task == ErrorTask && Error != null) { BeforeFailure?.Invoke(); throw Error; }
            return task == LlmTask.Speakers ? Speakers : Summary;
        }
    }

    private sealed class FakeLlm(string reply) : ILlmClient
    {
        public string Model => "fake";
        public Exception? Error { get; set; }
        public Action? BeforeFailure { get; set; }
        public List<(IReadOnlyList<LlmMessage> Messages, CancellationToken Token)> Calls { get; } = [];
        public Task<JsonElement> CompleteJsonAsync(string system, IReadOnlyList<LlmMessage> messages, JsonObject schema, CancellationToken ct = default)
        {
            Calls.Add((messages, ct));
            if (Error != null) { BeforeFailure?.Invoke(); throw Error; }
            return Task.FromResult(JsonSerializer.Deserialize<JsonElement>(reply));
        }
    }
}
