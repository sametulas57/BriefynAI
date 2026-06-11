using Microsoft.EntityFrameworkCore;
using MeetingWeb.Data;
using MeetingWeb.Models;
using System.Text.Json;
using Hangfire;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using MeetingWeb.Hubs;

namespace MeetingWeb.Services
{
    public interface IMeetingProcessor
    {
        [AutomaticRetry(Attempts = 0)]
        Task ProcessMeetingJob(int meetingId, string audioPath);
    }

    public class MeetingProcessor : IMeetingProcessor
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MeetingProcessor> _logger;
        private readonly IHubContext<MeetingHub> _hubContext;

        // Constructor injection for required dependencies.
        public MeetingProcessor(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<MeetingProcessor> logger,
            IHubContext<MeetingHub> hubContext)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _hubContext = hubContext;
        }

        public async Task ProcessMeetingJob(int meetingId, string audioPath)
        {
            _logger.LogInformation("Background job started. MeetingId: {MeetingId}, AudioPath: {AudioPath}", meetingId, audioPath);

            var meeting = await _context.MeetingSummaries
                                        .Include(m => m.AksiyonMaddeleri)
                                        .FirstOrDefaultAsync(m => m.Id == meetingId);

            if (meeting == null)
            {
                _logger.LogWarning("MeetingId: {MeetingId} not found in database. Aborting job.", meetingId);
                return;
            }

            try
            {
                _logger.LogInformation("Dispatching audio file to Python AI API for MeetingId: {MeetingId}", meetingId);

                // Execute the Python AI pipeline to retrieve the parsed summary DTO.
                var aiSummaryData = await RunPythonPipelineAsync(audioPath);

                if (aiSummaryData != null)
                {
                    _logger.LogInformation("Successfully received AI payload. Initiating database mapping.");

                    // Map the validated DTO data to the EF Core entity model.
                    meeting.ToplantiKonusu = aiSummaryData.ToplantiKonusu ?? "No Subject Found";
                    meeting.GorusulenKonular = aiSummaryData.GorusulenKonular ?? new List<string>();
                    meeting.AlinanKararlar = aiSummaryData.AlinanKararlar ?? new List<string>();
                    meeting.AksiyonMaddeleri ??= new List<ActionItem>();

                    if (aiSummaryData.AksiyonMaddeleri != null)
                    {
                        foreach (var taskText in aiSummaryData.AksiyonMaddeleri)
                        {
                            meeting.AksiyonMaddeleri.Add(new ActionItem { Text = taskText, IsDone = false });
                        }
                    }

                    // Update meeting status upon successful processing.
                    meeting.Status = MeetingStatus.Completed;
                }
                else
                {
                    _logger.LogWarning("Python API executed successfully but returned a null payload.");
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Processing complete. MeetingId: {MeetingId} saved successfully.", meetingId);

                // Push real-time completion notification to connected clients via SignalR.
                await _hubContext.Clients.All.SendAsync("MeetingProcessed", meetingId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical failure during processing for MeetingId: {MeetingId}", meetingId);

                // Clear EF Core tracked entities to prevent saving corrupted states.
                _context.ChangeTracker.Clear();

                var errorMeeting = await _context.MeetingSummaries.FindAsync(meetingId);
                if (errorMeeting != null)
                {
                    errorMeeting.Status = MeetingStatus.Failed;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Status reverted to Failed for MeetingId: {MeetingId}", meetingId);

                    // Push real-time error notification to connected clients.
                    await _hubContext.Clients.All.SendAsync("MeetingProcessed", meetingId);
                }

                // Rethrow to allow Hangfire to register the job as failed.
                throw;
            }
        }

        // Handles secure HTTP multipart/form-data communication with the external Python API.
        private async Task<AiSummaryData?> RunPythonPipelineAsync(string audioPath)
        {
            if (!System.IO.File.Exists(audioPath))
            {
                _logger.LogError("Uploaded audio file not found! Path: {AudioPath}", audioPath);
                throw new FileNotFoundException($"Uploaded audio file not found! Path: {audioPath}");
            }

            // Create an HttpClient instance using the configured factory to prevent socket exhaustion.
            var client = _httpClientFactory.CreateClient("PythonApi");
            var endpoint = _configuration["PythonApiSettings:ProcessEndpoint"];

            // Utilize 'using var' to ensure unmanaged memory resources are disposed immediately.
            using var form = new MultipartFormDataContent();
            using var fileStream = new FileStream(audioPath, FileMode.Open, FileAccess.Read);
            using var streamContent = new StreamContent(fileStream);

            streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");
            form.Add(streamContent, "file", Path.GetFileName(audioPath));

            try
            {
                _logger.LogInformation("Executing POST request to {Endpoint}", endpoint);

                HttpResponseMessage response = await client.PostAsync(endpoint, form);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Received 200 OK from Python API. Deserializing payload...");
                    string jsonResponse = await response.Content.ReadAsStringAsync();

                    // Deserialize the JSON response into a safe Data Transfer Object (DTO).
                    var aiResponse = JsonSerializer.Deserialize<AiApiResponse>(jsonResponse, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return aiResponse is { Success: true } ? aiResponse.Summary : null;
                }

                string errorDetail = await response.Content.ReadAsStringAsync();
                _logger.LogError("Python API responded with status code: {StatusCode}. Details: {ErrorDetail}", response.StatusCode, errorDetail);
                throw new HttpRequestException($"Python API Error: {response.StatusCode} - {errorDetail}");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to connect to the Python API server.");
                throw new Exception("Unable to reach the Python server. Please verify that the API is running.");
            }
        }
    }
}