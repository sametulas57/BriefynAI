using Microsoft.EntityFrameworkCore;
using MeetingWeb.Data;
using MeetingWeb.Models;
using System.Net.Http.Json;
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

        [AutomaticRetry(Attempts = 0)]
        Task ResummarizeMeetingJob(int meetingId);
    }

    public class MeetingProcessor : IMeetingProcessor
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MeetingProcessor> _logger;
        private readonly IHubContext<MeetingHub> _hubContext;
        private readonly IAudioStorage _audioStorage;

        // Constructor injection for required dependencies.
        public MeetingProcessor(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<MeetingProcessor> logger,
            IHubContext<MeetingHub> hubContext,
            IAudioStorage audioStorage)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _hubContext = hubContext;
            _audioStorage = audioStorage;
        }

        public async Task ProcessMeetingJob(int meetingId, string audioPath)
        {
            _logger.LogInformation("Background job started. MeetingId: {MeetingId}, AudioPath: {AudioPath}", meetingId, audioPath);

            var meeting = await LoadAsync(meetingId);
            if (meeting == null) return;

            string? completedAudio = null;

            try
            {
                _logger.LogInformation("Dispatching audio file to Python AI API for MeetingId: {MeetingId}", meetingId);
                var ai = await RunPythonPipelineAsync(audioPath);

                // Faz 2: keep the transcript (encrypted) even when the summary failed, so a retry only redoes the summary.
                if (!string.IsNullOrWhiteSpace(ai?.Transkript)) meeting.Transkript = ai.Transkript;

                if (ai is { Success: true, Summary: not null })
                {
                    ApplySummary(meeting, ai.Summary);
                    meeting.Status = MeetingStatus.Completed;

                    // DATA MINIMIZATION (S2/KVKK): the recording has served its purpose once the summary exists.
                    completedAudio = meeting.AudioFilePath;
                    meeting.AudioFilePath = null;
                }
                else
                {
                    // An empty answer is a failed analysis; leaving it "Processing" made the meeting spin forever.
                    _logger.LogWarning("Python API returned no summary for MeetingId: {MeetingId}; marking as Failed.", meetingId);
                    meeting.Status = MeetingStatus.Failed;
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Processing complete. MeetingId: {MeetingId} saved successfully.", meetingId);

                // Delete only after the save succeeded; a leftover file is swept by AudioCleanupService.
                _audioStorage.Delete(completedAudio);

                // Push real-time completion notification to the meeting's workspace members via SignalR.
                await NotifyWorkspaceAsync(meetingId, meeting.WorkspaceId);
            }
            catch (Exception ex)
            {
                await MarkFailedAsync(meetingId, ex);
                // Rethrow to allow Hangfire to register the job as failed.
                throw;
            }
        }

        // Retry path once the recording is gone: the stored transcript is summarised again; no new speech-to-text.
        public async Task ResummarizeMeetingJob(int meetingId)
        {
            var meeting = await LoadAsync(meetingId);
            if (meeting == null) return;

            try
            {
                if (string.IsNullOrWhiteSpace(meeting.Transkript))
                    throw new InvalidOperationException("No stored transcript to summarise.");

                var summary = await RequestSummaryAsync(meeting.Transkript);
                if (summary != null)
                {
                    ApplySummary(meeting, summary);
                    meeting.Status = MeetingStatus.Completed;
                }
                else
                {
                    meeting.Status = MeetingStatus.Failed;
                }

                await _context.SaveChangesAsync();
                await NotifyWorkspaceAsync(meetingId, meeting.WorkspaceId);
            }
            catch (Exception ex)
            {
                await MarkFailedAsync(meetingId, ex);
                throw;
            }
        }

        private async Task<MeetingSummary?> LoadAsync(int meetingId)
        {
            var meeting = await _context.MeetingSummaries
                .Include(m => m.AksiyonMaddeleri)
                .FirstOrDefaultAsync(m => m.Id == meetingId);
            if (meeting == null)
                _logger.LogWarning("MeetingId: {MeetingId} not found in database. Aborting job.", meetingId);
            return meeting;
        }

        private async Task MarkFailedAsync(int meetingId, Exception ex)
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

                // Push real-time error notification to the meeting's workspace members.
                await NotifyWorkspaceAsync(meetingId, errorMeeting.WorkspaceId);
            }
        }

        // Entries users added by hand are stored as "text|~|authorId"; they survive a regenerated summary.
        private static bool IsManual(string? value) => value != null && value.Contains("|~|");

        private static void ApplySummary(MeetingSummary meeting, AiSummaryData data)
        {
            if (!string.IsNullOrWhiteSpace(data.ToplantiKonusu)) meeting.ToplantiKonusu = data.ToplantiKonusu;
            meeting.GorusulenKonular = (data.GorusulenKonular ?? new()).Concat(meeting.GorusulenKonular.Where(IsManual)).ToList();
            meeting.AlinanKararlar = (data.AlinanKararlar ?? new()).Concat(meeting.AlinanKararlar.Where(IsManual)).ToList();

            // Earlier AI-generated tasks are replaced (removed items are deleted as orphans); manual ones stay,
            // and so do AI tasks someone has assigned or dated (aksiyon takibi).
            meeting.AksiyonMaddeleri.RemoveAll(a => !IsManual(a.Text) && !a.IsTracked);
            foreach (var taskText in data.AksiyonMaddeleri ?? new())
            {
                meeting.AksiyonMaddeleri.Add(new ActionItem { Text = taskText, IsDone = false });
            }
        }

        private async Task<AiSummaryData?> RequestSummaryAsync(string transcript)
        {
            var client = _httpClientFactory.CreateClient("PythonApi");
            var endpoint = _configuration["PythonApiSettings:SummarizeEndpoint"] ?? "api/summarize";

            var response = await client.PostAsJsonAsync(endpoint, new { transkript = transcript });
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Python API Error: {response.StatusCode}");

            var ai = await response.Content.ReadFromJsonAsync<AiApiResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return ai is { Success: true } ? ai.Summary : null;
        }

        // S3: resolve recipients from current workspace membership at send time (not from hub groups),
        // so a member removed from the workspace stops receiving notifications immediately.
        private async Task NotifyWorkspaceAsync(int meetingId, int? workspaceId)
        {
            if (workspaceId == null) return;

            var memberIds = await _context.WorkspaceUsers
                .Where(wu => wu.WorkspaceId == workspaceId)
                .Select(wu => wu.UserId)
                .ToListAsync();

            await _hubContext.Clients.Users(memberIds).SendAsync("MeetingProcessed", meetingId);
        }

        // Handles secure HTTP multipart/form-data communication with the external Python API.
        private async Task<AiApiResponse?> RunPythonPipelineAsync(string audioPath)
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

                    return aiResponse;
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