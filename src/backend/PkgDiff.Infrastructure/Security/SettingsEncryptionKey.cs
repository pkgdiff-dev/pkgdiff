using System.Security.Cryptography;

namespace PkgDiff.Infrastructure.Security;

public static class SettingsEncryptionKey
{
    public const int KeySize = 32;

    public static byte[] Resolve(string? base64Key, bool allowDevelopmentFallback, out bool usedDevelopmentFallback)
    {
        usedDevelopmentFallback = false;

        if (string.IsNullOrWhiteSpace(base64Key))
        {
            if (!allowDevelopmentFallback)
            {
                throw new InvalidOperationException(
                    "Security:SettingsEncryptionKey (env SETTINGS_ENCRYPTION_KEY) is required outside Development. Generate one with 'openssl rand -base64 32' and add it to .env before deploying.");
            }

            // Deterministic so a local development database stays readable across restarts
            usedDevelopmentFallback = true;
            return SHA256.HashData("pkgdiff-dev-only-settings-key"u8);
        }

        // Never echo the supplied value: it is key material
        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key.Trim());
        }
        catch (FormatException)
        {
            throw InvalidKey();
        }

        if (key.Length != KeySize)
        {
            throw InvalidKey();
        }

        return key;
    }

    private static InvalidOperationException InvalidKey() =>
        new("Security:SettingsEncryptionKey (env SETTINGS_ENCRYPTION_KEY) must be the base64 encoding of exactly 32 random bytes.");
}
