using System.Text.Json;
using System.Text.Json.Nodes;
using MeetingWeb.Models;

namespace MeetingWeb.Services.Ai;

public sealed class MeetingSummarizer(ILlmClient llm, ILogger logger, int maxChars = 12000)
{
    public const string System = "Toplantı dökümünden yapılandırılmış özet çıkaran bir asistansın. Yalnız dökümde geçen bilgiyi kullan; dökümde olmayan kişi, tarih, rakam ekleme. Döküm otomatik yazıya dökülmüştür, küçük yazım hataları olabilir; bariz olanları düzelt, emin olmadığını olduğu gibi bırak. Sonradan değiştirilen kararlarda son hâli yaz; önerilip kabul edilmeyen ya da ertelenen konuları karar diye yazma. Aksiyonun sahibi ya da süresi söylenmemişse null bırak. Türkçe yaz. Dökümde satırlar Ad: söz biçiminde olabilir; aksiyonun sahibini bu adlardan yalnız dökümde açıkça söylendiyse yaz. Toplantının işiyle ilgisi olmayan yan sohbet (maç, hava durumu, selamlaşma/vedalaşma, şaka, özel hayat) görüşülen konulara, kararlara ve aksiyonlara yazılmaz; yan sohbetin içinde iş ile ilgili bir karar ya da görev geçerse o iş kısmı yazılır. Bekleyen konular, toplantıda konuşulup karara bağlanmayan, ertelenen (sonra bakarız) ya da cevabı/bilgisi beklenen konulardır. Karar veya aksiyon olarak yazılanı tekrarlama; iş dışı yan sohbeti yazma; uydurma. Dökümde yoksa boş liste ver.";

    public const string MergeInstruction = "Aşağıda aynı toplantının ardışık bölümlerinden çıkarılmış kısmi özetler var. Bunları tek bir özette birleştir: tekrarları ayıkla, sonraki bölümde değişen kararlarda son hâli yaz, kısmi özetlerde olmayan hiçbir şey ekleme. Bekleyen konuları da birleştir, tekrarları ayıkla; sonraki bölümde karara bağlanan konuyu bekleyenlerden çıkar. Toplantının işiyle ilgisi olmayan yan sohbet (maç, hava durumu, selamlaşma/vedalaşma, şaka, özel hayat) görüşülen konulara, kararlara ve aksiyonlara yazma; yan sohbetin içindeki iş ile ilgili karar ya da görevi koru.";

    public static JsonObject Schema() => JsonNode.Parse("""
{
    "type": "object",
    "additionalProperties": false,
    "required": [
        "toplanti_konusu",
        "gorusulen_konular",
        "alinan_kararlar",
        "aksiyon_maddeleri",
        "bekleyen_konular"
    ],
    "properties": {
        "toplanti_konusu": {
            "type": "string"
        },
        "gorusulen_konular": {
            "type": "array",
            "items": {
                "type": "string"
            }
        },
        "alinan_kararlar": {
            "type": "array",
            "items": {
                "type": "string"
            }
        },
        "bekleyen_konular": {
            "type": "array",
            "items": {
                "type": "string"
            }
        },
        "aksiyon_maddeleri": {
            "type": "array",
            "items": {
                "type": "object",
                "additionalProperties": false,
                "required": [
                    "kim",
                    "ne",
                    "ne_zaman"
                ],
                "properties": {
                    "kim": {
                        "type": [
                            "string",
                            "null"
                        ]
                    },
                    "ne": {
                        "type": "string"
                    },
                    "ne_zaman": {
                        "type": [
                            "string",
                            "null"
                        ]
                    }
                }
            }
        }
    }
}
""")!.AsObject();

    public async Task<AiSummaryData> SummarizeAsync(string transcript, CancellationToken ct)
    {
        if (PyText.Length(transcript) <= maxChars)
            return ToBackendFormat(await AskAsync("Toplantı dökümü:\n\n" + transcript, ct));

        var parts = SplitText(transcript, maxChars);
        logger.LogInformation("Long transcript: summarising {Parts} parts, then merging.", parts.Count);
        var partials = new JsonArray();
        for (var i = 0; i < parts.Count; i++)
        {
            var partial = await AskAsync($"Toplantı dökümünün {i + 1}/{parts.Count}. bölümü:\n\n{parts[i]}", ct);
            partials.Add(JsonNode.Parse(partial.GetRawText()));
        }
        var merged = await AskAsync(MergeInstruction + "\n\nKısmi özetler:\n" + PythonJson.Dumps(partials), ct);
        return ToBackendFormat(merged);
    }

    private Task<JsonElement> AskAsync(string content, CancellationToken ct) =>
        llm.CompleteJsonAsync(System, [new LlmMessage("user", content)], Schema(), ct);

    public static IReadOnlyList<string> SplitText(string text, int maxChars)
    {
        var sentences = text.Replace("\n", " ").Split(". ", StringSplitOptions.None)
            .Where(s => PyText.Strip(s).Length > 0).Select(s => PyText.Strip(s) + ".");
        List<string> parts = [];
        var current = "";
        foreach (var raw in sentences)
        {
            var sentence = raw.EndsWith("..", StringComparison.Ordinal) ? raw[..^1] : raw;
            if (current.Length > 0 && PyText.Length(current) + PyText.Length(sentence) + 1 > maxChars)
            {
                parts.Add(PyText.Strip(current));
                current = "";
            }
            current += sentence + " ";
        }
        if (PyText.Strip(current).Length > 0) parts.Add(PyText.Strip(current));
        return parts;
    }

    public static AiSummaryData ToBackendFormat(JsonElement summary)
    {
        if (summary.ValueKind != JsonValueKind.Object) throw new JsonException("Summary must be an object");
        List<string> actions = [];
        foreach (var action in Array(summary, "aksiyon_maddeleri"))
        {
            if (action.ValueKind != JsonValueKind.Object) continue;
            var text = PyText.Strip(Text(action, "ne"));
            if (action.TryGetProperty("kim", out var owner) && IsTruthy(owner)) text = Display(owner) + ": " + text;
            if (action.TryGetProperty("ne_zaman", out var deadline) && IsTruthy(deadline)) text += " (" + Display(deadline) + ")";
            actions.Add(text);
        }
        return new AiSummaryData
        {
            ToplantiKonusu = PyText.Strip(Text(summary, "toplanti_konusu")),
            GorusulenKonular = Strings(summary, "gorusulen_konular"),
            AlinanKararlar = Strings(summary, "alinan_kararlar"),
            BekleyenKonular = Strings(summary, "bekleyen_konular"),
            AksiyonMaddeleri = actions
        };
    }

    private static string Text(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var value)) return "";
        if (value.ValueKind != JsonValueKind.String) throw new JsonException("Expected a string");
        return value.GetString()!;
    }

    private static IEnumerable<JsonElement> Array(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var value)) return [];
        if (value.ValueKind != JsonValueKind.Array) throw new JsonException("Expected an array");
        return value.EnumerateArray();
    }

    private static List<string> Strings(JsonElement obj, string key) => Array(obj, key).Select(value =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new JsonException("Expected a string item")).ToList();

    private static bool IsTruthy(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False => false,
        JsonValueKind.String => value.GetString()!.Length > 0,
        JsonValueKind.Number => value.GetDouble() != 0,
        JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.Object => value.EnumerateObject().Any(),
        _ => true
    };

    private static string Display(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
}
