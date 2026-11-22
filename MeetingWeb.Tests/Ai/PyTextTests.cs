using System.Text;
using MeetingWeb.Services.Ai;

namespace MeetingWeb.Tests.Ai;

public class PyTextTests
{
    [Theory]
    [InlineData("", "", "", new string[] { })]
    [InlineData("\u001c a\t b\u001f", "a\t b", "\u001c a\t b", new[] { "a", "b" })]
    [InlineData("\u200b x\ufeff", "\u200b x\ufeff", "\u200b x\ufeff", new[] { "\u200b", "x\ufeff" })]
    [InlineData("\u0085\u00a0😀\u2029", "😀", "\u0085\u00a0😀", new[] { "😀" })]
    public void Whitespace_matches_python(string text, string stripped, string rightStripped, string[] words)
    {
        Assert.Equal(stripped, PyText.Strip(text));
        Assert.Equal(rightStripped, PyText.RStrip(text));
        Assert.Equal(words, PyText.SplitWhitespace(text));
    }

    [Theory]
    [InlineData(0x1c, true)]
    [InlineData(0x1f, true)]
    [InlineData(0x85, true)]
    [InlineData(0x200b, false)]
    [InlineData(0xfeff, false)]
    public void Space_includes_python_information_separators(int value, bool expected) =>
        Assert.Equal(expected, PyText.IsSpace(new Rune(value)));

    [Theory]
    [InlineData("", new string[] { }, new string[] { })]
    [InlineData("a\r\nb\r", new[] { "a", "b" }, new[] { "a\r\n", "b\r" })]
    [InlineData("\n\n", new[] { "", "" }, new[] { "\n", "\n" })]
    [InlineData("a\u001fb", new[] { "a\u001fb" }, new[] { "a\u001fb" })]
    public void Lines_preserve_python_terminal_separator_rules(string text, string[] lines, string[] kept)
    {
        Assert.Equal(lines, PyText.SplitLines(text));
        Assert.Equal(kept, PyText.SplitLines(text, true));
    }

    [Theory]
    [InlineData('\n')]
    [InlineData('\r')]
    [InlineData('\v')]
    [InlineData('\f')]
    [InlineData('\u001c')]
    [InlineData('\u001d')]
    [InlineData('\u001e')]
    [InlineData('\u0085')]
    [InlineData('\u2028')]
    [InlineData('\u2029')]
    public void All_python_line_separators_are_recognized(char separator)
    {
        Assert.Equal(new[] { "😀", "son" }, PyText.SplitLines($"😀{separator}son{separator}"));
        Assert.Equal(new[] { $"😀{separator}", $"son{separator}" }, PyText.SplitLines($"😀{separator}son{separator}", true));
    }

    [Theory]
    [InlineData("😀İ😀x", 3, "😀İ😀")]
    [InlineData("😀İ", 0, "")]
    [InlineData("😀İ", 9, "😀İ")]
    [InlineData("😀İx", -1, "😀İ")]
    [InlineData("😀İx", -8, "")]
    public void Prefix_counts_code_points_like_a_python_slice(string text, int count, string expected)
    {
        Assert.Equal(text.EnumerateRunes().Count(), PyText.Length(text));
        Assert.Equal(expected, PyText.Prefix(text, count));
    }

    [Theory]
    [InlineData(0x216b, true)]
    [InlineData(0xb2, true)]
    [InlineData(0x660, true)]
    [InlineData(0x10400, true)]
    [InlineData(0x301, false)]
    [InlineData(0x1f600, false)]
    public void Alnum_includes_all_letter_and_number_categories(int value, bool expected) =>
        Assert.Equal(expected, PyText.IsAlnum(new Rune(value)));

    [Theory]
    [InlineData("Straße ẞ", "strasse ss")]
    [InlineData("Σςσ", "σσσ")]
    [InlineData("İﬁևﬓ", "i\u0307fiեւմն")]
    [InlineData("ΐᾈᾲ", "ι\u0308\u0301ἀιὰι")]
    public void Casefold_expands_latin_greek_and_armenian(string text, string expected) =>
        Assert.Equal(expected, PyText.CaseFold(text));
}
