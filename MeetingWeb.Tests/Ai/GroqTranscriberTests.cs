using System.Text;
using MeetingWeb.Services.Ai;
using Microsoft.Extensions.Configuration;

namespace MeetingWeb.Tests.Ai;

public class GroqTranscriberTests
{
    private const string NamedVtt = "WEBVTT\n\n00:00:00.000 --> 00:00:04.000\nBucky Barnes: Merhaba";
    private const string OffsetVtt = "WEBVTT\n\n1\n00:00:01.000 --> 00:00:03.000\nAli: bir\n\n2\n00:01:01.000 --> 00:01:03.000\nAyşe: iki";

    [Fact]
    public async Task Segmented_transcription_drops_prompt_and_known_hallucinations()
    {
        var client = new FakeClient { Segments = [new(0, 1, "Altyazı M.K."), new(1, 2, TranscriptText.OldTechInstruction), new(2, 3, "Gerçek sözler burada.")] };
        var audio = new FakeAudio();
        Assert.Equal("Bucky Barnes: Gerçek sözler burada.", await new GroqTranscriber(client, audio, "whisper").TranscribeAsync("input.wav", NamedVtt, default));
        AssertCleaned(audio);
    }

    [Fact]
    public async Task Segmented_transcription_keeps_real_speech_that_looks_like_the_hint_or_a_caption()
    {
        var client = new FakeClient
        {
            Segments = [new(0, 1, "Docker."), new(1, 2, "Docker, Kubernetes, GitHub."), new(2, 3, "Altyazı desteğini ekleyelim."), new(3, 4, "Altyazı M.K.")]
        };
        var audio = new FakeAudio();
        Assert.Equal("Bucky Barnes: Docker. Docker, Kubernetes, GitHub. Altyazı desteğini ekleyelim.",
            await new GroqTranscriber(client, audio, "whisper").TranscribeAsync("input.wav", NamedVtt, default));
        AssertCleaned(audio);
    }

    [Theory]
    [InlineData("Gerçek konuşma. Altyazı M.K.", "Gerçek konuşma.")]
    [InlineData("Gerçek konuşma. İzlediğiniz için teşekkür ederim.", "Gerçek konuşma.")]
    [InlineData("Bunu izlediğiniz için teşekkür ederim dedi ve devam etti.", "Bunu izlediğiniz için teşekkür ederim dedi ve devam etti.")]
    [InlineData(TranscriptText.OldTechInstruction, "")]
    [InlineData(GroqTranscriber.TechPrompt, "")]
    public async Task Plain_transcription_cleans_only_prompt_chunks_and_final_captions(string text, string expected)
    {
        var client = new FakeClient { Plain = new Queue<string>([text]) };
        var audio = new FakeAudio();
        Assert.Equal(expected, await new GroqTranscriber(client, audio, "whisper").TranscribeAsync("input.wav", null, default));
        Assert.False(Assert.Single(client.Calls).Segmented);
        AssertCleaned(audio);
    }

    [Fact]
    public void Tech_prompt_is_a_term_list_not_an_instruction_and_matches_python_bytes()
    {
        Assert.Contains("backend", GroqTranscriber.TechPrompt);
        Assert.Contains("CI/CD", GroqTranscriber.TechPrompt);
        Assert.DoesNotContain("İngilizce", GroqTranscriber.TechPrompt);
        Assert.DoesNotContain(":", GroqTranscriber.TechPrompt);
        Assert.Equal(Encoding.UTF8.GetBytes(TranscriptTextTests.Golden("transcriber_tech_prompt.txt")), Encoding.UTF8.GetBytes(GroqTranscriber.TechPrompt));
    }

