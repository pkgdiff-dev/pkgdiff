using Hangfire.Client;
using Hangfire.Server;
using PkgDiff.Application.Jobs;

namespace PkgDiff.API.Filters;

public class JobTriggerFilter : IClientFilter, IServerFilter
{
    private static readonly AsyncLocal<string?> CurrentJob = new();
    private readonly IHttpContextAccessor _httpContextAccessor;

    public JobTriggerFilter(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void OnCreating(CreatingContext context)
    {
        var trigger = CurrentJob.Value ?? DescribeRequest();
        if (trigger != null)
            context.SetJobParameter(JobDescriptions.TriggerParameter, trigger);
    }

    public void OnCreated(CreatedContext context)
    {
    }

    public void OnPerforming(PerformingContext context)
    {
        CurrentJob.Value = $"Job #{context.BackgroundJob.Id} · {JobDescriptions.Label(context.BackgroundJob.Job)}";
    }

    public void OnPerformed(PerformedContext context)
    {
        CurrentJob.Value = null;
    }

    private string? DescribeRequest()
    {
        try
        {
            var request = _httpContextAccessor.HttpContext?.Request;
            return request == null ? null : $"Request · {request.Method} {request.Path}";
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }
}
