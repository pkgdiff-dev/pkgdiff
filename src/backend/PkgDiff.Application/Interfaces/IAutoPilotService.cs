using Hangfire;

namespace PkgDiff.Application.Interfaces;

public interface IAutoPilotService
{
    // Hangfire reads filters from the interface method it enqueues. A failed draft is not
    // retried: the next scheduled run tries again, and retries of a slow CPU-bound LLM call
    // used to pile up behind each other and block the workers.
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    Task RunAutoPilotJobAsync();
}
