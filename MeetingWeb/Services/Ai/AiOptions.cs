namespace MeetingWeb.Services.Ai;

public sealed class AiOptions
{
    public string GroqApiKey { get; init; } = "";
    public string TranscribeModel { get; init; } = "whisper-large-v3";
    public string? Transcriber { get; init; }
    public string? AzureSpeechKey { get; init; }
    public string? AzureSpeechEndpoint { get; init; }
    public string? AzureSpeechMaxSpeakers { get; init; }
    public string? Summarizer { get; init; }
    public string? SummaryModel { get; init; }
    public string? SpeakerSuggester { get; init; }
    public string? SpeakersModel { get; init; }
    public string? Answerer { get; init; }
    public string? AnswerModel { get; init; }

    public static AiOptions FromConfiguration(IConfiguration c) => new()
    {
        GroqApiKey = c["GROQ_API_KEY"] ?? "",
        TranscribeModel = string.IsNullOrEmpty(c["AI_TRANSCRIBE_MODEL"]) ? "whisper-large-v3" : c["AI_TRANSCRIBE_MODEL"]!,
        Transcriber = c["TRANSCRIBER"],
        AzureSpeechKey = c["AZURE_SPEECH_KEY"],
        AzureSpeechEndpoint = c["AZURE_SPEECH_ENDPOINT"],
        AzureSpeechMaxSpeakers = c["AZURE_SPEECH_MAX_SPEAKERS"],
        Summarizer = c["SUMMARIZER"],
        SummaryModel = c["AI_SUMMARY_MODEL"],
        SpeakerSuggester = c["SPEAKER_SUGGESTER"],
        SpeakersModel = c["AI_SPEAKERS_MODEL"],
        Answerer = c["ANSWERER"],
        AnswerModel = c["AI_ANSWER_MODEL"]
    };
}
