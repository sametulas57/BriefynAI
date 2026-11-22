using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeetingWeb.Services.Ai;

namespace MeetingWeb.Tests.Ai;

public class TranscriptTextTests
{
    public static IEnumerable<object[]> Cases()
    {
        var inputs = JsonSerializer.Deserialize<JsonElement>(Golden("transcript_inputs.json"));
        var expected = JsonSerializer.Deserialize<JsonElement>(Golden("transcript_expected.json"));
        return inputs.EnumerateArray().Select((input, i) => new object[]
        {
            input.GetProperty("op").GetString() + "/" + input.GetProperty("id").GetString(), input, expected[i]
        }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matches_python_golden(string name, JsonElement input, JsonElement expected)
    {
        var text = input.TryGetProperty("text", out var t) ? t.GetString()! : "";
        var prompt = input.TryGetProperty("prompt", out var p) ? p.GetString()! : "";
        object actual = input.GetProperty("op").GetString() switch
        {
            "normalise" => TranscriptText.NormaliseText(text),
            "prompt_echo" => TranscriptText.IsPromptEcho(text, prompt),
            "caption" => TranscriptText.IsHallucinatedCaption(text),
            "only_captions" => TranscriptText.IsOnlyHallucinatedCaptions(text,
                input.GetProperty("speakers").EnumerateArray().Select(s => s.GetString()!).ToHashSet()),
            "clean_segment" => TranscriptText.CleanSegmentText(text, prompt),
            "clean_plain" => TranscriptText.CleanPlainChunk(text, prompt),
            "parse_vtt" => TranscriptText.ParseVtt(text).Select(c => new object?[] { c.Start, c.End, c.Speaker, c.Text }).ToArray(),
            "assign" => TranscriptText.AssignSpeakers(
                input.GetProperty("segments").EnumerateArray().Select(s => new TranscriptSegment(s[0].GetDouble(), s[1].GetDouble(), s[2].GetString()!)).ToArray(),
                input.GetProperty("cues").EnumerateArray().Select(c => new Cue(c[0].GetDouble(), c[1].GetDouble(), c[2].GetString(), c[3].GetString()!)).ToArray())
                .Select(pair => new string?[] { pair.Speaker, pair.Text }).ToArray(),
            "format" => TranscriptText.FormatLines(input.GetProperty("pairs").EnumerateArray()
                .Select(pair => (pair[0].GetString(), pair[1].GetString()!)).ToArray()),
            "casefold" => PyText.CaseFold(text),
            _ => throw new ArgumentException(name)
        };
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetRawText()), JsonSerializer.SerializeToNode(actual)),
            $"{name}: expected {expected.GetRawText()}, actual {JsonSerializer.Serialize(actual)}");
    }

    [Fact]
    public void Constants_match_python_bytes()
    {
        var actual = new JsonObject
        {
            ["OLD_TECH_INSTRUCTION"] = TranscriptText.OldTechInstruction,
            ["HALLUCINATED_PHRASES"] = JsonSerializer.SerializeToNode(TranscriptText.HallucinatedPhrases)
        };
        var expected = Golden("transcript_constants.json");
        Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(PythonJson.Dumps(actual)));
    }

    internal static string Golden(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "MeetingWeb.Tests", "Ai", "Golden", name);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(name);
    }
}
