using System.Text.Json;
using System.Text.Json.Nodes;
using MeetingWeb.Models;
using MeetingWeb.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetingWeb.Tests.Ai;

public class MeetingSummarizerTests
{
    internal const string Transcript = "Dr. Ayşe toplantıyı açtı ve müşteri görüşlerini paylaştı. " +
        "Ürün ekibi yeni tasarımın çarşamba günü hazır olacağını söyledi.. " +
        "Bütçe için   iki seçenek incelendi ve uygun teklifin yanıtı beklendi.\n" +
        "Çağrı merkezi şikâyetleri azalırken ölçüm yönteminin yeniden ele alınması istendi. " +
        "Deniz, dağıtım öncesi güvenlik kontrolünü üstlendi. " +
        "Öğleden sonra İzmir ekibiyle yapılacak görüşmenin saati henüz kesinleşmedi. " +
        "Şule raporu güncelledi ve eksik rakamları perşembe günü tamamlayacağını belirtti. " +
        "Eski takvim değiştirildi; son karar cuma sabahı yayımlamak oldu. " +
        "Son görüşmede tedarikçiden gelecek açıklamanın beklenmesine karar verildi";
    internal const string Partial = """{"aksiyon_maddeleri":[{"ne_zaman":null,"ne":"taslağı hazırla","kim":"Şule"}],"bekleyen_konular":["Ücret yanıtı"],"alinan_kararlar":["Çarşamba görüşülecek"],"toplanti_konusu":"İş akışı","gorusulen_konular":["Ölçüm"]}""";
    private const string BackendInput = """{"toplanti_konusu":"  Çalışma planı \t","gorusulen_konular":["  Bütçe  "],"alinan_kararlar":["Cuma yayımla"],"bekleyen_konular":["Yanıt bekleniyor"],"aksiyon_maddeleri":[{"kim":"","ne":"  planı hazırla  ","ne_zaman":null},{"kim":" Ayşe ","ne":"  kontrol et\t","ne_zaman":" cuma "},"atlanır",null,7,[],{}]}""";

    [Fact]
    public void System_matches_python_golden() => Assert.Equal(MeetingSummarizer.System, Golden("summarizer_system.txt"));

    [Fact]
    public void Merge_instruction_matches_python_golden() => Assert.Equal(MeetingSummarizer.MergeInstruction, Golden("summarizer_merge_instruction.txt"));

    [Fact]
    public void Schema_matches_python_golden() => Assert.Equal(Golden("summarizer_schema.json"), PythonJson.Dumps(MeetingSummarizer.Schema()));

    [Fact]
    public void Split_text_matches_python_golden() => Assert.Equal(Golden("summarizer_split.json"),
        PythonJson.Dumps(JsonSerializer.SerializeToNode(MeetingSummarizer.SplitText(Transcript, 120))!));

    [Fact]
    public void Backend_format_matches_python_golden() => Assert.Equal(Golden("summarizer_backend.json"),
        BackendJson(MeetingSummarizer.ToBackendFormat(JsonSerializer.Deserialize<JsonElement>(BackendInput))));

    [Fact]
    public void Actions_become_readable_lines_for_the_backend()
    {
        var result = MeetingSummarizer.ToBackendFormat(JsonSerializer.Deserialize<JsonElement>("""
            {"toplanti_konusu":"Lansman","gorusulen_konular":["Tedarik"],"alinan_kararlar":["Release salıya"],
             "bekleyen_konular":["Tedarik fiyatı yanıtı bekleniyor"],"aksiyon_maddeleri":[
             {"kim":"Deniz","ne":"hotfix çıkar","ne_zaman":"çarşamba"},{"kim":null,"ne":"rollback planı","ne_zaman":null}]}
            """));
        Assert.Equal(["Deniz: hotfix çıkar (çarşamba)", "rollback planı"], result.AksiyonMaddeleri);
        Assert.Equal(["Release salıya"], result.AlinanKararlar);
        Assert.Equal(["Tedarik fiyatı yanıtı bekleniyor"], result.BekleyenKonular);
    }

    [Fact]
    public void Pending_topics_default_to_empty_list()
    {
        var result = MeetingSummarizer.ToBackendFormat(JsonSerializer.Deserialize<JsonElement>("{}"));
        Assert.Equal("", result.ToplantiKonusu);
        Assert.Empty(result.GorusulenKonular);
        Assert.Empty(result.AlinanKararlar);
        Assert.Empty(result.BekleyenKonular!);
        Assert.Empty(result.AksiyonMaddeleri);
    }

