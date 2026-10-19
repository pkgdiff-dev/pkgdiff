using Hangfire;

namespace PkgDiff.Application.Interfaces;

public interface IAutoPilotService
{
    // Hangfire reads filters from the interface. No retries: the next scheduled run tries again.
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    Task RunAutoPilotJobAsync();
}
