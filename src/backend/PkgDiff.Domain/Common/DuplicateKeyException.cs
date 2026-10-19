namespace PkgDiff.Domain.Common;

// A save was refused because a row with the same unique key already exists
public sealed class DuplicateKeyException : Exception
{
    public DuplicateKeyException(Exception? innerException = null)
        : base("A row with the same unique key already exists.", innerException)
    {
    }
}