    [Fact]
    public void Pending_topics_are_required_by_schema_and_each_schema_is_fresh()
    {
        var schema = MeetingSummarizer.Schema();
        Assert.Contains("bekleyen_konular", schema["required"]!.AsArray().Select(p => p!.GetValue<string>()));
        schema["properties"]!["bekleyen_konular"]!["type"] = "number";
        Assert.Equal("array", MeetingSummarizer.Schema()["properties"]!["bekleyen_konular"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Long_transcripts_are_split_on_sentence_boundaries()
    {
        var parts = MeetingSummarizer.SplitText(string.Concat(Enumerable.Repeat("Bir cümle. ", 500)), 1000);
        Assert.True(parts.Count > 1);
        Assert.All(parts, part => { Assert.True(part.EnumerateRunes().Count() <= 1000); Assert.EndsWith(".", part); });
    }

    [Theory]
    [InlineData("", 10, new string[] { })]
    [InlineData(" \n ", 10, new string[] { })]
    [InlineData("İlk.. Son", 20, new[] { "İlk. Son." })]
    [InlineData("😀. 😀.", 6, new[] { "😀. 😀." })]
    [InlineData("😀. 😀.", 5, new[] { "😀.", "😀." })]
    [InlineData("Uzunca bir cümle", 3, new[] { "Uzunca bir cümle." })]
    public void Split_text_preserves_python_edge_cases(string text, int maxChars, string[] expected) =>
        Assert.Equal(expected, MeetingSummarizer.SplitText(text, maxChars));

    public static TheoryData<string> InvalidSummaries => new()
    {
        "null", "[]", "42", "\"text\"",
        """{"toplanti_konusu":null}""", """{"toplanti_konusu":7}""",
        """{"gorusulen_konular":"konu"}""", """{"gorusulen_konular":[7]}""",
        """{"alinan_kararlar":null}""", """{"alinan_kararlar":[null]}""",
        """{"bekleyen_konular":{}}""", """{"bekleyen_konular":[false]}""",
        """{"aksiyon_maddeleri":null}""", """{"aksiyon_maddeleri":"görev"}""",
        """{"aksiyon_maddeleri":[{"ne":null}]}""", """{"aksiyon_maddeleri":[{"ne":7}]}"""
    };

    [Theory]
    [MemberData(nameof(InvalidSummaries))]
    public void Type_mismatches_throw_json_exception(string json) => Assert.Throws<JsonException>(() =>
        MeetingSummarizer.ToBackendFormat(JsonSerializer.Deserialize<JsonElement>(json)));

    [Theory]
    [InlineData("null", "görev")]
    [InlineData("\"\"", "görev")]
    [InlineData("false", "görev")]
    [InlineData("0", "görev")]
    [InlineData("0.0", "görev")]
    [InlineData("[]", "görev")]
    [InlineData("{}", "görev")]
    [InlineData("7", "7: görev (7)")]
    [InlineData("true", "true: görev (true)")]
    [InlineData("[1]", "[1]: görev ([1])")]
    [InlineData("{\"ad\":1}", "{\"ad\":1}: görev ({\"ad\":1})")]
    [InlineData("\" \"", " : görev ( )")]
    public void Action_owner_and_deadline_use_python_truthiness_and_raw_json_for_non_strings(string value, string expected)
    {
        var result = MeetingSummarizer.ToBackendFormat(JsonSerializer.Deserialize<JsonElement>(
            "{\"aksiyon_maddeleri\":[{\"ne\":\" görev \",\"kim\":" + value + ",\"ne_zaman\":" + value + "}]}"));
        Assert.Equal(expected, Assert.Single(result.AksiyonMaddeleri));
    }

    [Fact]
    public async Task Summarizer_runs_on_any_llm_client()
    {
        var llm = new FakeLlm
        {
            Reply = _ => """{"aksiyon_maddeleri":[{"kim":"Deniz","ne":"hotfix çıkar","ne_zaman":null}]}"""
        };
        using var source = new CancellationTokenSource();
        var result = await new MeetingSummarizer(llm, NullLogger.Instance).SummarizeAsync("Kısa toplantı.", source.Token);
        Assert.Equal(["Deniz: hotfix çıkar"], result.AksiyonMaddeleri);
        var call = Assert.Single(llm.Calls);
        Assert.Equal(MeetingSummarizer.System, call.System);
        Assert.Equal([new LlmMessage("user", "Toplantı dökümü:\n\nKısa toplantı.")], call.Messages);
        Assert.Equal(PythonJson.Dumps(MeetingSummarizer.Schema()), PythonJson.Dumps(call.Schema));
        Assert.Equal(source.Token, call.Token);
    }

    [Fact]
    public async Task Short_transcript_is_one_call()
    {
        var llm = new FakeLlm();
        await new MeetingSummarizer(llm, NullLogger.Instance).SummarizeAsync("Kısa toplantı.", default);
        Assert.Single(llm.Calls);
    }

    [Fact]
    public async Task Long_transcript_is_summarised_in_parts_then_merged_sequentially()
    {
        var parts = MeetingSummarizer.SplitText(Transcript, 120);
        var llm = new FakeLlm { Reply = i => Partial.Replace("İş akışı", $"Bölüm {i}") };
        using var source = new CancellationTokenSource();
        var result = await new MeetingSummarizer(llm, NullLogger.Instance, 120).SummarizeAsync(Transcript, source.Token);
        Assert.Equal(parts.Count + 1, llm.Calls.Count);
        Assert.Equal(1, llm.MaxConcurrentCalls);
        for (var i = 0; i < parts.Count; i++)
            Assert.Equal($"Toplantı dökümünün {i + 1}/{parts.Count}. bölümü:\n\n{parts[i]}", Assert.Single(llm.Calls[i].Messages).Content);
        var merge = Assert.Single(llm.Calls[^1].Messages).Content;
        Assert.StartsWith(MeetingSummarizer.MergeInstruction + "\n\nKısmi özetler:\n", merge);
        var partials = JsonNode.Parse(merge[(merge.IndexOf("Kısmi özetler:\n", StringComparison.Ordinal) + "Kısmi özetler:\n".Length)..])!.AsArray();
        Assert.Equal(Enumerable.Range(0, parts.Count).Select(i => $"Bölüm {i}"), partials.Select(p => p!["toplanti_konusu"]!.GetValue<string>()));
        Assert.Equal($"Bölüm {parts.Count}", result.ToplantiKonusu);
        Assert.All(llm.Calls, call =>
        {
            Assert.Equal(MeetingSummarizer.System, call.System);
            Assert.Equal(PythonJson.Dumps(MeetingSummarizer.Schema()), PythonJson.Dumps(call.Schema));
            Assert.Equal(source.Token, call.Token);
            Assert.Equal("user", Assert.Single(call.Messages).Role);
        });
    }

    [Fact]
    public async Task Merge_content_matches_python_golden()
    {
        var llm = new FakeLlm();
        await new MeetingSummarizer(llm, NullLogger.Instance, 120).SummarizeAsync(Transcript, default);
        Assert.Equal(Golden("summarizer_merge_content.txt"), Assert.Single(llm.Calls[^1].Messages).Content);
    }

    [Theory]
    [InlineData(120, 1)]
    [InlineData(121, 2)]
    public async Task Max_chars_boundary_counts_unicode_code_points(int length, int expectedCalls)
    {
        var llm = new FakeLlm();
        var transcript = string.Concat(Enumerable.Repeat("😀", length));
        await new MeetingSummarizer(llm, NullLogger.Instance, 120).SummarizeAsync(transcript, default);
        Assert.Equal(expectedCalls, llm.Calls.Count);
    }

    [Fact]
    public async Task Summary_system_excludes_off_topic_small_talk_for_short_transcripts()
    {
        var llm = new FakeLlm();
        await new MeetingSummarizer(llm, NullLogger.Instance).SummarizeAsync("Maç hakkında kısa sohbet.", default);
        Assert.Contains("işiyle ilgisi olmayan yan sohbet", Assert.Single(llm.Calls).System);
    }

    [Fact]
    public async Task Summary_system_and_merge_keep_off_topic_rule_for_long_transcripts()
    {
        var llm = new FakeLlm();
        await new MeetingSummarizer(llm, NullLogger.Instance, 100).SummarizeAsync(string.Concat(Enumerable.Repeat("Maç hakkında uzun sohbet. ", 30)), default);
        Assert.True(llm.Calls.Count > 1);
        Assert.All(llm.Calls, call => Assert.Contains("işiyle ilgisi olmayan yan sohbet", call.System));
        Assert.Contains("işiyle ilgisi olmayan yan sohbet", Assert.Single(llm.Calls[^1].Messages).Content);
    }

    internal sealed record Call(string System, IReadOnlyList<LlmMessage> Messages, JsonObject Schema, CancellationToken Token);

    internal sealed class FakeLlm : ILlmClient
    {
        public string Model => "fake";
        public List<Call> Calls { get; } = [];
        public Func<int, string> Reply { get; init; } = _ => Partial;
        public Exception? Error { get; init; }
        public int MaxConcurrentCalls { get; private set; }
        private int _activeCalls;

        public async Task<JsonElement> CompleteJsonAsync(string system, IReadOnlyList<LlmMessage> messages, JsonObject schema, CancellationToken ct = default)
        {
            var index = Calls.Count;
            Calls.Add(new(system, messages, schema, ct));
            MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, ++_activeCalls);
            try
            {
                await Task.Yield();
                if (Error != null) throw Error;
                return JsonSerializer.Deserialize<JsonElement>(Reply(index));
            }
            finally { _activeCalls--; }
        }
    }

    internal static string BackendJson(AiSummaryData summary) => PythonJson.Dumps(new JsonObject
    {
        ["toplanti_konusu"] = summary.ToplantiKonusu,
        ["gorusulen_konular"] = JsonSerializer.SerializeToNode(summary.GorusulenKonular),
        ["alinan_kararlar"] = JsonSerializer.SerializeToNode(summary.AlinanKararlar),
        ["bekleyen_konular"] = JsonSerializer.SerializeToNode(summary.BekleyenKonular),
        ["aksiyon_maddeleri"] = JsonSerializer.SerializeToNode(summary.AksiyonMaddeleri)
    });

    private static string Golden(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "MeetingWeb.Tests", "Ai", "Golden", name);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(name);
    }
}
