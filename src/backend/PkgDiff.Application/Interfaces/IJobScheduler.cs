using System.Linq.Expressions;

namespace PkgDiff.Application.Interfaces;

public interface IJobScheduler
{
    string Enqueue<T>(Expression<Func<T, Task>> job);

    string Schedule<T>(Expression<Func<T, Task>> job, TimeSpan delay);
}
