namespace PkgDiff.Application.Services;

// Ambient cancellation token of the long work a request started; None outside such a scope.
// Outbound HTTP, repository reads and the rate limiter honour it without a token parameter.
public static class WorkCancellation
{
    private static readonly AsyncLocal<CancellationToken> Current = new();

    public static CancellationToken Token => Current.Value;

    public static IDisposable Use(CancellationToken token)
    {
        var previous = Current.Value;
        Current.Value = token;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly CancellationToken _previous;

        public Scope(CancellationToken previous)
        {
            _previous = previous;
        }

        public void Dispose() => Current.Value = _previous;
    }
}
