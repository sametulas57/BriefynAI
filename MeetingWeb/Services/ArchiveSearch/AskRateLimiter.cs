using Microsoft.Extensions.Options;

namespace MeetingWeb.Services.ArchiveSearch;

// Holds only user IDs and accepted question times, never question content.
public sealed class AskRateLimiter(TimeProvider clock, IOptions<AskOptions> options)
{
    private readonly Dictionary<string, Queue<DateTimeOffset>> _accepted = new();
    private readonly object _gate = new();

    public bool TryAccept(string? userId)
    {
        var key = userId ?? "anonymous";
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            if (!_accepted.TryGetValue(key, out var times))
                _accepted[key] = times = new Queue<DateTimeOffset>();
            while (times.Count > 0 && now - times.Peek() >= TimeSpan.FromMinutes(60)) times.Dequeue();
            if (times.Count >= Math.Max(1, options.Value.QuestionsPerHour)) return false;
            times.Enqueue(now);
            return true;
        }
    }
}
