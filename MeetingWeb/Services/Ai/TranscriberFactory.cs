using System.Globalization;

namespace MeetingWeb.Services.Ai;

public interface ITranscriberFactory
{
    ITranscriber Create(string? kind);
}

public sealed class TranscriberFactory(IHttpClientFactory http, AiOptions options, IAudioTools audio, ILoggerFactory loggers) : ITranscriberFactory
{
    public ITranscriber Create(string? kind)
    {
        kind = string.IsNullOrEmpty(kind) ? (string.IsNullOrEmpty(options.Transcriber) ? "groq" : options.Transcriber) : kind;
        if (kind == "groq")
            return new GroqTranscriber(new GroqClient(http.CreateClient("Groq"), options, loggers.CreateLogger<GroqClient>()),
                audio, options.TranscribeModel);
        if (kind == "azure")
        {
            if (string.IsNullOrEmpty(options.AzureSpeechKey)) throw new InvalidOperationException("AZURE_SPEECH_KEY is not set");
            if (string.IsNullOrEmpty(options.AzureSpeechEndpoint)) throw new InvalidOperationException("AZURE_SPEECH_ENDPOINT is not set");
            // Empty .env values use the default; Python's int("") would fail here.
            var maxSpeakers = int.Parse(string.IsNullOrEmpty(options.AzureSpeechMaxSpeakers) ? "10" : options.AzureSpeechMaxSpeakers, CultureInfo.InvariantCulture);
            return new AzureTranscriber(http.CreateClient("AzureSpeech"), audio, options.AzureSpeechEndpoint, options.AzureSpeechKey, maxSpeakers: maxSpeakers);
        }
        throw new InvalidOperationException($"Unknown TRANSCRIBER: {kind}");
    }
}
