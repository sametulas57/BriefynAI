using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MeetingWeb.Models;

namespace MeetingWeb.Services.Ai;

public sealed class PythonAiEngine(
    IHttpClientFactory clients,
    IConfiguration configuration,
    ILogger<PythonAiEngine> logger) : IAiEngine
{
    public const string HttpClientName = "PythonApi";

    public async Task<AiApiResponse?> ProcessMeetingAsync(string audioPath, string? speakersPath, CancellationToken ct = default)
    {
        // Create an HttpClient instance using the configured factory to prevent socket exhaustion.
        var client = clients.CreateClient(HttpClientName);
        var endpoint = configuration["PythonApiSettings:ProcessEndpoint"];

        // Utilize 'using var' to ensure unmanaged memory resources are disposed immediately.
        using var form = new MultipartFormDataContent();
        using var fileStream = new FileStream(audioPath, FileMode.Open, FileAccess.Read);
        using var streamContent = new StreamContent(fileStream);

        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");
        form.Add(streamContent, "file", Path.GetFileName(audioPath));
        using var sidecarStream = speakersPath == null ? null : new FileStream(speakersPath, FileMode.Open, FileAccess.Read);
        if (sidecarStream != null)
            form.Add(new StreamContent(sidecarStream), "speakers", Path.GetFileName(speakersPath!));

        try
        {
            logger.LogInformation("Executing POST request to {Endpoint}", endpoint);

            HttpResponseMessage response = await client.PostAsync(endpoint, form, ct);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Received 200 OK from Python API. Deserializing payload...");
                string jsonResponse = await response.Content.ReadAsStringAsync(ct);

                // Deserialize the JSON response into a safe Data Transfer Object (DTO).
                var aiResponse = JsonSerializer.Deserialize<AiApiResponse>(jsonResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return aiResponse;
            }

            string errorDetail = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                try
                {
                    using var error = JsonDocument.Parse(errorDetail);
                    if (error.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String
                        && detail.GetString() == "no_speech")
                        throw new NoSpeechException();
                }
                catch (JsonException) { /* Keep malformed API responses on the general error path. */ }
            }
            logger.LogError("Python API responded with status code: {StatusCode}. Details: {ErrorDetail}", response.StatusCode, errorDetail);
            throw new HttpRequestException($"Python API Error: {response.StatusCode} - {errorDetail}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to connect to the Python API server.");
            throw new Exception("Unable to reach the Python server. Please verify that the API is running.");
        }
    }

    public async Task<AiSummaryData?> SummarizeAsync(string transcript, CancellationToken ct = default)
    {
        var client = clients.CreateClient(HttpClientName);
        var endpoint = configuration["PythonApiSettings:SummarizeEndpoint"] ?? "api/summarize";

        var response = await client.PostAsJsonAsync(endpoint, new { transkript = transcript }, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Python API Error: {response.StatusCode}");

        var ai = await response.Content.ReadFromJsonAsync<AiApiResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        return ai is { Success: true } ? ai.Summary : null;
    }

    public async Task<JsonElement> AnswerAsync(string question, IReadOnlyList<AiAnswerPassage> passages, CancellationToken ct = default)
    {
        var payload = new
        {
            soru = question,
            parcalar = passages.Select(passage => new
            {
                id = passage.Id,
                toplanti = passage.Toplanti,
                tarih = passage.Tarih,
                tur = passage.Tur,
                metin = passage.Metin
            }).ToArray()
        };

        var client = clients.CreateClient(HttpClientName);
        var endpoint = configuration["PythonApiSettings:AnswerEndpoint"] ?? "api/answer";
        using var response = await client.PostAsJsonAsync(endpoint, payload, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"AI engine answer failed: HTTP {(int)response.StatusCode}", null, response.StatusCode);

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return document.RootElement.Clone();
    }
}
