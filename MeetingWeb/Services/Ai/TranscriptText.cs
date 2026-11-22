using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MeetingWeb.Services.Ai;

public sealed record Cue(double Start, double End, string? Speaker, string Text);

public static class TranscriptText
{
    public const string OldTechInstruction = "İngilizce teknik terimleri orijinal yazımıyla koru";
    public static IReadOnlyList<string> HallucinatedPhrases { get; } = Array.AsReadOnly(new[]
    {
        "Altyazı M.K.", "İzlediğiniz için teşekkür ederim", "İzlediğiniz için teşekkürler", "Abone olmayı unutmayın"
    });

    private static readonly Regex Sentences = new(@"[^.!?…]+[.!?…]*");
    // Python's Unicode \s also includes the four information separators.
    private static readonly Regex Blocks = new(@"\n[\s\u001c-\u001f]*\n");
    private static readonly Regex Times = new(@"\A(\d{2}:)?\d{2}:\d{2}\.\d{3} --> (\d{2}:)?\d{2}:\d{2}\.\d{3}(?:[\s\u001c-\u001f]+.*)?\z");

    public static string NormaliseText(string text)
    {
        var folded = PyText.CaseFold(text.Replace('I', 'ı').Replace('İ', 'i')).Normalize(NormalizationForm.FormKD);
        var result = new StringBuilder();
        foreach (var rune in folded.EnumerateRunes())
        {
            if (HasCombiningClass(rune.Value)) continue;
            result.Append(PyText.IsAlnum(rune) ? rune.ToString() : " ");
        }
        return string.Join(" ", PyText.SplitWhitespace(result.ToString()));
    }

    public static bool IsPromptEcho(string text, string prompt)
    {
        var candidate = NormaliseText(text);
        var hint = NormaliseText(prompt);
        var echo = PyText.SplitWhitespace(candidate).Count >= 6 && (" " + hint + " ").Contains(" " + candidate + " ", StringComparison.Ordinal);
        return candidate.Length > 0 && (candidate == NormaliseText(OldTechInstruction) || candidate == hint || echo);
    }

    public static bool IsHallucinatedCaption(string text)
    {
        var candidate = NormaliseText(text);
        return candidate.Length > 0 && HallucinatedPhrases.Any(phrase => candidate == NormaliseText(phrase));
    }

    public static bool IsOnlyHallucinatedCaptions(string text, IReadOnlySet<string>? speakers = null)
    {
        var lines = PyText.SplitLines(text).Select(line =>
        {
            var colon = line.IndexOf(": ", StringComparison.Ordinal);
            return colon >= 0 && colon + 2 < line.Length && speakers?.Contains(line[..colon]) == true ? line[(colon + 2)..] : line;
        });
        var remaining = NormaliseText(string.Join(" ", lines)).Replace(" ", "");
        var phrases = HallucinatedPhrases.Select(phrase => NormaliseText(phrase).Replace(" ", "")).ToArray();
        while (remaining.Length > 0)
        {
            var match = phrases.FirstOrDefault(phrase => remaining.StartsWith(phrase, StringComparison.Ordinal));
            if (match == null) return false;
            remaining = remaining[match.Length..];
        }
        return true;
    }

    public static string CleanSegmentText(string text, string prompt)
    {
        text = PyText.Strip(text);
        return IsPromptEcho(text, prompt) || IsHallucinatedCaption(text) ? "" : text;
    }

    public static string CleanPlainChunk(string text, string prompt)
    {
        text = PyText.Strip(text);
        if (text.Length == 0 || IsPromptEcho(text, prompt)) return "";
        foreach (var span in Sentences.Matches(text).TakeLast(2).Reverse())
            if (IsHallucinatedCaption(text[span.Index..])) return PyText.RStrip(text[..span.Index]);
        return text;
    }

