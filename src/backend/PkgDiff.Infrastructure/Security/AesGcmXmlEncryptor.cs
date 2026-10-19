using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace PkgDiff.Infrastructure.Security;

public sealed class AesGcmXmlEncryptor : IXmlEncryptor
{
    public const string EncryptedElementName = "encryptedKey";
    public const string ValueElementName = "value";

    private readonly KeyRingEncryptionKey _key;

    public AesGcmXmlEncryptor(KeyRingEncryptionKey key)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
    }

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var encryptedElement = new XElement(
            EncryptedElementName,
            new XComment(" Encrypted with AES-256-GCM using a key derived from SETTINGS_ENCRYPTION_KEY. "),
            new XElement(ValueElementName, Convert.ToBase64String(_key.Seal(plaintext))));

        return new EncryptedXmlInfo(encryptedElement, typeof(AesGcmXmlDecryptor));
    }
}
