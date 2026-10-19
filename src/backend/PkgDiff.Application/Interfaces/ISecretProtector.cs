namespace PkgDiff.Application.Interfaces;

public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string stored);
    bool IsProtected(string? value);
}
