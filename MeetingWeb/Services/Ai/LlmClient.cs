using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MeetingWeb.Services.Ai;

public sealed record LlmMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

public enum LlmTask { Summary, Speakers, Answer }

public interface ILlmClient
{
    string Model { get; }
    Task<JsonElement> CompleteJsonAsync(string system, IReadOnlyList<LlmMessage> messages, JsonObject schema, CancellationToken ct = default);
}

// Strict json_schema with json_object fallback and 429/5xx retries live in GroqClient.
public sealed class GroqLlm(GroqClient client, string model) : ILlmClient
{
    public string Model { get; } = model;

    public Task<JsonElement> CompleteJsonAsync(string system, IReadOnlyList<LlmMessage> messages, JsonObject schema, CancellationToken ct = default) =>
        client.ChatJsonAsync(Model, [new("system", system), .. messages], schema, ct);
}

public interface ILlmClientFactory
{
    ILlmClient Create(LlmTask task);
}

// A fresh GroqClient per Create (like Python's build_llm): the key is checked on use, not at startup,
// and the named HttpClient's handler keeps rotating instead of living for the whole process.
public sealed class LlmClientFactory(AiOptions options, IHttpClientFactory http, ILogger<GroqClient> logger) : ILlmClientFactory
{
    private readonly AiOptions _options = options;

    public ILlmClient Create(LlmTask task)
    {
        // task -> (provider variable, model variable, default model)
        var (kindVar, kind, model, defaultModel) = task switch
        {
            LlmTask.Summary => ("SUMMARIZER", _options.Summarizer, _options.SummaryModel, "qwen/qwen3.8-27b"),
            // 2026-11-20, one sample transcript x3 runs: gpt-oss-120b 3/3 valid, no wrong name; qwen3.8-27b one empty/broken answer.
            LlmTask.Speakers => ("SPEAKER_SUGGESTER", _options.SpeakerSuggester, _options.SpeakersModel, "openai/gpt-oss-120b"),
            LlmTask.Answer => ("ANSWERER", _options.Answerer, _options.AnswerModel, "openai/gpt-oss-20b"), // 2026-10-24 ölçümü, ADR-0014
            _ => throw new InvalidOperationException($"Unknown LLM task: {task}")
        };
        // compose passes unset variables as empty strings
        if (string.IsNullOrEmpty(kind)) kind = "groq";
        if (kind != "groq") throw new InvalidOperationException($"Unknown {kindVar}: {kind}");
        return new GroqLlm(new GroqClient(http.CreateClient("Groq"), _options, logger), string.IsNullOrEmpty(model) ? defaultModel : model);
    }
}
