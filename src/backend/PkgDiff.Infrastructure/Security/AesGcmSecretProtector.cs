using System.Security.Cryptography;
using System.Text;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.Infrastructure.Security;

public sealed class AesGcmSecretProtector : ISecretProtector
{
    public const string Prefix = "enc:v1:";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    // Binds every ciphertext to this purpose so it cannot be replayed into another AES-GCM context
    private static readonly byte[] Aad = Encoding.UTF8.GetBytes("PkgDiff.SystemSettings.v1");

    private readonly byte[] _key;

    public AesGcmSecretProtector(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySize)
        {
            throw new ArgumentException($"The settings encryption key must be exactly {KeySize} bytes.", nameof(key));
        }

        _key = (byte[])key.Clone();
    }

    public bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var buffer = new byte[NonceSize + plainBytes.Length + TagSize];
        var nonce = buffer.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var gcm = new AesGcm(_key, TagSize);
        gcm.Encrypt(
            nonce,
            plainBytes,
            buffer.AsSpan(NonceSize, plainBytes.Length),
            buffer.AsSpan(NonceSize + plainBytes.Length, TagSize),
            Aad);

        return Prefix + Convert.ToBase64String(buffer);
    }

    public string Unprotect(string stored)
    {
        // Legacy plaintext is tolerated until the startup data step has encrypted it
        if (!IsProtected(stored))
        {
            return stored;
        }

        byte[] buffer;
        try
        {
            buffer = Convert.FromBase64String(stored[Prefix.Length..]);
        }
        catch (FormatException)
        {
            throw new CryptographicException("The protected setting value is not valid base64.");
        }

        if (buffer.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("The protected setting value is too short.");
        }

        var cipherLength = buffer.Length - NonceSize - TagSize;
        var plainBytes = new byte[cipherLength];

        using var gcm = new AesGcm(_key, TagSize);
        gcm.Decrypt(
            buffer.AsSpan(0, NonceSize),
            buffer.AsSpan(NonceSize, cipherLength),
            buffer.AsSpan(NonceSize + cipherLength, TagSize),
            plainBytes,
            Aad);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
