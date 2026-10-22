using System.Collections.Concurrent;
using Hangfire;
using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Services
{
    public enum ZoomConnectOutcome { Connected, Failed, MissingScopes }

    public enum ZoomImportOutcome { Imported, AlreadyImported, NoTeam, LeftTeam, NotFound, NoAudio, SeveralParts, TooLarge, NotHost, ChoiceChanged }

    public record ZoomImportResult(ZoomImportOutcome Outcome, int? MeetingId = null);

    // The user has no Zoom connection (never made, or removed because Zoom rejected it).
    public class ZoomNotConnectedException : Exception
    {
        public ZoomNotConnectedException() : base("No Zoom connection.") { }
    }

    // Zoom rejected the stored grant and the connection has already been removed (inside the user's lock).
    public class ZoomConnectionRemovedException : ZoomGrantRejectedException { }

    // ADR-0011: a user's own Zoom connection and importing a cloud recording into the user's active team. Every method
    // takes the user id from the caller's session only; a connection is never used for another user.
    public interface IZoomService
    {
        bool IsEnabled { get; }
        bool IsAutoImportEnabled { get; }
        string AuthorizeUrl(string state);
        Task<ZoomConnectOutcome> ConnectAsync(string userId, string code);
        Task<ZoomConnection?> GetConnectionAsync(string userId);
        Task DisconnectAsync(string userId);
        Task RevokeQuietlyAsync(ZoomConnection connection);
        Task<IReadOnlyList<ZoomRecording>> ListRecentAsync(string userId);
        Task<ZoomImportResult> ImportAsync(string userId, string uuid);
        Task<bool> SetAutoImportAsync(string userId, int? workspaceId);
        // Hangfire job queued by Zoom's notification (ADR-0012); receives only Zoom's ids, never a token.
        Task AutoImportAsync(string zoomUserId, string uuid);
    }

    public class ZoomService : IZoomService
    {
        // Read-only permissions the app needs (ADR-0011); user:read:user is for showing which Zoom account is linked.
        // cloud_recording:read:recording is needed for the recording.completed event.
        public static readonly string[] RequiredScopes =
        {
            "cloud_recording:read:list_user_recordings",
            "cloud_recording:read:list_recording_files",
            "user:read:user",
            "cloud_recording:read:recording"
        };

        public static bool NeedsReconnect(string? grantedScopes)
        {
            // Unknown (linked before scopes were stored): asking once is safer than missing events silently.
            if (grantedScopes == null) return true;
            var granted = grantedScopes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return !RequiredScopes.All(granted.Contains);
        }

        private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
        // One refresh (or revoke) at a time per user inside this process; RowVersion covers other processes.
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> RefreshLocks = new();

        private readonly ApplicationDbContext _db;
        private readonly ZoomApiClient _api;
        private readonly ZoomOptions _options;
        private readonly IActiveWorkspaceService _activeWorkspace;
        private readonly IAudioStorage _audioStorage;
        private readonly IMeetingSeriesService _series;
        private readonly IBackgroundJobClient _jobs;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ZoomService> _logger;

        public ZoomService(ApplicationDbContext db, ZoomApiClient api, IOptions<ZoomOptions> options, IActiveWorkspaceService activeWorkspace,
            IAudioStorage audioStorage, IMeetingSeriesService series, IBackgroundJobClient jobs, IConfiguration configuration, ILogger<ZoomService> logger)
        {
            _db = db;
            _api = api;
            _options = options.Value;
            _activeWorkspace = activeWorkspace;
            _audioStorage = audioStorage;
            _series = series;
            _jobs = jobs;
            _configuration = configuration;
            _logger = logger;
        }

        public bool IsEnabled => _options.IsEnabled;
        public bool IsAutoImportEnabled => _options.IsWebhookEnabled;

        // A refresh mark older than this was left by a process that stopped: it outlives any token request (which is
        // cut off after TokenTimeoutSeconds), so a refresh still running elsewhere always keeps its mark.
        private TimeSpan RefreshMarkLifetime => TimeSpan.FromSeconds(2 * _options.TokenTimeoutSeconds);

        private long MaxBytes => _configuration.GetValue("Upload:MaxMegabytes", 300) * 1024L * 1024L;
        private const long MaxTranscriptBytes = 2 * 1024 * 1024;

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

            // Connecting again replaces the old tokens (one connection per user); waits for a disconnect in progress.
            var gate = Gate(userId);
            await gate.WaitAsync();
            try
            {
                return await SaveConnectionAsync(userId, me, tokens);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<ZoomConnectOutcome> SaveConnectionAsync(string userId, ZoomUser me, ZoomTokens tokens)
        {
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
                // RowVersion: a connection saved again meanwhile (another tab, another server process) stays.
                await _db.ZoomConnections.Where(z => z.Id == connection.Id && z.RowVersion == connection.RowVersion).ExecuteDeleteAsync();
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

        // ADR-0012: null turns automatic import off; a team is accepted only if the user is a member (ADR-0005).
        public async Task<bool> SetAutoImportAsync(string userId, int? workspaceId)
        {
            if (workspaceId != null && !IsAutoImportEnabled) return false;
            if (workspaceId != null && !await _db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId)) return false;
            return await _db.ZoomConnections.Where(z => z.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(z => z.AutoImportWorkspaceId, workspaceId)) > 0;
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
            return await ImportIntoAsync(userId, uuid, workspaceId.Value, requiredHostId: null);
        }

        // ADR-0012: runs for every connection of this Zoom user that has automatic import on. Ends quietly when there
        // is nothing to take; only a temporary Zoom failure is thrown, so Hangfire tries again.
        public async Task AutoImportAsync(string zoomUserId, string uuid)
        {
            if (!IsAutoImportEnabled || string.IsNullOrEmpty(uuid) || uuid.Length > 200) return;
            var connections = await _db.ZoomConnections.AsNoTracking()
                .Where(z => z.ZoomUserId == zoomUserId && z.AutoImportWorkspaceId != null)
                .Select(z => new { z.UserId, z.ZoomUserId, WorkspaceId = z.AutoImportWorkspaceId!.Value }).ToListAsync();
            foreach (var c in connections)
            {
                ZoomImportResult result;
                if (!await _db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == c.WorkspaceId && m.UserId == c.UserId))
                {
                    result = new(ZoomImportOutcome.LeftTeam);
                }
                else
                {
                    try
                    {
                        result = await ImportIntoAsync(c.UserId, uuid, c.WorkspaceId, requiredHostId: c.ZoomUserId);
                    }
                    catch (Exception ex) when (ex is ZoomGrantRejectedException or ZoomNotConnectedException)
                    {
                        // The connection is gone (Zoom rejected it, or it was removed meanwhile): nothing to retry.
                        _logger.LogWarning("Zoom automatic import for user {UserId} stopped: no working connection.", c.UserId);
                        continue;
                    }
                }

                if (result.Outcome == ZoomImportOutcome.LeftTeam)
                {
                    // Turned off only if it still points at the team the user left.
                    await _db.ZoomConnections.Where(z => z.UserId == c.UserId && z.AutoImportWorkspaceId == c.WorkspaceId)
                        .ExecuteUpdateAsync(s => s.SetProperty(z => z.AutoImportWorkspaceId, (int?)null));
                }
                if (result.Outcome is ZoomImportOutcome.NoAudio or ZoomImportOutcome.SeveralParts or ZoomImportOutcome.TooLarge or ZoomImportOutcome.LeftTeam)
                    _logger.LogWarning("Zoom automatic import for user {UserId} took nothing: {Outcome}.", c.UserId, result.Outcome);
                else
                    _logger.LogInformation("Zoom automatic import for user {UserId}: {Outcome}.", c.UserId, result.Outcome);
            }
        }

        // requiredHostId: automatic import takes only recordings the connected Zoom user hosted, as Zoom itself says.
        private async Task<ZoomImportResult> ImportIntoAsync(string userId, string uuid, int workspaceId, string? requiredHostId)
        {
            var existing = await ExistingAsync(workspaceId, uuid);
            if (existing != null) return new(ZoomImportOutcome.AlreadyImported, existing);

            // Always asked again from Zoom: nothing about the recording is taken from the page.
            var recording = await AsUserAsync(userId, token => _api.GetRecordingAsync(token, uuid));
            if (recording == null || string.IsNullOrEmpty(recording.Uuid) || recording.Uuid.Length > 200) return new(ZoomImportOutcome.NotFound);
            if (requiredHostId != null && recording.HostId != requiredHostId) return new(ZoomImportOutcome.NotHost);
            // Zoom also accepts the numeric meeting id here; the recording is stored and matched by its UUID.
            var recordingId = recording.Uuid;
            if (recordingId != uuid)
            {
                existing = await ExistingAsync(workspaceId, recordingId);
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

            if (recording.TranscriptFile is { } transcript)
            {
                if (transcript.FileSize > MaxTranscriptBytes)
                {
                    _logger.LogWarning("Zoom transcript was not saved.");
                }
                else
                {
                    try
                    {
                        var saved = await AsUserAsync(userId, token => _api.DownloadAsync(token, transcript.DownloadUrl,
                            stream => _audioStorage.SaveSidecarAsync(storedName, stream, MaxTranscriptBytes)));
                        if (!saved) _logger.LogWarning("Zoom transcript was not saved.");
                    }
                    catch (Exception ex) when (ex is ZoomApiException or ZoomGrantRejectedException or ZoomNotConnectedException
                                               or IOException or UnauthorizedAccessException)
                    {
                        _logger.LogWarning("Zoom transcript was not saved.");
                    }
                }
            }

            // The user may have left (or been removed from) the team while the file was downloading.
            if (!await _db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId))
            {
                _audioStorage.Delete(storedName);
                return new(ZoomImportOutcome.LeftTeam);
            }
            // Automatic import: the user may have turned it off or picked another team meanwhile.
            if (requiredHostId != null && !await _db.ZoomConnections.AnyAsync(z => z.UserId == userId && z.AutoImportWorkspaceId == workspaceId))
            {
                _audioStorage.Delete(storedName);
                return new(ZoomImportOutcome.ChoiceChanged);
            }

            int? seriesId = null;
            if (MeetingSeriesService.StartsSeries(recording.Type) && recording.MeetingNumber > 0)
                seriesId = await _series.FindOrCreateForZoomAsync(workspaceId, recording.MeetingNumber, recording.Topic, userId);

            var meeting = new Meeting
            {
                Title = string.IsNullOrWhiteSpace(recording.Topic) ? "Zoom toplantısı" : recording.Topic,
                WorkspaceId = workspaceId,
                Status = MeetingStatus.Processing,
                Source = MeetingSource.Zoom,
                ExternalRecordingId = recordingId,
                AudioFileName = storedName,
                SeriesId = seriesId,
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
                existing = await ExistingAsync(workspaceId, recordingId);
                if (existing == null) throw;
                return new(ZoomImportOutcome.AlreadyImported, existing);
            }

            try
            {
                _jobs.Enqueue<IMeetingProcessor>(processor => processor.ProcessMeetingJob(meeting.Id));
            }
            catch (Exception)
            {
                // Marked failed so the meeting page offers a retry (and the audio cleanup can see it) instead of
                // showing "in progress" forever.
                _logger.LogWarning("Analysis of imported meeting {MeetingId} could not be queued.", meeting.Id);
                meeting.Status = MeetingStatus.Failed;
                await _db.SaveChangesAsync();
            }
            return new(ZoomImportOutcome.Imported, meeting.Id);
        }

        private Task<int?> ExistingAsync(int workspaceId, string uuid) =>
            _db.Meetings.Where(m => m.WorkspaceId == workspaceId && m.ExternalRecordingId == uuid)
                .Select(m => (int?)m.Id).FirstOrDefaultAsync();

        // Runs a Zoom call with a valid access token; when Zoom rejects the grant the connection is removed, but only
        // if it still holds the rejected token (the user may have connected again meanwhile).
        private async Task<T> AsUserAsync<T>(string userId, Func<string, Task<T>> call)
        {
            string? used = null;
            try
            {
                var connection = await ValidConnectionAsync(userId);
                used = connection.AccessToken;
                return await call(used);
            }
            catch (ZoomGrantRejectedException ex) when (ex is not ZoomConnectionRemovedException)
            {
                var current = await GetConnectionAsync(userId);
                if (current == null || used == null) throw;
                if (current.AccessToken != used) throw new ZoomApiException(null);
                // Another process is saving a new pair; Zoom may already refuse the old token. Temporary, not broken.
                if (current.RefreshStartedAtUtc > DateTime.UtcNow - RefreshMarkLifetime) throw new ZoomApiException(null);
                // RowVersion: a connection saved between the read above and this delete is kept.
                await _db.ZoomConnections.Where(z => z.Id == current.Id && z.RowVersion == current.RowVersion).ExecuteDeleteAsync();
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

                // Another server process is refreshing right now; using the same refresh token here would make Zoom
                // reject one of the two. The mark is saved with RowVersion, so only one process wins it.
                if (connection.RefreshStartedAtUtc > DateTime.UtcNow - RefreshMarkLifetime) return StillValid(connection);
                connection.RefreshStartedAtUtc = DateTime.UtcNow;
                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    await ReloadAsync(connection);
                    return connection.AccessTokenExpiresAtUtc > DateTime.UtcNow + RefreshMargin ? connection : StillValid(connection);
                }

                var used = connection.RefreshToken;
                ZoomTokens tokens;
                try
                {
                    tokens = await _api.RefreshAsync(used);
                }
                catch (ZoomApiException)
                {
                    // Zoom could not be reached: the mark is lifted so the next request tries again.
                    connection.RefreshStartedAtUtc = null;
                    try { await _db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { }
                    throw;
                }
                catch (ZoomGrantRejectedException)
                {
                    // Another server process may have used this refresh token a moment ago and saved a newer one.
                    await ReloadAsync(connection);
                    if (connection.RefreshToken != used) return connection;

                    // Removed while the lock is held, so a reconnect waiting on it is saved after this, not deleted.
                    _db.ZoomConnections.Remove(connection);
                    try
                    {
                        await _db.SaveChangesAsync();
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        // Saved again by another server process meanwhile: that newer connection stays.
                        await ReloadAsync(connection);
                        return connection;
                    }
                    _logger.LogWarning("Zoom connection removed for user {UserId}: Zoom rejected it.", userId);
                    throw new ZoomConnectionRemovedException();
                }

                Apply(connection, tokens);
                connection.RefreshStartedAtUtc = null;
                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Someone else saved first (a reconnect, or a refresh after an old mark); theirs is kept and used.
                    await ReloadAsync(connection);
                }
                return connection;
            }
            finally
            {
                gate.Release();
            }
        }

        // Used while another process holds the refresh mark: the current token works until it actually expires.
        private static ZoomConnection StillValid(ZoomConnection connection) =>
            connection.AccessTokenExpiresAtUtc > DateTime.UtcNow ? connection : throw new ZoomApiException(null);

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
            if (!string.IsNullOrWhiteSpace(tokens.Scope)) connection.GrantedScopes = tokens.Scope;
        }
    }
}
