using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace PkgDiff.Infrastructure.Security;

// Data Protection creates decryptors by type name through its activator, which supplies the service provider
public sealed class AesGcmXmlDecryptor : IXmlDecryptor
{
    private readonly KeyRingEncryptionKey _key;

    public AesGcmXmlDecryptor(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _key = services.GetRequiredService<KeyRingEncryptionKey>();
    }

    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        var value = encryptedElement.Element(AesGcmXmlEncryptor.ValueElementName)?.Value
            ?? throw new CryptographicException("The encrypted Data Protection key has no value element.");

        byte[] sealedData;
        try
        {
            sealedData = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            throw new CryptographicException("The encrypted Data Protection key is not valid base64.");
        }

        return XElement.Parse(Encoding.UTF8.GetString(_key.Open(sealedData)));
    }
}
