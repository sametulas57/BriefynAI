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
        // ADR-0009: jobs carry only the meeting id; the recording is resolved from the meeting at run time.
        [AutomaticRetry(Attempts = 0)]
        Task ProcessMeetingJob(int meetingId);

        [AutomaticRetry(Attempts = 0)]
        Task ResummarizeMeetingJob(int meetingId);
    }

    public class MeetingProcessor : IMeetingProcessor
    {
        // Named HttpClient for the AI engine, configured in Program.cs (base address, API key).
        public const string PythonApiClient = "PythonApi";

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

        public async Task ProcessMeetingJob(int meetingId)
        {
            _logger.LogInformation("Background job started. MeetingId: {MeetingId}", meetingId);

            var meeting = await LoadAsync(meetingId);
            if (meeting == null) return;

            string? completedAudio = null;

            try
            {
                if (string.IsNullOrEmpty(meeting.AudioFileName))
                    throw new FileNotFoundException($"Meeting {meetingId} has no stored recording.");

                _logger.LogInformation("Dispatching audio file to Python AI API for MeetingId: {MeetingId}", meetingId);
                var ai = await RunPythonPipelineAsync(_audioStorage.GetPath(meeting.AudioFileName));

                meeting = await SaveOnFreshCopyAsync(meetingId, m =>
                {
                    // Faz 2: keep the transcript (encrypted) even when the summary failed, so a retry only redoes the summary.
                    if (!string.IsNullOrWhiteSpace(ai?.Transkript))
                    {
                        m.Transcript ??= new MeetingTranscript();
                        m.Transcript.Text = ai.Transkript;
                    }

                    if (ai is { Success: true, Summary: not null })
                    {
                        ApplySummary(m, ai.Summary);

                        // DATA MINIMIZATION (S2/KVKK): the recording has served its purpose once the summary exists.
                        completedAudio = m.AudioFileName;
                        m.AudioFileName = null;
                    }
                    else
                    {
                        // An empty answer is a failed analysis; leaving it "Processing" made the meeting spin forever.
                        _logger.LogWarning("Python API returned no summary for MeetingId: {MeetingId}; marking as Failed.", meetingId);
                        m.Status = MeetingStatus.Failed;
                    }
                });
                if (meeting == null) return;
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
                if (string.IsNullOrWhiteSpace(meeting.Transcript?.Text))
                    throw new InvalidOperationException("No stored transcript to summarise.");

                var summary = await RequestSummaryAsync(meeting.Transcript.Text);
                meeting = await SaveOnFreshCopyAsync(meetingId, m =>
                {
                    if (summary != null) ApplySummary(m, summary);
                    else m.Status = MeetingStatus.Failed;
                });
                if (meeting == null) return;
                await NotifyWorkspaceAsync(meetingId, meeting.WorkspaceId);
            }
            catch (Exception ex)
            {
                await MarkFailedAsync(meetingId, ex);
                throw;
            }
        }

        // The AI call takes minutes; meanwhile users may assign or complete tasks. The result is therefore applied to a
        // freshly loaded copy, and if someone still changed a row in between (RowVersion), it is applied again.
        private async Task<Meeting?> SaveOnFreshCopyAsync(int meetingId, Action<Meeting> apply)
        {
            for (var attempt = 1; ; attempt++)
            {
                _context.ChangeTracker.Clear();
                var meeting = await LoadAsync(meetingId);
                if (meeting == null) return null;

                apply(meeting);
                try
                {
                    await _context.SaveChangesAsync();
                    return meeting;
                }
                catch (DbUpdateConcurrencyException) when (attempt < 3)
                {
                    _logger.LogWarning("MeetingId: {MeetingId} changed while saving the summary; applying it again.", meetingId);
                }
            }
        }

        private async Task<Meeting?> LoadAsync(int meetingId)
        {
            var meeting = await _context.Meetings
                .Include(m => m.Transcript)
                .Include(m => m.Points)
                .Include(m => m.ActionItems)
                .AsSplitQuery()
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

            var errorMeeting = await _context.Meetings.FindAsync(meetingId);
            if (errorMeeting != null)
            {
                errorMeeting.Status = MeetingStatus.Failed;
                await _context.SaveChangesAsync();
                _logger.LogInformation("Status reverted to Failed for MeetingId: {MeetingId}", meetingId);

                // Push real-time error notification to the meeting's workspace members.
                await NotifyWorkspaceAsync(meetingId, errorMeeting.WorkspaceId);
            }
        }

        private static bool IsTracked(ActionItem a) => a.IsDone || a.AssigneeUserId != null || a.DueDate != null || a.CompletedByUserId != null;

        private static void ApplySummary(Meeting meeting, AiSummaryData data)
        {
            if (!string.IsNullOrWhiteSpace(data.ToplantiKonusu)) meeting.Title = data.ToplantiKonusu;
            meeting.Status = MeetingStatus.Completed;
            meeting.CompletedAtUtc = DateTime.UtcNow;

            // Lines users added by hand are separate rows and survive a regenerated summary; earlier AI lines are
            // replaced (removed rows are deleted as orphans).
            meeting.Points.RemoveAll(p => p.Source == EntrySource.Ai);
            AddPoints(MeetingPointKind.Topic, data.GorusulenKonular);
            AddPoints(MeetingPointKind.Decision, data.AlinanKararlar);

            // Earlier AI tasks are replaced too, except those someone has assigned or completed: regenerating the
            // summary must not erase that work.
            meeting.ActionItems.RemoveAll(a => a.Source == EntrySource.Ai && !IsTracked(a));
            var kept = meeting.ActionItems.Where(a => a.Source == EntrySource.Ai).Select(a => a.Text).ToHashSet();
            var nextOrder = meeting.ActionItems.Select(a => a.SortOrder + 1).DefaultIfEmpty(0).Max();
            foreach (var taskText in data.AksiyonMaddeleri ?? new())
            {
                if (kept.Contains(taskText)) continue;
                meeting.ActionItems.Add(new ActionItem { Text = taskText, Source = EntrySource.Ai, SortOrder = nextOrder++ });
            }

            void AddPoints(MeetingPointKind kind, List<string>? texts)
            {
                var order = 0;
                foreach (var text in texts ?? new())
                    meeting.Points.Add(new MeetingPoint { Kind = kind, Text = text, Source = EntrySource.Ai, SortOrder = order++ });
            }
        }

        private async Task<AiSummaryData?> RequestSummaryAsync(string transcript)
        {
            var client = _httpClientFactory.CreateClient(PythonApiClient);
            var endpoint = _configuration["PythonApiSettings:SummarizeEndpoint"] ?? "api/summarize";

            var response = await client.PostAsJsonAsync(endpoint, new { transkript = transcript });
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Python API Error: {response.StatusCode}");

            var ai = await response.Content.ReadFromJsonAsync<AiApiResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return ai is { Success: true } ? ai.Summary : null;
        }

        // S3: resolve recipients from current workspace membership at send time (not from hub groups),
        // so a member removed from the workspace stops receiving notifications immediately.
        private async Task NotifyWorkspaceAsync(int meetingId, int workspaceId)
        {
            var memberIds = await _context.WorkspaceMembers
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
            var client = _httpClientFactory.CreateClient(PythonApiClient);
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