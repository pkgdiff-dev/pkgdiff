namespace PkgDiff.Infrastructure.Security;

public sealed class SsrfBlockedException : Exception
{
    public SsrfBlockedException(string message)
        : base(message)
    {
    }
}