    public static IReadOnlyList<Cue> ParseVtt(string text)
    {
        List<Cue> cues = [];
        foreach (var block in Blocks.Split(text.Replace("\r\n", "\n").TrimStart('\ufeff')))
        {
            var lines = PyText.SplitLines(block).Select(PyText.Strip).Where(line => line.Length > 0).ToArray();
            var timing = Array.FindIndex(lines, 0, Math.Min(2, lines.Length), line => Times.IsMatch(line));
            if (timing < 0 || timing + 1 >= lines.Length) continue;
            var times = lines[timing].Split(" --> ", 2, StringSplitOptions.None);
            if (!TrySeconds(times[0], out var start) || !TrySeconds(PyText.SplitWhitespace(times[1])[0], out var end) || end <= start) continue;
            var first = lines[timing + 1];
            var colon = first.IndexOf(": ", StringComparison.Ordinal);
            var speaker = colon >= 0 ? first[..colon] : null;
            var firstText = colon >= 0 ? first[(colon + 2)..] : first;
            var spoken = PyText.Strip(string.Join(" ", new[] { firstText }.Concat(lines.Skip(timing + 2))));
            cues.Add(new Cue(start, end, string.IsNullOrEmpty(speaker) ? null : speaker, spoken));
        }
        return cues;
    }

