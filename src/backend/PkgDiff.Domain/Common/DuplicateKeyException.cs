namespace PkgDiff.Domain.Common;

public sealed class DuplicateKeyException : Exception
{
    public DuplicateKeyException(Exception? innerException = null)
        : base("A row with the same unique key already exists.", innerException)
    {
    }
}