    [Theory]
    [InlineData(OffsetVtt, "Ali: Merhaba\nAyşe: Merhaba", true)]
    [InlineData(null, "Merhaba Merhaba", false)]
    [InlineData("WEBVTT\n\n00:00:01.000 --> 00:00:03.000\nadsız", "Merhaba Merhaba", false)]
    [InlineData("", "Merhaba Merhaba", false)]
    public async Task Chunk_offsets_are_added_before_matching(string? vtt, string expected, bool segmented)
    {
        var client = new FakeClient { Plain = new Queue<string>(["Merhaba", "Merhaba"]), Segments = [new(1, 3, "Merhaba")] };
        var audio = new FakeAudio { Duration = 120 };
        Assert.Equal(expected, await new GroqTranscriber(client, audio, "test").TranscribeAsync("input.wav", vtt, default));
        Assert.Equal(new[] { (0.0, 60.0), (60.0, 120.0) }, audio.Cuts.Select(c => (c.Start, c.End)));
        Assert.Equal(2, client.Calls.Count);
        Assert.All(client.Calls, call => Assert.Equal(segmented, call.Segmented));
        AssertCleaned(audio);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Chunks_are_awaited_in_order_and_receive_model_language_prompt_and_token(bool named)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient { Gate = gate.Task, Plain = new Queue<string>(["bir", "iki"]), Segments = [new(1, 3, "Merhaba")] };
        var audio = new FakeAudio { Duration = 120 };
        using var cts = new CancellationTokenSource();
        var transcriber = new GroqTranscriber(client, audio, "custom-model", language: "en");
        var running = transcriber.TranscribeAsync("source audio.wav", named ? OffsetVtt : null, cts.Token);
        await client.FirstCall.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(client.Calls);
        Assert.Single(audio.Cuts);
        Assert.False(running.IsCompleted);
        gate.SetResult();
        Assert.Equal(named ? "Ali: Merhaba\nAyşe: Merhaba" : "bir iki", await running);
        Assert.Equal("source audio.wav", audio.Source);
        Assert.Equal(2, client.Calls.Count);
        for (var i = 0; i < client.Calls.Count; i++)
        {
            var call = client.Calls[i];
            Assert.Equal($"part{i}.flac", Path.GetFileName(call.Path));
            Assert.Equal(audio.Cuts[i].Target, call.Path);
            Assert.Equal("custom-model", call.Model);
            Assert.Equal("en", call.Language);
            Assert.Equal(GroqTranscriber.TechPrompt, call.Prompt);
            Assert.Equal(cts.Token, call.Token);
            Assert.Equal(named, call.Segmented);
        }
        Assert.All(audio.Tokens, token => Assert.Equal(cts.Token, token));
        AssertCleaned(audio);
    }

    [Fact]
    public async Task Custom_chunk_length_uses_real_cut_planner_and_skips_empty_parts()
    {
        var client = new FakeClient { Plain = new Queue<string>([GroqTranscriber.TechPrompt, "Gerçek konuşma.", "Altyazı M.K."]) };
        var audio = new FakeAudio { Duration = 90, Silences = [29, 61] };
        Assert.Equal("Gerçek konuşma.", await new GroqTranscriber(client, audio, "whisper", chunkSeconds: 30).TranscribeAsync("input", null, default));
        Assert.Equal(new[] { (0.0, 29.0), (29.0, 61.0), (61.0, 90.0) }, audio.Cuts.Select(c => (c.Start, c.End)));
        Assert.All(client.Calls, call => Assert.Equal("tr", call.Language));
        AssertCleaned(audio);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Temporary_directory_is_removed_when_the_client_throws(bool named)
    {
        var expected = new InvalidOperationException("client failed");
        var audio = new FakeAudio();
        var client = new FakeClient { Error = expected };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new GroqTranscriber(client, audio, "m").TranscribeAsync("input", named ? NamedVtt : null, default));
        Assert.Same(expected, error);
        AssertCleaned(audio);
    }

    [Fact]
    public async Task Temporary_directory_is_removed_when_audio_preparation_throws()
    {
        var audio = new FakeAudio { Error = new AudioToolException("ffmpeg failed: exit code 1") };
        await Assert.ThrowsAsync<AudioToolException>(() => new GroqTranscriber(new FakeClient(), audio, "m").TranscribeAsync("input", null, default));
        AssertCleaned(audio);
    }