    private static bool TrySeconds(string value, out double seconds)
    {
        // Python int/float accept Unicode decimal digits; .NET's invariant parser accepts ASCII only.
        var ascii = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
            ascii.Append(Rune.GetUnicodeCategory(rune) == UnicodeCategory.DecimalDigitNumber
                ? ((int)Rune.GetNumericValue(rune)).ToString(CultureInfo.InvariantCulture) : rune.ToString());
        var parts = ascii.ToString().Split(':');
        seconds = 0;
        if (parts.Length is not (2 or 3)) return false;
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], i == parts.Length - 1 ? NumberStyles.AllowDecimalPoint : NumberStyles.None,
                CultureInfo.InvariantCulture, out var number)) return false;
            seconds = seconds * 60 + number;
        }
        return true;
    }

    public static IReadOnlyList<(string? Speaker, string Text)> AssignSpeakers(IReadOnlyList<TranscriptSegment> segments, IReadOnlyList<Cue> cues)
    {
        List<(string? Speaker, string Text)> pairs = [];
        foreach (var segment in segments)
        {
            // A list admits null names and preserves Python dict insertion order when overlaps tie.
            List<(string? Speaker, double Seconds)> overlap = [];
            foreach (var cue in cues)
            {
                var seconds = Math.Max(0, Math.Min(segment.End, cue.End) - Math.Max(segment.Start, cue.Start));
                if (seconds <= 0) continue;
                var index = overlap.FindIndex(pair => pair.Speaker == cue.Speaker);
                if (index < 0) overlap.Add((cue.Speaker, seconds));
                else overlap[index] = (cue.Speaker, overlap[index].Seconds + seconds);
            }
            string? speaker = null;
            var best = 0.0;
            foreach (var item in overlap)
                if (item.Seconds > best) { best = item.Seconds; speaker = item.Speaker; }
            pairs.Add((speaker, segment.Text));
        }
        return pairs;
    }

    public static string FormatLines(IReadOnlyList<(string? Speaker, string Text)> pairs)
    {
        List<(string? Speaker, string Text)> lines = [];
        foreach (var (speaker, raw) in pairs)
        {
            var text = PyText.Strip(raw);
            if (text.Length == 0) continue;
            if (lines.Count > 0 && lines[^1].Speaker == speaker) lines[^1] = (speaker, lines[^1].Text + " " + text);
            else lines.Add((speaker, text));
        }
        return string.Join("\n", lines.Select(pair => string.IsNullOrEmpty(pair.Speaker) ? pair.Text : $"{pair.Speaker}: {pair.Text}"));
    }

    // Unicode 15.0 nonzero canonical combining classes, matching Python unicodedata.
    // https://www.unicode.org/Public/15.0.0/ucd/extracted/DerivedCombiningClass.txt
    // Mn/Mc/Me alone would incorrectly remove class-zero marks (e.g. U+034F or U+093E).
    private static readonly (int Start, int End)[] CombiningRanges =
    [
        (0x0300, 0x034e),
        (0x0350, 0x036f),
        (0x0483, 0x0487),
        (0x0591, 0x05bd),
        (0x05bf, 0x05bf),
        (0x05c1, 0x05c2),
        (0x05c4, 0x05c5),
        (0x05c7, 0x05c7),
        (0x0610, 0x061a),
        (0x064b, 0x065f),
        (0x0670, 0x0670),
        (0x06d6, 0x06dc),
        (0x06df, 0x06e4),
        (0x06e7, 0x06e8),
        (0x06ea, 0x06ed),
        (0x0711, 0x0711),
        (0x0730, 0x074a),
        (0x07eb, 0x07f3),
        (0x07fd, 0x07fd),
        (0x0816, 0x0819),
        (0x081b, 0x0823),
        (0x0825, 0x0827),
        (0x0829, 0x082d),
        (0x0859, 0x085b),
        (0x0898, 0x089f),
        (0x08ca, 0x08e1),
        (0x08e3, 0x08ff),
        (0x093c, 0x093c),
        (0x094d, 0x094d),
        (0x0951, 0x0954),
        (0x09bc, 0x09bc),
        (0x09cd, 0x09cd),
        (0x09fe, 0x09fe),
        (0x0a3c, 0x0a3c),
        (0x0a4d, 0x0a4d),
        (0x0abc, 0x0abc),
        (0x0acd, 0x0acd),
        (0x0b3c, 0x0b3c),
        (0x0b4d, 0x0b4d),
        (0x0bcd, 0x0bcd),
        (0x0c3c, 0x0c3c),
        (0x0c4d, 0x0c4d),
        (0x0c55, 0x0c56),
        (0x0cbc, 0x0cbc),
        (0x0ccd, 0x0ccd),
        (0x0d3b, 0x0d3c),
        (0x0d4d, 0x0d4d),
        (0x0dca, 0x0dca),
        (0x0e38, 0x0e3a),
        (0x0e48, 0x0e4b),
        (0x0eb8, 0x0eba),
        (0x0ec8, 0x0ecb),
        (0x0f18, 0x0f19),
        (0x0f35, 0x0f35),
        (0x0f37, 0x0f37),
        (0x0f39, 0x0f39),
        (0x0f71, 0x0f72),
        (0x0f74, 0x0f74),
        (0x0f7a, 0x0f7d),
        (0x0f80, 0x0f80),
        (0x0f82, 0x0f84),
        (0x0f86, 0x0f87),
        (0x0fc6, 0x0fc6),
        (0x1037, 0x1037),
        (0x1039, 0x103a),
        (0x108d, 0x108d),
        (0x135d, 0x135f),
        (0x1714, 0x1715),
        (0x1734, 0x1734),
        (0x17d2, 0x17d2),
        (0x17dd, 0x17dd),
        (0x18a9, 0x18a9),
        (0x1939, 0x193b),
        (0x1a17, 0x1a18),
        (0x1a60, 0x1a60),
        (0x1a75, 0x1a7c),
        (0x1a7f, 0x1a7f),
        (0x1ab0, 0x1abd),
        (0x1abf, 0x1ace),
        (0x1b34, 0x1b34),
        (0x1b44, 0x1b44),
        (0x1b6b, 0x1b73),
        (0x1baa, 0x1bab),
        (0x1be6, 0x1be6),
        (0x1bf2, 0x1bf3),
        (0x1c37, 0x1c37),
        (0x1cd0, 0x1cd2),
        (0x1cd4, 0x1ce0),
        (0x1ce2, 0x1ce8),
        (0x1ced, 0x1ced),
        (0x1cf4, 0x1cf4),
        (0x1cf8, 0x1cf9),
        (0x1dc0, 0x1dff),
        (0x20d0, 0x20dc),
        (0x20e1, 0x20e1),
        (0x20e5, 0x20f0),
        (0x2cef, 0x2cf1),
        (0x2d7f, 0x2d7f),
        (0x2de0, 0x2dff),
        (0x302a, 0x302f),
        (0x3099, 0x309a),
        (0xa66f, 0xa66f),
        (0xa674, 0xa67d),
        (0xa69e, 0xa69f),
        (0xa6f0, 0xa6f1),
        (0xa806, 0xa806),
        (0xa82c, 0xa82c),
        (0xa8c4, 0xa8c4),
        (0xa8e0, 0xa8f1),
        (0xa92b, 0xa92d),
        (0xa953, 0xa953),
        (0xa9b3, 0xa9b3),
        (0xa9c0, 0xa9c0),
        (0xaab0, 0xaab0),
        (0xaab2, 0xaab4),
        (0xaab7, 0xaab8),
        (0xaabe, 0xaabf),
        (0xaac1, 0xaac1),
        (0xaaf6, 0xaaf6),
        (0xabed, 0xabed),
        (0xfb1e, 0xfb1e),
        (0xfe20, 0xfe2f),
        (0x101fd, 0x101fd),
        (0x102e0, 0x102e0),
        (0x10376, 0x1037a),
        (0x10a0d, 0x10a0d),
        (0x10a0f, 0x10a0f),
        (0x10a38, 0x10a3a),
        (0x10a3f, 0x10a3f),
        (0x10ae5, 0x10ae6),
        (0x10d24, 0x10d27),
        (0x10eab, 0x10eac),
        (0x10efd, 0x10eff),
        (0x10f46, 0x10f50),
        (0x10f82, 0x10f85),
        (0x11046, 0x11046),
        (0x11070, 0x11070),
        (0x1107f, 0x1107f),
        (0x110b9, 0x110ba),
        (0x11100, 0x11102),
        (0x11133, 0x11134),
        (0x11173, 0x11173),
        (0x111c0, 0x111c0),
        (0x111ca, 0x111ca),
        (0x11235, 0x11236),
        (0x112e9, 0x112ea),
        (0x1133b, 0x1133c),
        (0x1134d, 0x1134d),
        (0x11366, 0x1136c),
        (0x11370, 0x11374),
        (0x11442, 0x11442),
        (0x11446, 0x11446),
        (0x1145e, 0x1145e),
        (0x114c2, 0x114c3),
        (0x115bf, 0x115c0),
        (0x1163f, 0x1163f),
        (0x116b6, 0x116b7),
        (0x1172b, 0x1172b),
        (0x11839, 0x1183a),
        (0x1193d, 0x1193e),
        (0x11943, 0x11943),
        (0x119e0, 0x119e0),
        (0x11a34, 0x11a34),
        (0x11a47, 0x11a47),
        (0x11a99, 0x11a99),
        (0x11c3f, 0x11c3f),
        (0x11d42, 0x11d42),
        (0x11d44, 0x11d45),
        (0x11d97, 0x11d97),
        (0x11f41, 0x11f42),
        (0x16af0, 0x16af4),
        (0x16b30, 0x16b36),
        (0x16ff0, 0x16ff1),
        (0x1bc9e, 0x1bc9e),
        (0x1d165, 0x1d169),
        (0x1d16d, 0x1d172),
        (0x1d17b, 0x1d182),
        (0x1d185, 0x1d18b),
        (0x1d1aa, 0x1d1ad),
        (0x1d242, 0x1d244),
        (0x1e000, 0x1e006),
        (0x1e008, 0x1e018),
        (0x1e01b, 0x1e021),
        (0x1e023, 0x1e024),
        (0x1e026, 0x1e02a),
        (0x1e08f, 0x1e08f),
        (0x1e130, 0x1e136),
        (0x1e2ae, 0x1e2ae),
        (0x1e2ec, 0x1e2ef),
        (0x1e4ec, 0x1e4ef),
        (0x1e8d0, 0x1e8d6),
        (0x1e944, 0x1e94a),
    ];

    private static bool HasCombiningClass(int value)
    {
        var low = 0;
        var high = CombiningRanges.Length - 1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            var range = CombiningRanges[mid];
            if (value < range.Start) high = mid - 1;
            else if (value > range.End) low = mid + 1;
            else return true;
        }
        return false;
    }
}
