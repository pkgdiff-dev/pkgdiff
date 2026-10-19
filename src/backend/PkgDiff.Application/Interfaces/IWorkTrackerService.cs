namespace PkgDiff.Application.Interfaces;

public interface IWorkTrackerService
{
    bool IsRunning(string key);
    int GetActiveCount(string keyPrefix);
    Task<T> RunCoalescedAsync<T>(string key, Func<Task<T>> work);
}
