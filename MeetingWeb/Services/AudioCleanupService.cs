using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Services
{
    // Recordings are deleted right after a successful analysis (see MeetingProcessor).
    // This daily job removes what is left behind: audio of meetings that stayed Failed past the retry window,
    // and files no meeting references any more (e.g. a delete that failed because the file was locked).
    public interface IAudioCleanupService
    {
        Task RunAsync();
        Task<int> CleanupAsync(DateTime utcNow);
    }

    public class AudioCleanupService : IAudioCleanupService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAudioStorage _storage;
        private readonly TimeSpan _retention;
        private readonly ILogger<AudioCleanupService> _logger;

        public AudioCleanupService(ApplicationDbContext context, IAudioStorage storage, IConfiguration configuration, ILogger<AudioCleanupService> logger)
        {
            _context = context;
            _storage = storage;
            _retention = TimeSpan.FromDays(configuration.GetValue("AudioStorage:FailedRetentionDays", 7));
            _logger = logger;
        }

        public Task RunAsync() => CleanupAsync(DateTime.UtcNow);

        public async Task<int> CleanupAsync(DateTime utcNow)
        {
            var cutoff = utcNow - _retention;
            var deleted = 0;

            // 1. Failed meetings whose retry window has passed.
            var expired = await _context.Meetings
                .Where(m => m.Status == MeetingStatus.Failed && m.AudioFileName != null && m.CreatedAtUtc < cutoff)
                .ToListAsync();
            foreach (var meeting in expired)
            {
                if (_storage.Delete(meeting.AudioFileName)) deleted++;
                meeting.AudioFileName = null;
            }
            await _context.SaveChangesAsync();

            // 2. Files in the private store that no meeting points to, once they are older than the window.
            var referenced = (await _context.Meetings
                    .Where(m => m.AudioFileName != null)
                    .Select(m => m.AudioFileName!)
                    .ToListAsync())
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var file in new DirectoryInfo(_storage.RootPath).EnumerateFiles())
            {
                if (referenced.Contains(file.Name) || file.LastWriteTimeUtc >= cutoff) continue;
                if (_storage.Delete(file.Name)) deleted++;
            }

            _logger.LogInformation("Audio cleanup removed {Count} file(s).", deleted);
            return deleted;
        }
    }
}
