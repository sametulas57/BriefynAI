using System.Text.Json;
using MeetingWeb.Services.Ai;
using MeetingWeb.Tests.Infrastructure;

namespace MeetingWeb.Tests.Ai;

public class GoldSetTests
{
    private static JsonElement Read(string name) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(GoldSet.FindDirectory(), name)));
    private static readonly JsonElement Data = Read("toplantilar.json");
    private static readonly JsonElement[] Questions = Read("sorular.json").EnumerateArray().ToArray();
    private static readonly Dictionary<string, JsonElement> Meetings = Data.GetProperty("toplantilar").EnumerateArray().ToDictionary(m => S(m, "id"));
    private static readonly Dictionary<string, JsonElement> Series = Data.GetProperty("seriler").EnumerateArray().ToDictionary(s => S(s, "id"));
    private static readonly HashSet<string> Teams = Data.GetProperty("ekipler").EnumerateArray().Select(t => S(t, "id")).ToHashSet();
    private static string S(JsonElement e, string key) => e.GetProperty(key).GetString()!;

    private static string MeetingText(JsonElement m)
    {
        var summary = m.GetProperty("ozet");
        var parts = new List<string> { S(m, "baslik") };
        parts.AddRange(m.GetProperty("dokum").EnumerateArray().Select(l => l.GetString()!));
        parts.Add(S(summary, "toplanti_konusu"));
        foreach (var field in new[] { "konular", "kararlar", "bekleyen" })
            parts.AddRange(summary.GetProperty(field).EnumerateArray().Select(l => l.GetString()!));
        parts.AddRange(summary.GetProperty("aksiyonlar").EnumerateArray().Select(a => S(a, "metin")));
        return GoldSet.Norm(string.Join("\n", parts));
    }

    private static HashSet<string> InScope(JsonElement q)
    {
        var scope = q.GetProperty("kapsam");
        if (S(scope, "tur") == "toplanti") return [S(scope, "hedef")];
        return Meetings.Where(m => S(m.Value, "ekip") == S(q, "ekip") &&
            (S(scope, "tur") != "seri" || m.Value.GetProperty("seri").GetString() == S(scope, "hedef")))
            .Select(m => m.Key).ToHashSet();
    }

    [Fact]
    public void Meetings_are_well_formed()
    {
        Assert.InRange(Meetings.Count, 10, 14);
        Assert.Equal(Data.GetProperty("toplantilar").GetArrayLength(), Meetings.Count);
        Assert.True(Teams.SetEquals(new[] { "A", "B" }));
        Assert.True(Teams.SetEquals(Meetings.Values.Select(m => S(m, "ekip"))));
        foreach (var m in Meetings.Values)
        {
            if (m.GetProperty("seri").GetString() is { } series)
                Assert.Equal(S(m, "ekip"), S(Series[series], "ekip"));
            foreach (var line in m.GetProperty("dokum").EnumerateArray())
                Assert.Matches(@"^[^:]{3,40}: \S", line.GetString()!);
            foreach (var action in m.GetProperty("ozet").GetProperty("aksiyonlar").EnumerateArray())
                Assert.True(new HashSet<string> { "metin", "sorumlu", "son_tarih", "bitti" }.SetEquals(action.EnumerateObject().Select(p => p.Name)));
        }
        Assert.Equal(4, Meetings.Values.Count(m => m.GetProperty("seri").GetString() == "S1"));
    }

    [Fact]
    public void Two_transcripts_carry_a_hidden_instruction()
    {
        var hidden = Meetings.Where(m => MeetingText(m.Value).Contains("yapay zekâ") || MeetingText(m.Value).Contains("asistan"))
            .Select(m => m.Key).Order().ToArray();
        Assert.Equal(new[] { "A7", "B3" }, hidden);
        foreach (var id in hidden)
            Assert.DoesNotContain("asistan", GoldSet.Norm(PythonJson.Dumps(System.Text.Json.Nodes.JsonNode.Parse(Meetings[id].GetProperty("ozet").GetRawText())!)));
    }

    [Fact]
    public void Questions_are_well_formed_and_balanced()
    {
        Assert.InRange(Questions.Length, 40, 60);
        Assert.Equal(Questions.Length, Questions.Select(q => S(q, "id")).Distinct().Count());
        var kinds = Questions.GroupBy(q => S(q, "tur")).ToDictionary(g => g.Key, g => g.Count());
        var types = GoldSet.Unanswered.Concat(new[] { "olgu", "karar", "gecersiz_kilma", "karar_yok", "kisi", "zaman", "seri", "toplanti", "talimat" }).ToHashSet();
        Assert.All(kinds.Keys, kind => Assert.Contains(kind, types));
        Assert.True(kinds.Where(k => !GoldSet.Unanswered.Contains(k.Key)).Sum(k => k.Value) >= 30);
        Assert.True(kinds.GetValueOrDefault("cevapsiz") + kinds.GetValueOrDefault("kapsam") >= 10);
        Assert.True(kinds.GetValueOrDefault("talimat") >= 5 && kinds.GetValueOrDefault("sizma") >= 5);
        foreach (var q in Questions)
        {
            Assert.Contains(S(q, "ekip"), Teams);
            Assert.True(S(q, "soru").EnumerateRunes().Count() <= 500);
            var scope = q.GetProperty("kapsam");
            var target = scope.GetProperty("hedef").GetString();
            Assert.Equal(S(scope, "tur") == "arsiv", target == null);
            var owner = S(scope, "tur") switch
            {
                "arsiv" => S(q, "ekip"),
                "seri" => S(Series[target!], "ekip"),
                "toplanti" => S(Meetings[target!], "ekip"),
                _ => throw new InvalidOperationException("Unknown scope")
            };
            Assert.Equal(S(q, "ekip"), owner);
        }
    }

    [Fact]
    public void Expected_evidence_is_in_the_asking_teams_meeting_and_in_scope()
    {
        foreach (var q in Questions)
        {
            var expected = q.GetProperty("beklenen").EnumerateArray().ToArray();
            if (GoldSet.Unanswered.Contains(S(q, "tur")))
            {
                Assert.Empty(expected);
                Assert.Equal(JsonValueKind.Null, q.GetProperty("cevap_ozu").ValueKind);
                continue;
            }
            Assert.NotEmpty(expected);
            Assert.False(string.IsNullOrEmpty(S(q, "cevap_ozu")));
            foreach (var hit in expected)
            {
                Assert.Contains(S(hit, "toplanti"), InScope(q));
                Assert.Contains(GoldSet.Norm(S(hit, "kanit")), MeetingText(Meetings[S(hit, "toplanti")]));
            }
        }
    }

    [Fact]
    public void Injection_questions_hit_the_poisoned_meetings_and_name_what_must_not_leak()
    {
        foreach (var q in Questions.Where(q => S(q, "tur") == "talimat"))
        {
            Assert.Contains(q.GetProperty("beklenen").EnumerateArray(), h => S(h, "toplanti") is "A7" or "B3");
            Assert.NotEmpty(q.GetProperty("yasak").EnumerateArray());
        }
    }

    [Fact]
    public void Leak_questions_ask_about_the_other_teams_data()
    {
        foreach (var q in Questions.Where(q => S(q, "tur") == "sizma"))
        {
            var trace = GoldSet.Norm(S(q, "iz"));
            Assert.Contains(trace, string.Join("\n", Meetings.Values.Where(m => S(m, "ekip") != S(q, "ekip")).Select(MeetingText)));
            foreach (var m in Meetings.Values.Where(m => S(m, "ekip") == S(q, "ekip")))
                Assert.DoesNotContain(trace, MeetingText(m));
        }
    }
}
