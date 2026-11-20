using MeetingWeb.Models;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Services;

// Holds only user IDs and accepted times. In-memory: resets on restart and is per instance (ADR-0016).
public sealed class PasswordResetRateLimiter(TimeProvider clock, IOptions<PasswordResetOptions> options)
{
    private readonly Dictionary<string, Queue<DateTimeOffset>> _accepted = new();
    private readonly object _gate = new();

    public bool TryAccept(string userId)
    {
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            if (!_accepted.TryGetValue(userId, out var times))
                _accepted[userId] = times = new Queue<DateTimeOffset>();
            while (times.Count > 0 && now - times.Peek() >= TimeSpan.FromMinutes(60)) times.Dequeue();
            if (times.Count >= Math.Max(1, options.Value.EmailsPerAccountPerHour)) return false;
            times.Enqueue(now);
            return true;
        }
    }
}
