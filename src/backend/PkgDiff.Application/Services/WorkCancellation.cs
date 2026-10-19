namespace PkgDiff.Application.Services;

// The cancellation token of the work that is running on this async flow. An endpoint that starts
// long work sets it from the request; the outbound HTTP handler, the repository reads and the rate
// limiter honour it, so the work stops without every method in between taking a token parameter.
// Outside such a scope (background jobs, short requests) the token is None.
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
