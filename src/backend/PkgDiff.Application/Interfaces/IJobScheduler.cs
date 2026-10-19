using System.Linq.Expressions;

namespace PkgDiff.Application.Interfaces;

// Queues work for the background workers. The caller names the service and the call; which job
// system stores and runs it is not its concern.
public interface IJobScheduler
{
    string Enqueue<T>(Expression<Func<T, Task>> job);

    string Schedule<T>(Expression<Func<T, Task>> job, TimeSpan delay);
}
