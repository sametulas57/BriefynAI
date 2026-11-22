using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MeetingWeb.Services.Ai;

public interface IAudioTools
{
    Task<string> ToMonoFlacAsync(string source, string target, CancellationToken ct);
    Task<double> DurationSecondsAsync(string audio, CancellationToken ct);
    Task<IReadOnlyList<double>> SilenceMidpointsAsync(string audio, CancellationToken ct);
    Task<string> CutAsync(string audio, double start, double end, string target, CancellationToken ct);
}

public sealed class AudioToolException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class FfmpegAudioTools(string ffmpegPath = "ffmpeg", string ffprobePath = "ffprobe",
    TimeSpan? longTimeout = null, TimeSpan? shortTimeout = null) : IAudioTools
{
    private readonly TimeSpan _longTimeout = longTimeout ?? TimeSpan.FromMinutes(30);
    private readonly TimeSpan _shortTimeout = shortTimeout ?? TimeSpan.FromMinutes(5);
    private static readonly Regex SilenceStarts = new(@"silence_start: ([\d.]+)");
    private static readonly Regex SilenceEnds = new(@"silence_end: ([\d.]+)");

    public async Task<string> ToMonoFlacAsync(string source, string target, CancellationToken ct)
    {
        await RunAsync(ffmpegPath, "ffmpeg", ["-y", "-loglevel", "error", "-i", source, "-ac", "1", "-ar", "16000", target], _longTimeout, true, ct);
        return target;
    }

    public async Task<double> DurationSecondsAsync(string audio, CancellationToken ct)
    {
        var result = await RunAsync(ffprobePath, "ffprobe", ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", audio], _shortTimeout, true, ct);
        try { return double.Parse(PyText.Strip(result.Output), NumberStyles.Float, CultureInfo.InvariantCulture); }
        catch (FormatException) { throw new AudioToolException($"ffprobe failed: exit code {result.ExitCode}"); }
    }

    public async Task<IReadOnlyList<double>> SilenceMidpointsAsync(string audio, CancellationToken ct)
    {
        var result = await RunAsync(ffmpegPath, "ffmpeg", ["-i", audio, "-af", "silencedetect=noise=-35dB:d=0.4", "-f", "null", "-"], _longTimeout, false, ct);
        var starts = SilenceStarts.Matches(result.Error).Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        var ends = SilenceEnds.Matches(result.Error).Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        return starts.Zip(ends, (start, end) => (start + end) / 2).ToArray();
    }

    public static IReadOnlyList<double> PlanCuts(double duration, IReadOnlyList<double> silences, double target = 60.0)
    {
        List<double> cuts = [0.0];
        var mark = target;
        while (mark < duration - target / 3)
        {
            var nearest = mark;
            var first = true;
            foreach (var silence in silences)
                if (first || Math.Abs(silence - mark) < Math.Abs(nearest - mark))
                {
                    nearest = silence;
                    first = false;
                }
            var cut = Math.Abs(nearest - mark) <= target / 3 ? nearest : mark;
            if (cut > cuts[^1] + 5) cuts.Add(Math.Round(cut, 3, MidpointRounding.ToEven));
            mark = cuts[^1] + target;
        }
        cuts.Add(duration);
        return cuts;
    }

    public async Task<string> CutAsync(string audio, double start, double end, string target, CancellationToken ct)
    {
        // Round-trip invariant numbers may use 0 instead of Python's 0.0; ffmpeg reads both identically.
        await RunAsync(ffmpegPath, "ffmpeg", ["-y", "-loglevel", "error", "-ss", start.ToString("R", CultureInfo.InvariantCulture),
            "-t", (end - start).ToString("R", CultureInfo.InvariantCulture), "-i", audio, target], _shortTimeout, true, ct);
        return target;
    }

    private static async Task<(string Output, string Error, int ExitCode)> RunAsync(string path, string tool,
        IReadOnlyList<string> arguments, TimeSpan timeout, bool checkExitCode, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try { process.Start(); }
        catch (Win32Exception error) { throw new AudioToolException($"{tool} is not installed", error); }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try { await process.WaitForExitAsync(linked.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* The process exited while cancellation was being delivered. */ }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            ct.ThrowIfCancellationRequested();
            throw new AudioToolException($"{tool} timed out");
        }
        await Task.WhenAll(stdout, stderr);
        if (checkExitCode && process.ExitCode != 0) throw new AudioToolException($"{tool} failed: exit code {process.ExitCode}");
        return (await stdout, await stderr, process.ExitCode);
    }
}
