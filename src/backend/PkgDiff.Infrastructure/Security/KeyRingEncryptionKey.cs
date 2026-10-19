using System.Security.Cryptography;

namespace PkgDiff.Infrastructure.Security;

// Encrypts the Data Protection key ring with a key derived from SETTINGS_ENCRYPTION_KEY, which lives outside
// the database, so a database dump alone cannot be used to forge Data Protection payloads.
public sealed class KeyRingEncryptionKey
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    // Domain separation: the key ring never shares a key with the SystemSettings ciphertexts
    private static readonly byte[] DerivationInfo = "PkgDiff.DataProtection.KeyRing.v1"u8.ToArray();
    private static readonly byte[] Aad = "PkgDiff.DataProtection.KeyRing.v1"u8.ToArray();

    private readonly byte[] _key;

    public KeyRingEncryptionKey(byte[] settingsKey)
    {
        ArgumentNullException.ThrowIfNull(settingsKey);
        if (settingsKey.Length != SettingsEncryptionKey.KeySize)
        {
            throw new ArgumentException($"The settings encryption key must be exactly {SettingsEncryptionKey.KeySize} bytes.", nameof(settingsKey));
        }

        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, settingsKey, SettingsEncryptionKey.KeySize, salt: null, info: DerivationInfo);
    }

    public byte[] Seal(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var buffer = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = buffer.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var gcm = new AesGcm(_key, TagSize);
        gcm.Encrypt(
            nonce,
            plaintext,
            buffer.AsSpan(NonceSize, plaintext.Length),
            buffer.AsSpan(NonceSize + plaintext.Length, TagSize),
            Aad);

        return buffer;
    }

    public byte[] Open(byte[] sealedData)
    {
        ArgumentNullException.ThrowIfNull(sealedData);
        if (sealedData.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("The encrypted Data Protection key is too short.");
        }

        var cipherLength = sealedData.Length - NonceSize - TagSize;
        var plaintext = new byte[cipherLength];

        using var gcm = new AesGcm(_key, TagSize);
        gcm.Decrypt(
            sealedData.AsSpan(0, NonceSize),
            sealedData.AsSpan(NonceSize, cipherLength),
            sealedData.AsSpan(NonceSize + cipherLength, TagSize),
            plaintext,
            Aad);

        return plaintext;
    }
}
