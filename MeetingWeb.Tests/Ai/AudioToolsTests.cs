using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MeetingWeb.Services.Ai;

namespace MeetingWeb.Tests.Ai;

public class AudioToolsTests
{
    [Fact]
    public void Cuts_land_on_silences_near_the_target() =>
        Assert.Equal(new[] { 0.0, 59.3, 120.8, 179.5, 234.0 }, FfmpegAudioTools.PlanCuts(234, [59.3, 120.8, 179.5]));

    [Fact]
    public void Cuts_fall_back_to_fixed_length_without_silences() =>
        Assert.Equal(new[] { 0.0, 60.0, 120.0, 150.0 }, FfmpegAudioTools.PlanCuts(150, []));

    [Fact]
    public void Short_audio_is_one_piece() =>
        Assert.Equal(new[] { 0.0, 40.0 }, FfmpegAudioTools.PlanCuts(40, [20]));

    [Theory]
    [InlineData(65, 55, 65)]
    [InlineData(55, 65, 55)]
    public void Equidistant_silences_keep_the_first(double first, double second, double expected) =>
        Assert.Equal(new[] { 0.0, expected, 100.0 }, FfmpegAudioTools.PlanCuts(100, [first, second]));

    [Fact]
    public void Cuts_round_to_even_at_three_places() =>
        Assert.Equal(new[] { 0.0, 60.062, 120.188, 180.125 }, FfmpegAudioTools.PlanCuts(180.125, [60.0625, 120.1875]));

    [Fact]
    public void Cut_just_above_five_seconds_is_checked_before_rounding() =>
        Assert.Equal(new[] { 0.0, 5.0, 12.0 }, FfmpegAudioTools.PlanCuts(12, [5.0001], 7.5));

    [Fact]
    public void Cut_at_exactly_five_seconds_is_not_appended()
    {
        // Python retries the same mark forever here. Stop on the second enumeration so the test cannot hang.
        Assert.Throws<RetryDetectedException>(() => FfmpegAudioTools.PlanCuts(12, new RetryGuardSilences(), 7.5));
    }

    private sealed class RetryDetectedException : Exception;

