using System.Linq.Expressions;
using Hangfire;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.API.Services;

public class HangfireJobScheduler : IJobScheduler
{
    private readonly IBackgroundJobClient _client;

    public HangfireJobScheduler(IBackgroundJobClient client)
    {
        _client = client;
    }

    public string Enqueue<T>(Expression<Func<T, Task>> job) => _client.Enqueue(job);

    public string Schedule<T>(Expression<Func<T, Task>> job, TimeSpan delay) => _client.Schedule(job, delay);
}
