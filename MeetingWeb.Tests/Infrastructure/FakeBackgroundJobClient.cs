using System.Collections.Concurrent;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace MeetingWeb.Tests.Infrastructure
{
    // Records enqueued jobs instead of running them, so tests can assert what would have been sent to Hangfire.
    public class FakeBackgroundJobClient : IBackgroundJobClient
    {
        public ConcurrentBag<Job> Created { get; } = new();
        // The state each job was created in (enqueued now, or scheduled for later).
        public ConcurrentDictionary<Job, IState> States { get; } = new();

        // The next Create throws, like Hangfire when its storage is unreachable.
        public bool FailNext { get; set; }

        public string Create(Job job, IState state)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("job storage unavailable");
            }
            Created.Add(job);
            States[job] = state;
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;

        public int CountFor(string methodName, Func<IReadOnlyList<object?>, bool>? argsMatch = null) =>
            Created.Count(j => j.Method.Name == methodName && (argsMatch == null || argsMatch(j.Args)));
    }
}