    private sealed class RetryGuardSilences : IReadOnlyList<double>
    {
        private bool _enumerated;
        public int Count => 1;
        public double this[int index] => index == 0 ? 5.0 : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<double> GetEnumerator()
        {
            if (_enumerated) throw new RetryDetectedException();
            _enumerated = true;
            yield return 5.0;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static IEnumerable<object[]> GoldenCases()
    {
        var golden = JsonSerializer.Deserialize<JsonElement>(TranscriptTextTests.Golden("audio_plan_cuts.json"));
        return golden.GetProperty("inputs").EnumerateArray().Select((input, index) => new object[]
        {
            index, input, golden.GetProperty("expected")[index]
        }).ToArray();
    }

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void Plan_cuts_matches_python_exactly(int index, JsonElement input, JsonElement expected)
    {
        Assert.InRange(index, 0, 14);
        Assert.Equal(expected.EnumerateArray().Select(value => value.GetDouble()), FfmpegAudioTools.PlanCuts(
            input.GetProperty("duration").GetDouble(),
            input.GetProperty("silences").EnumerateArray().Select(value => value.GetDouble()).ToArray(),
            input.GetProperty("target").GetDouble()));
    }
}

[Trait("Category", "Ffmpeg")]
public class FfmpegAudioToolsTests
{
    [Fact]
    public async Task Real_audio_with_quoted_paths_is_converted_measured_and_cut()
    {
        var work = CreateWork();
        try
        {
            var source = Path.Combine(work, "kayıt 1.wav");
            await RunFfmpeg("-y", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
                "-f", "lavfi", "-i", "anullsrc=r=16000:cl=mono:d=1",
                "-f", "lavfi", "-i", "sine=frequency=660:duration=3",
                "-filter_complex", "[0:a][1:a][2:a]concat=n=3:v=0:a=1[out]", "-map", "[out]", source);
            var audio = new FfmpegAudioTools();
            var mono = Path.Combine(work, "mono.flac");
            Assert.Equal(mono, await audio.ToMonoFlacAsync(source, mono, default));
            Assert.InRange(await audio.DurationSecondsAsync(mono, default), 6.8, 7.2);
            Assert.Contains(await audio.SilenceMidpointsAsync(mono, default), point => point is >= 3 and <= 4);
            var piece = Path.Combine(work, "parça.flac");
            Assert.Equal(piece, await audio.CutAsync(mono, 0, 2, piece, default));
            Assert.InRange(await audio.DurationSecondsAsync(piece, default), 1.8, 2.2);
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Fact]
    public async Task Missing_program_is_wrapped_without_paths()
    {
        var error = await Assert.ThrowsAsync<AudioToolException>(() =>
            new FfmpegAudioTools(ffmpegPath: "briefyn-yok-ffmpeg").ToMonoFlacAsync("secret input", "secret output", default));
        Assert.Equal("ffmpeg is not installed", error.Message);
        Assert.IsType<Win32Exception>(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeout_or_cancellation_kills_the_started_process(bool cancel)
    {
        var work = CreateWork();
        try
        {
            var pidFile = Path.Combine(work, "pid");
            var executable = WriteProgram(work, $"echo $$ > {Quote(pidFile)}\nexec sleep 30\n");
            using var cts = new CancellationTokenSource();
            var audio = new FfmpegAudioTools(ffmpegPath: executable, longTimeout: TimeSpan.FromMilliseconds(cancel ? 10000 : 300));
            var running = audio.ToMonoFlacAsync("input", "output", cts.Token);
            if (cancel)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (!File.Exists(pidFile)) await Task.Delay(10, deadline.Token);
                cts.Cancel();
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
                Assert.Equal(cts.Token, error.CancellationToken);
            }
            else
            {
                var error = await Assert.ThrowsAsync<AudioToolException>(() => running);
                Assert.Equal("ffmpeg timed out", error.Message);
            }
            var pid = int.Parse(await File.ReadAllTextAsync(pidFile), CultureInfo.InvariantCulture);
            try
            {
                using var process = Process.GetProcessById(pid);
                Assert.True(process.HasExited, $"Process {pid} survived termination");
            }
            catch (ArgumentException) { /* Process already reaped. */ }
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Theory]
    [InlineData("N/A", 0)]
    [InlineData("7.0", 9)]
    public async Task Invalid_duration_or_exit_code_is_private(string output, int exitCode)
    {
        var work = CreateWork();
        try
        {
            var executable = WriteProgram(work, $"echo '{output}'\necho 'private path and stderr' >&2\nexit {exitCode}\n");
            var audio = new FfmpegAudioTools(ffprobePath: executable);
            var error = await Assert.ThrowsAsync<AudioToolException>(() => audio.DurationSecondsAsync("private file", default));
            Assert.Equal($"ffprobe failed: exit code {exitCode}", error.Message);
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Fact]
    public async Task Duration_uses_python_strip_and_invariant_float_parsing()
    {
        var work = CreateWork();
        try
        {
            var executable = WriteProgram(work, "printf '\\0341.25e1\\037'\n");
            Assert.Equal(12.5, await new FfmpegAudioTools(ffprobePath: executable).DurationSecondsAsync("audio", default));
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Fact]
    public async Task Commands_preserve_argument_order_paths_and_invariant_numbers()
    {
        var work = CreateWork();
        try
        {
            var argumentsFile = Path.Combine(work, "arguments");
            var executable = WriteProgram(work, $"printf '%s\\n' \"$@\" > {Quote(argumentsFile)}\nprintf '7.0'\n");
            var audio = new FfmpegAudioTools(ffmpegPath: executable, ffprobePath: executable);
            var input = Path.Combine(work, "kayıt 1.wav");
            var output = Path.Combine(work, "hedef '2' \"3\"; x.flac");
            await audio.ToMonoFlacAsync(input, output, default);
            Assert.Equal(new[] { "-y", "-loglevel", "error", "-i", input, "-ac", "1", "-ar", "16000", output }, await File.ReadAllLinesAsync(argumentsFile));
            await audio.DurationSecondsAsync(input, default);
            Assert.Equal(new[] { "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", input }, await File.ReadAllLinesAsync(argumentsFile));
            await audio.SilenceMidpointsAsync(input, default);
            Assert.Equal(new[] { "-i", input, "-af", "silencedetect=noise=-35dB:d=0.4", "-f", "null", "-" }, await File.ReadAllLinesAsync(argumentsFile));
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                await audio.CutAsync(input, 1.25, 3.75, output, default);
            }
            finally { CultureInfo.CurrentCulture = culture; }
            Assert.Equal(new[] { "-y", "-loglevel", "error", "-ss", "1.25", "-t", "2.5", "-i", input, output }, await File.ReadAllLinesAsync(argumentsFile));
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Fact]
    public async Task Silence_pairs_use_the_shorter_list_and_ignore_nonzero_exit_while_draining_both_streams()
    {
        var work = CreateWork();
        try
        {
            var executable = WriteProgram(work,
                "dd if=/dev/zero bs=1024 count=256 2>/dev/null\n" +
                "dd if=/dev/zero bs=1024 count=256 >&2 2>/dev/null\n" +
                "echo 'silence_start: 1.2 silence_start: 8.4 silence_start: 20' >&2\n" +
                "echo 'silence_end: 2.6 silence_end: 10.8' >&2\nexit 7\n");
            var audio = new FfmpegAudioTools(ffmpegPath: executable, longTimeout: TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { (1.2 + 2.6) / 2, (8.4 + 10.8) / 2 }, await audio.SilenceMidpointsAsync("audio", default));
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conversion_and_cut_fail_without_exposing_stderr(bool cut)
    {
        var work = CreateWork();
        try
        {
            var executable = WriteProgram(work, "echo 'private filename' >&2\nexit 11\n");
            var audio = new FfmpegAudioTools(ffmpegPath: executable);
            var error = await Assert.ThrowsAsync<AudioToolException>(() => cut
                ? audio.CutAsync("private input", 0, 2, "private output", default)
                : audio.ToMonoFlacAsync("private input", "private output", default));
            Assert.Equal("ffmpeg failed: exit code 11", error.Message);
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    private static string CreateWork()
    {
        var work = Path.Combine(Path.GetTempPath(), "briefyn_audio_test_" + Guid.NewGuid().ToString("N") + " ses 'a' \"b\"; x");
        Directory.CreateDirectory(work);
        return work;
    }

    private static string Quote(string text) => "'" + text.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static string WriteProgram(string work, string body)
    {
        var path = Path.Combine(work, "fake-tool");
        File.WriteAllText(path, "#!/bin/sh\n" + body);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static async Task RunFfmpeg(params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("ffmpeg")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try { process.Start(); }
        catch (Win32Exception)
        {
            Assert.Fail("ffmpeg kurulu değil: brew install ffmpeg / apt-get install ffmpeg");
        }
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        await Task.WhenAll(stdout, stderr);
        Assert.True(process.ExitCode == 0, await stderr);
    }
}
