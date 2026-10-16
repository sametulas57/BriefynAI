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

        public string Create(Job job, IState state)
        {
            Created.Add(job);
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;

        public int CountFor(string methodName, Func<IReadOnlyList<object?>, bool>? argsMatch = null) =>
            Created.Count(j => j.Method.Name == methodName && (argsMatch == null || argsMatch(j.Args)));
    }
}
