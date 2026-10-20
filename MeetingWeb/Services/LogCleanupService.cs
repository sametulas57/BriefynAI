using MeetingWeb.Data;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Services
{
    // ADR-0009: the Logs table keeps warnings and errors for a limited time; this daily job removes older rows.
    public interface ILogCleanupService
    {
        Task<int> RunAsync();
    }

    public class LogCleanupService : ILogCleanupService
    {
        private readonly ApplicationDbContext _context;
        private readonly TimeSpan _retention;

        public LogCleanupService(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _retention = TimeSpan.FromDays(configuration.GetValue("Logging:DatabaseRetentionDays", 30));
        }

        public Task<int> RunAsync()
        {
            var cutoff = DateTime.UtcNow - _retention;
            return _context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Logs] WHERE [TimeStamp] < {cutoff}");
        }
    }
}
