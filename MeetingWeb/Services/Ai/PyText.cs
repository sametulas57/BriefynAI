using System.Globalization;
using System.Text;

namespace MeetingWeb.Services.Ai;

// Python str operations count Unicode code points and recognize U+001C..U+001F as whitespace.
public static class PyText
{
    public static bool IsSpace(Rune rune) => Rune.IsWhiteSpace(rune) || rune.Value is >= 0x1c and <= 0x1f;

    public static string Strip(string text)
    {
        var start = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!IsSpace(rune)) break;
            start += rune.Utf16SequenceLength;
        }
        return RStrip(text[start..]);
    }

    public static string RStrip(string text)
    {
        var end = 0;
        var position = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            position += rune.Utf16SequenceLength;
            if (!IsSpace(rune)) end = position;
        }
        return text[..end];
    }

    public static IReadOnlyList<string> SplitWhitespace(string text)
    {
        List<string> words = [];
        var start = 0;
        var position = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsSpace(rune))
            {
                if (position > start) words.Add(text[start..position]);
                start = position + rune.Utf16SequenceLength;
            }
            position += rune.Utf16SequenceLength;
        }
        if (position > start) words.Add(text[start..]);
        return words;
    }

    public static IReadOnlyList<string> SplitLines(string text, bool keepEnds = false)
    {
        List<string> lines = [];
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n' or '\v' or '\f' or '\u001c' or '\u001d' or '\u001e' or '\u0085' or '\u2028' or '\u2029')) continue;
            var end = i;
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            lines.Add(text[start..(keepEnds ? i + 1 : end)]);
            start = i + 1;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }

    public static int Length(string text) => text.EnumerateRunes().Count();

    public static string Prefix(string text, int n)
    {
        if (n < 0) n = Math.Max(0, Length(text) + n);
        var end = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (n-- <= 0) break;
            end += rune.Utf16SequenceLength;
        }
        return text[..end];
    }

    public static bool IsAlnum(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or
        UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber or
        UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber;

    public static string CaseFold(string text)
    {
        var result = new StringBuilder();
        foreach (var rune in text.ToLowerInvariant().EnumerateRunes())
            result.Append(rune.Value switch
            {
                // Unicode 15.0 CaseFolding.txt, F mappings for Latin, Greek and Armenian letters:
                // https://www.unicode.org/Public/15.0.0/ucd/CaseFolding.txt
                0x00df => "\u0073\u0073",
                0x0130 => "\u0069\u0307",
                0x0149 => "\u02bc\u006e",
                0x01f0 => "\u006a\u030c",
                0x0390 => "\u03b9\u0308\u0301",
                0x03b0 => "\u03c5\u0308\u0301",
                0x0587 => "\u0565\u0582",
                0x1e96 => "\u0068\u0331",
                0x1e97 => "\u0074\u0308",
                0x1e98 => "\u0077\u030a",
                0x1e99 => "\u0079\u030a",
                0x1e9a => "\u0061\u02be",
                0x1e9e => "\u0073\u0073",
                0x1f50 => "\u03c5\u0313",
                0x1f52 => "\u03c5\u0313\u0300",
                0x1f54 => "\u03c5\u0313\u0301",
                0x1f56 => "\u03c5\u0313\u0342",
                0x1f80 => "\u1f00\u03b9",
                0x1f81 => "\u1f01\u03b9",
                0x1f82 => "\u1f02\u03b9",
                0x1f83 => "\u1f03\u03b9",
                0x1f84 => "\u1f04\u03b9",
                0x1f85 => "\u1f05\u03b9",
                0x1f86 => "\u1f06\u03b9",
                0x1f87 => "\u1f07\u03b9",
                0x1f88 => "\u1f00\u03b9",
                0x1f89 => "\u1f01\u03b9",
                0x1f8a => "\u1f02\u03b9",
                0x1f8b => "\u1f03\u03b9",
                0x1f8c => "\u1f04\u03b9",
                0x1f8d => "\u1f05\u03b9",
                0x1f8e => "\u1f06\u03b9",
                0x1f8f => "\u1f07\u03b9",
                0x1f90 => "\u1f20\u03b9",
                0x1f91 => "\u1f21\u03b9",
                0x1f92 => "\u1f22\u03b9",
                0x1f93 => "\u1f23\u03b9",
                0x1f94 => "\u1f24\u03b9",
                0x1f95 => "\u1f25\u03b9",
                0x1f96 => "\u1f26\u03b9",
                0x1f97 => "\u1f27\u03b9",
                0x1f98 => "\u1f20\u03b9",
                0x1f99 => "\u1f21\u03b9",
                0x1f9a => "\u1f22\u03b9",
                0x1f9b => "\u1f23\u03b9",
                0x1f9c => "\u1f24\u03b9",
                0x1f9d => "\u1f25\u03b9",
                0x1f9e => "\u1f26\u03b9",
                0x1f9f => "\u1f27\u03b9",
                0x1fa0 => "\u1f60\u03b9",
                0x1fa1 => "\u1f61\u03b9",
                0x1fa2 => "\u1f62\u03b9",
                0x1fa3 => "\u1f63\u03b9",
                0x1fa4 => "\u1f64\u03b9",
                0x1fa5 => "\u1f65\u03b9",
                0x1fa6 => "\u1f66\u03b9",
                0x1fa7 => "\u1f67\u03b9",
                0x1fa8 => "\u1f60\u03b9",
                0x1fa9 => "\u1f61\u03b9",
                0x1faa => "\u1f62\u03b9",
                0x1fab => "\u1f63\u03b9",
                0x1fac => "\u1f64\u03b9",
                0x1fad => "\u1f65\u03b9",
                0x1fae => "\u1f66\u03b9",
                0x1faf => "\u1f67\u03b9",
                0x1fb2 => "\u1f70\u03b9",
                0x1fb3 => "\u03b1\u03b9",
                0x1fb4 => "\u03ac\u03b9",
                0x1fb6 => "\u03b1\u0342",
                0x1fb7 => "\u03b1\u0342\u03b9",
                0x1fbc => "\u03b1\u03b9",
                0x1fc2 => "\u1f74\u03b9",
                0x1fc3 => "\u03b7\u03b9",
                0x1fc4 => "\u03ae\u03b9",
                0x1fc6 => "\u03b7\u0342",
                0x1fc7 => "\u03b7\u0342\u03b9",
                0x1fcc => "\u03b7\u03b9",
                0x1fd2 => "\u03b9\u0308\u0300",
                0x1fd3 => "\u03b9\u0308\u0301",
                0x1fd6 => "\u03b9\u0342",
                0x1fd7 => "\u03b9\u0308\u0342",
                0x1fe2 => "\u03c5\u0308\u0300",
                0x1fe3 => "\u03c5\u0308\u0301",
                0x1fe4 => "\u03c1\u0313",
                0x1fe6 => "\u03c5\u0342",
                0x1fe7 => "\u03c5\u0308\u0342",
                0x1ff2 => "\u1f7c\u03b9",
                0x1ff3 => "\u03c9\u03b9",
                0x1ff4 => "\u03ce\u03b9",
                0x1ff6 => "\u03c9\u0342",
                0x1ff7 => "\u03c9\u0342\u03b9",
                0x1ffc => "\u03c9\u03b9",
                0xfb00 => "\u0066\u0066",
                0xfb01 => "\u0066\u0069",
                0xfb02 => "\u0066\u006c",
                0xfb03 => "\u0066\u0066\u0069",
                0xfb04 => "\u0066\u0066\u006c",
                0xfb05 => "\u0073\u0074",
                0xfb06 => "\u0073\u0074",
                0xfb13 => "\u0574\u0576",
                0xfb14 => "\u0574\u0565",
                0xfb15 => "\u0574\u056b",
                0xfb16 => "\u057e\u0576",
                0xfb17 => "\u0574\u056d",
                // Common folds that lowercase alone does not perform; Cherokee folds to uppercase.
                0x00b5 => "\u03bc",
                0x017f => "\u0073",
                0x0345 => "\u03b9",
                0x03d0 => "\u03b2",
                0x03d1 => "\u03b8",
                0x03d5 => "\u03c6",
                0x03d6 => "\u03c0",
                0x03f0 => "\u03ba",
                0x03f1 => "\u03c1",
                0x03f5 => "\u03b5",
                0x1c80 => "\u0432",
                0x1c81 => "\u0434",
                0x1c82 => "\u043e",
                0x1c83 => "\u0441",
                0x1c84 => "\u0442",
                0x1c85 => "\u0442",
                0x1c86 => "\u044a",
                0x1c87 => "\u0463",
                0x1c88 => "\ua64b",
                0x1e9b => "\u1e61",
                0x1fbe => "\u03b9",
                >= 0xab70 and <= 0xabbf => new Rune(rune.Value - 0x97d0).ToString(),
                >= 0x13f8 and <= 0x13fd => new Rune(rune.Value - 8).ToString(),
                0x03c2 => "σ",
                _ => rune.ToString()
            });
        return result.ToString();
    }
}
