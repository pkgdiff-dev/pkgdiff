using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PkgDiff.API.Extensions;
using PkgDiff.Infrastructure.Security;

namespace PkgDiff.Tests.Secrets;

// WR-03: the Data Protection key ring stored in the database must be encrypted with key material kept outside it.
public class DataProtectionKeyRingTests
{
    private sealed class InMemoryXmlRepository : IXmlRepository
    {
        private readonly List<XElement> _elements = new();

        public IReadOnlyList<XElement> Elements => _elements;

        public IReadOnlyCollection<XElement> GetAllElements() => _elements.Select(e => new XElement(e)).ToList();

        public void StoreElement(XElement element, string friendlyName) => _elements.Add(new XElement(element));
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "PkgDiff.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string NewSettingsKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static ServiceProvider BuildProvider(InMemoryXmlRepository repository, string environmentName, string? settingsKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SecretsServiceCollectionExtensions.SettingsEncryptionKeyConfigKey] = settingsKey
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(environmentName));
        services.AddDataProtection()
            .SetApplicationName("pkgdiff-tests")
            .ProtectKeysWithSettingsEncryptionKey();
        services.Configure<KeyManagementOptions>(options => options.XmlRepository = repository);
        return services.BuildServiceProvider();
    }

    private static IDataProtector Protector(ServiceProvider provider) =>
        provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("tests");

    [Fact]
    public void StoredKey_IsEncrypted_AndHasNoPlaintextMasterKey()
    {
        var repository = new InMemoryXmlRepository();
        using var provider = BuildProvider(repository, "Production", NewSettingsKey());

        Protector(provider).Protect("payload");

        var stored = Assert.Single(repository.Elements).ToString();
        Assert.Contains("encryptedSecret", stored, StringComparison.Ordinal);
        Assert.Contains(nameof(AesGcmXmlDecryptor), stored, StringComparison.Ordinal);
        Assert.DoesNotContain("masterKey", stored, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyRing_IsReadableAfterRestart_WithTheSameSettingsKey()
    {
        var repository = new InMemoryXmlRepository();
        var settingsKey = NewSettingsKey();
        string protectedPayload;
        using (var first = BuildProvider(repository, "Production", settingsKey))
        {
            protectedPayload = Protector(first).Protect("payload");
        }

        using var second = BuildProvider(repository, "Production", settingsKey);

        Assert.Equal("payload", Protector(second).Unprotect(protectedPayload));
    }

    [Fact]
    public void KeyRing_CannotBeUsed_WithoutTheSettingsKey()
    {
        var repository = new InMemoryXmlRepository();
        string protectedPayload;
        using (var first = BuildProvider(repository, "Production", NewSettingsKey()))
        {
            protectedPayload = Protector(first).Protect("payload");
        }

        using var attacker = BuildProvider(repository, "Production", NewSettingsKey());

        Assert.ThrowsAny<CryptographicException>(() => Protector(attacker).Unprotect(protectedPayload));
    }

    [Fact]
    public void Production_WithoutSettingsKey_FailsWhenTheKeyRingIsUsed()
    {
        using var provider = BuildProvider(new InMemoryXmlRepository(), "Production", settingsKey: null);

        var ex = Assert.ThrowsAny<Exception>(() => Protector(provider).Protect("payload"));

        Assert.Contains("SETTINGS_ENCRYPTION_KEY", (ex.InnerException ?? ex).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_WithoutSettingsKey_StillWorks()
    {
        var repository = new InMemoryXmlRepository();
        using var provider = BuildProvider(repository, "Development", settingsKey: null);

        var protector = Protector(provider);

        Assert.Equal("payload", protector.Unprotect(protector.Protect("payload")));
        Assert.Contains("encryptedSecret", Assert.Single(repository.Elements).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeyRingKey_IsDerived_NotTheSettingsKeyItself()
    {
        var settingsKey = RandomNumberGenerator.GetBytes(32);
        var sealedData = new KeyRingEncryptionKey(settingsKey).Seal("x"u8.ToArray());

        // The SystemSettings protector uses the raw settings key; it must not open key ring ciphertext
        var settingsProtector = new AesGcmSecretProtector(settingsKey);

        Assert.ThrowsAny<CryptographicException>(() =>
            settingsProtector.Unprotect(AesGcmSecretProtector.Prefix + Convert.ToBase64String(sealedData)));
    }

    [Fact]
    public void TamperedCiphertext_IsRejected()
    {
        var key = new KeyRingEncryptionKey(RandomNumberGenerator.GetBytes(32));
        var sealedData = key.Seal("secret"u8.ToArray());
        sealedData[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => key.Open(sealedData));
    }
}
