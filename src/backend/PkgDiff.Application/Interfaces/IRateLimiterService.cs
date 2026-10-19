namespace PkgDiff.Application.Interfaces;

public interface IRateLimiterService
{
    Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action);

    bool CanExecute(string key);

    TimeSpan GetTimeUntilNextSlot(string key);
}
