using System.Globalization;
using System.Text.RegularExpressions;

namespace MeetingWeb.Services;

public static class TurkishNameSuffix
{
    public static string ReplaceSpeakerNames(string text, IReadOnlyDictionary<string, string> oldToNew)
    {
        if (oldToNew.Count == 0) return text;
        var alternatives = string.Join("|", oldToNew.Keys.OrderByDescending(name => name.Length).Select(Regex.Escape));
        return Regex.Replace(text,
            @"(?<![\p{L}\p{N}])(?<name>" + alternatives + @")(?![\p{L}\p{N}])(?:(?<quote>['’])(?<suffix>[a-zçğıöşü]+)(?!\p{L}))?",
            match =>
            {
                var name = oldToNew[match.Groups["name"].Value];
                var suffix = match.Groups["suffix"].Value;
                return name + match.Groups["quote"].Value + Inflect(name, suffix);
            });
    }

    private static string Inflect(string name, string suffix)
    {
        var lastWord = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        if (string.IsNullOrEmpty(lastWord) || !char.IsLetter(lastWord[^1])) return suffix;
        var vowel = lastWord.LastOrDefault(c => "aeıioöuü".Contains(c));
        if (vowel == default) return suffix;
        var v2 = "eiöü".Contains(vowel) ? "e" : "a";
        var v4 = vowel switch { 'a' or 'ı' => "ı", 'e' or 'i' => "i", 'o' or 'u' => "u", _ => "ü" };
        var endsVowel = "aeıioöuü".Contains(lastWord[^1]);
        var consonant = "fstkçşhp".Contains(lastWord[^1]) ? "t" : "d";
        return suffix switch
        {
            "in" or "ın" or "un" or "ün" or "nin" or "nın" or "nun" or "nün" => (endsVowel ? "n" : "") + v4 + "n",
            "i" or "ı" or "u" or "ü" or "yi" or "yı" or "yu" or "yü" => (endsVowel ? "y" : "") + v4,
            "e" or "a" or "ye" or "ya" => (endsVowel ? "y" : "") + v2,
            "de" or "da" or "te" or "ta" => consonant + v2,
            "den" or "dan" or "ten" or "tan" => consonant + v2 + "n",
            "le" or "la" or "yle" or "yla" => (endsVowel ? "yl" : "l") + v2,
            _ => suffix
        };
    }
}
