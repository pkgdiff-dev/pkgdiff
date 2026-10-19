using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace PkgDiff.API.Services;

public static class ProgressTokens
{
    public const string Purpose = "PkgDiff.SignalR.ProgressToken.v1";

    public static string Create(IDataProtectionProvider provider, string connectionId) =>
        provider.CreateProtector(Purpose).Protect(connectionId);

    public static bool IsValid(IDataProtectionProvider provider, string? connectionId, string? token)
    {
        if (string.IsNullOrEmpty(connectionId) || string.IsNullOrEmpty(token))
            return false;

        try
        {
            var boundConnectionId = provider.CreateProtector(Purpose).Unprotect(token);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(boundConnectionId),
                Encoding.UTF8.GetBytes(connectionId));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