    [Fact]
    public async Task Cancellation_reaches_the_client_and_removes_temporary_files()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var audio = new FakeAudio();
        var client = new FakeClient { Error = new OperationCanceledException(cts.Token) };
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GroqTranscriber(client, audio, "m").TranscribeAsync("input", null, cts.Token));
        Assert.Equal(cts.Token, error.CancellationToken);
        AssertCleaned(audio);
    }

    [Theory]
    [InlineData(null, "whisper-large-v3")]
    [InlineData("", "whisper-large-v3")]
    [InlineData("custom-whisper", "custom-whisper")]
    [InlineData(" ", " ")]
    public void Transcribe_model_uses_configuration_with_empty_value_default(string? configured, string expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AI_TRANSCRIBE_MODEL"] = configured }).Build();
        Assert.Equal(expected, AiOptions.FromConfiguration(configuration).TranscribeModel);
        Assert.Equal("whisper-large-v3", new AiOptions().TranscribeModel);
    }

    private static void AssertCleaned(FakeAudio audio)
    {
        Assert.NotNull(audio.Mono);
        var directory = Path.GetDirectoryName(audio.Mono)!;
        Assert.StartsWith("briefyn_stt_", Path.GetFileName(directory));
        Assert.False(Directory.Exists(directory));
    }

    private sealed class FakeAudio : IAudioTools
    {
        public double Duration { get; init; } = 60;
        public IReadOnlyList<double> Silences { get; init; } = [];
        public Exception? Error { get; init; }
        public string? Source { get; private set; }
        public string? Mono { get; private set; }
        public List<(double Start, double End, string Target)> Cuts { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public async Task<string> ToMonoFlacAsync(string source, string target, CancellationToken ct)
        {
            Tokens.Add(ct);
            Source = source;
            Mono = target;
            await File.WriteAllTextAsync(target, "mono");
            if (Error != null) throw Error;
            return target;
        }

        public Task<double> DurationSecondsAsync(string audio, CancellationToken ct)
        {
            Tokens.Add(ct);
            Assert.Equal(Mono, audio);
            return Task.FromResult(Duration);
        }

        public Task<IReadOnlyList<double>> SilenceMidpointsAsync(string audio, CancellationToken ct)
        {
            Tokens.Add(ct);
            Assert.Equal(Mono, audio);
            return Task.FromResult(Silences);
        }

        public Task<string> CutAsync(string audio, double start, double end, string target, CancellationToken ct)
        {
            Tokens.Add(ct);
            Assert.Equal(Mono, audio);
            File.WriteAllText(target, "part");
            Cuts.Add((start, end, target));
            return Task.FromResult(target);
        }
    }

    private sealed record Call(string Path, string Model, string Language, string Prompt, CancellationToken Token, bool Segmented);

    private sealed class FakeClient : ITranscriptionClient
    {
        public Queue<string> Plain { get; init; } = new();
        public IReadOnlyList<TranscriptSegment> Segments { get; init; } = [];
        public Exception? Error { get; init; }
        public Task Gate { get; init; } = Task.CompletedTask;
        public List<Call> Calls { get; } = [];
        public TaskCompletionSource FirstCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private async Task RecordAsync(string path, string model, string language, string prompt, CancellationToken ct, bool segmented)
        {
            Assert.True(File.Exists(path));
            Calls.Add(new Call(path, model, language, prompt, ct, segmented));
            FirstCall.TrySetResult();
            if (Error != null) throw Error;
            await Gate;
        }

        public async Task<string> TranscribeAsync(string audioPath, string model, string language, string prompt, CancellationToken ct)
        {
            await RecordAsync(audioPath, model, language, prompt, ct, false);
            return Plain.Dequeue();
        }

        public async Task<IReadOnlyList<TranscriptSegment>> TranscribeSegmentsAsync(string audioPath, string model, string language, string prompt, CancellationToken ct)
        {
            await RecordAsync(audioPath, model, language, prompt, ct, true);
            return Segments;
        }
    }
}
