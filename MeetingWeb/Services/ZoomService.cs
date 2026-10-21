using System.Collections.Concurrent;
using Hangfire;
using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Services
{
    public enum ZoomConnectOutcome { Connected, Failed, MissingScopes }

    public enum ZoomImportOutcome { Imported, AlreadyImported, NoTeam, LeftTeam, NotFound, NoAudio, SeveralParts, TooLarge }

    public record ZoomImportResult(ZoomImportOutcome Outcome, int? MeetingId = null);

    // The user has no Zoom connection (never made, or removed because Zoom rejected it).
    public class ZoomNotConnectedException : Exception
    {
        public ZoomNotConnectedException() : base("No Zoom connection.") { }
    }

    // ADR-0011: a user's own Zoom connection and importing a cloud recording into the user's active team. Every method
    // takes the user id from the caller's session only; a connection is never used for another user.
    public interface IZoomService
    {
        bool IsEnabled { get; }
        string AuthorizeUrl(string state);
        Task<ZoomConnectOutcome> ConnectAsync(string userId, string code);
        Task<ZoomConnection?> GetConnectionAsync(string userId);
        Task DisconnectAsync(string userId);
        Task RevokeQuietlyAsync(ZoomConnection connection);
        Task<IReadOnlyList<ZoomRecording>> ListRecentAsync(string userId);
        Task<ZoomImportResult> ImportAsync(string userId, string uuid);
    }

    public class ZoomService : IZoomService
    {
        // Read-only permissions the app needs (ADR-0011); user:read:user is for showing which Zoom account is linked.
        public static readonly string[] RequiredScopes =
        {
            "cloud_recording:read:list_user_recordings",
            "cloud_recording:read:list_recording_files",
            "user:read:user"
        };

        private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
        // One refresh (or revoke) at a time per user inside this process; RowVersion covers other processes.
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> RefreshLocks = new();

        private readonly ApplicationDbContext _db;
        private readonly ZoomApiClient _api;
        private readonly ZoomOptions _options;
        private readonly IActiveWorkspaceService _activeWorkspace;
        private readonly IAudioStorage _audioStorage;
        private readonly IBackgroundJobClient _jobs;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ZoomService> _logger;

        public ZoomService(ApplicationDbContext db, ZoomApiClient api, IOptions<ZoomOptions> options, IActiveWorkspaceService activeWorkspace,
            IAudioStorage audioStorage, IBackgroundJobClient jobs, IConfiguration configuration, ILogger<ZoomService> logger)
        {
            _db = db;
            _api = api;
            _options = options.Value;
            _activeWorkspace = activeWorkspace;
            _audioStorage = audioStorage;
            _jobs = jobs;
            _configuration = configuration;
            _logger = logger;
        }

        public bool IsEnabled => _options.IsEnabled;

        private long MaxBytes => _configuration.GetValue("Upload:MaxMegabytes", 300) * 1024L * 1024L;

        public string AuthorizeUrl(string state) => _api.AuthorizeUrl(state);

        public async Task<ZoomConnectOutcome> ConnectAsync(string userId, string code)
        {
            ZoomTokens tokens;
            ZoomUser me;
            try
            {
                tokens = await _api.ExchangeCodeAsync(code);
                var granted = tokens.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (!RequiredScopes.All(granted.Contains)) return ZoomConnectOutcome.MissingScopes;
                me = await _api.GetMeAsync(tokens.AccessToken);
            }
            catch (Exception ex) when (ex is ZoomApiException or ZoomGrantRejectedException)
            {
                _logger.LogWarning("Zoom connection could not be completed for user {UserId}: {Reason}", userId, ex.Message);
                return ZoomConnectOutcome.Failed;
            }

            // Connecting again replaces the old tokens (one connection per user).
            var connection = await _db.ZoomConnections.SingleOrDefaultAsync(z => z.UserId == userId);
            if (connection == null)
            {
                connection = new ZoomConnection { UserId = userId };
                _db.ZoomConnections.Add(connection);
            }
            connection.ZoomUserId = me.Id;
            connection.ZoomEmail = me.Email;
            connection.ConnectedAtUtc = DateTime.UtcNow;
            Apply(connection, tokens);
            try
            {
                await _db.SaveChangesAsync();
                return ZoomConnectOutcome.Connected;
            }
            catch (DbUpdateException)
            {
                // A second callback for the same user saved first.
                _db.ChangeTracker.Clear();
                return ZoomConnectOutcome.Failed;
            }
        }

        public Task<ZoomConnection?> GetConnectionAsync(string userId) =>
            _db.ZoomConnections.AsNoTracking().SingleOrDefaultAsync(z => z.UserId == userId);

        // The row is deleted even when Zoom cannot be reached. Waits for a refresh in progress, so the newest token is
        // the one revoked.
        public async Task DisconnectAsync(string userId)
        {
            var gate = Gate(userId);
            await gate.WaitAsync();
            try
            {
                var connection = await GetConnectionAsync(userId);
                if (connection == null) return;
                await RevokeCoreAsync(connection);
                await _db.ZoomConnections.Where(z => z.UserId == userId).ExecuteDeleteAsync();
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task RevokeQuietlyAsync(ZoomConnection connection)
        {
            var gate = Gate(connection.UserId);
            await gate.WaitAsync();
            try
            {
                await RevokeCoreAsync(connection);
            }
            finally
            {
                gate.Release();
            }
        }

        private static SemaphoreSlim Gate(string userId) => RefreshLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

        private async Task RevokeCoreAsync(ZoomConnection connection)
        {
            if (!IsEnabled) return;
            try
            {
                // Zoom revokes only a valid access token; an expired one is refreshed first (the row is deleted anyway).
                var accessToken = connection.AccessToken;
                if (connection.AccessTokenExpiresAtUtc <= DateTime.UtcNow + RefreshMargin)
                    accessToken = (await _api.RefreshAsync(connection.RefreshToken)).AccessToken;
                await _api.RevokeAsync(accessToken);
            }
            catch (Exception ex) when (ex is ZoomApiException or ZoomGrantRejectedException)
            {
                _logger.LogWarning("Zoom token could not be revoked for user {UserId}: {Reason}", connection.UserId, ex.Message);
            }
        }

        public async Task<IReadOnlyList<ZoomRecording>> ListRecentAsync(string userId)
        {
            var to = DateTime.UtcNow.Date;
            return await AsUserAsync(userId, token => _api.ListRecordingsAsync(token, to.AddDays(-30), to));
        }

        public async Task<ZoomImportResult> ImportAsync(string userId, string uuid)
        {
            if (!await _db.ZoomConnections.AnyAsync(z => z.UserId == userId)) throw new ZoomNotConnectedException();
            if (string.IsNullOrWhiteSpace(uuid) || uuid.Length > 200) return new(ZoomImportOutcome.NotFound);

            // Same check as the upload page (S7): the remembered team is re-checked against current membership.
            var workspaceId = (await _activeWorkspace.GetActiveMembershipAsync(userId))?.WorkspaceId;
            if (workspaceId == null) return new(ZoomImportOutcome.NoTeam);

            var existing = await ExistingAsync(workspaceId.Value, uuid);
            if (existing != null) return new(ZoomImportOutcome.AlreadyImported, existing);

            // Always asked again from Zoom: nothing about the recording is taken from the page.
            var recording = await AsUserAsync(userId, token => _api.GetRecordingAsync(token, uuid));
            if (recording == null || string.IsNullOrEmpty(recording.Uuid) || recording.Uuid.Length > 200) return new(ZoomImportOutcome.NotFound);
            // Zoom also accepts the numeric meeting id here; the recording is stored and matched by its UUID.
            var recordingId = recording.Uuid;
            if (recordingId != uuid)
            {
                existing = await ExistingAsync(workspaceId.Value, recordingId);
                if (existing != null) return new(ZoomImportOutcome.AlreadyImported, existing);
            }
            if (recording.AudioFiles.Count == 0) return new(ZoomImportOutcome.NoAudio);
            // Taking one part would silently leave the rest of the meeting out of the summary.
            if (recording.AudioFiles.Count > 1) return new(ZoomImportOutcome.SeveralParts);
            var audio = recording.AudioFiles[0];
            if (audio.FileSize > MaxBytes) return new(ZoomImportOutcome.TooLarge);

            var storedName = await AsUserAsync(userId, token => _api.DownloadAsync(token, audio.DownloadUrl,
                stream => _audioStorage.SaveAsync(stream, "zoom.m4a", MaxBytes)));
            if (storedName == null) return new(ZoomImportOutcome.TooLarge);

            // The user may have left (or been removed from) the team while the file was downloading.
            if (!await _db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId))
            {
                _audioStorage.Delete(storedName);
                return new(ZoomImportOutcome.LeftTeam);
            }

            var meeting = new Meeting
            {
                Title = string.IsNullOrWhiteSpace(recording.Topic) ? "Zoom toplantısı" : recording.Topic,
                WorkspaceId = workspaceId.Value,
                Status = MeetingStatus.Processing,
                Source = MeetingSource.Zoom,
                ExternalRecordingId = recordingId,
                AudioFileName = storedName,
                CreatedByUserId = userId
            };
            _db.Meetings.Add(meeting);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // The same recording was taken into this team a moment ago (unique index): keep that one.
                _db.ChangeTracker.Clear();
                _audioStorage.Delete(storedName);
                existing = await ExistingAsync(workspaceId.Value, recordingId);
                if (existing == null) throw;
                return new(ZoomImportOutcome.AlreadyImported, existing);
            }

            _jobs.Enqueue<IMeetingProcessor>(processor => processor.ProcessMeetingJob(meeting.Id));
            return new(ZoomImportOutcome.Imported, meeting.Id);
        }

        private Task<int?> ExistingAsync(int workspaceId, string uuid) =>
            _db.Meetings.Where(m => m.WorkspaceId == workspaceId && m.ExternalRecordingId == uuid)
                .Select(m => (int?)m.Id).FirstOrDefaultAsync();

        // Runs a Zoom call with a valid access token; when Zoom rejects the grant the connection is removed.
        private async Task<T> AsUserAsync<T>(string userId, Func<string, Task<T>> call)
        {
            try
            {
                var connection = await ValidConnectionAsync(userId);
                return await call(connection.AccessToken);
            }
            catch (ZoomGrantRejectedException)
            {
                await _db.ZoomConnections.Where(z => z.UserId == userId).ExecuteDeleteAsync();
                _logger.LogWarning("Zoom connection removed for user {UserId}: Zoom rejected it.", userId);
                throw;
            }
        }

        // Refreshes the access token when less than five minutes are left. Zoom replaces the refresh token on every
        // refresh, so the new one is saved at once.
        private async Task<ZoomConnection> ValidConnectionAsync(string userId)
        {
            var connection = await _db.ZoomConnections.SingleOrDefaultAsync(z => z.UserId == userId)
                ?? throw new ZoomNotConnectedException();
            if (connection.AccessTokenExpiresAtUtc > DateTime.UtcNow + RefreshMargin) return connection;

            var gate = Gate(userId);
            await gate.WaitAsync();
            try
            {
                // Another request may have refreshed while this one waited.
                await ReloadAsync(connection);
                if (connection.AccessTokenExpiresAtUtc > DateTime.UtcNow + RefreshMargin) return connection;

                var used = connection.RefreshToken;
                ZoomTokens tokens;
                try
                {
                    tokens = await _api.RefreshAsync(used);
                }
                catch (ZoomGrantRejectedException)
                {
                    // Another server process may have used this refresh token a moment ago and saved a newer one.
                    await ReloadAsync(connection);
                    if (connection.RefreshToken != used) return connection;
                    throw;
                }

                Apply(connection, tokens);
                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Someone else saved a refresh first; theirs is the latest pair, so this request uses it.
                    await ReloadAsync(connection);
                }
                return connection;
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task ReloadAsync(ZoomConnection connection)
        {
            var entry = _db.Entry(connection);
            await entry.ReloadAsync();
            if (entry.State == EntityState.Detached) throw new ZoomNotConnectedException();
        }

        private static void Apply(ZoomConnection connection, ZoomTokens tokens)
        {
            connection.AccessToken = tokens.AccessToken;
            connection.RefreshToken = tokens.RefreshToken;
            connection.AccessTokenExpiresAtUtc = tokens.ExpiresAtUtc;
        }
    }
}
