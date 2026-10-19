using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PkgDiff.API.Extensions;
using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Infrastructure.Services;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Secrets;

public class SecretProtectorTests
{
    private static AesGcmSecretProtector NewProtector() => new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Protect_ProducesPrefixedValue_ThatRoundTrips()
    {
        var protector = NewProtector();

        var stored = protector.Protect("tok");

        Assert.StartsWith("enc:v1:", stored, StringComparison.Ordinal);
        Assert.Equal("tok", protector.Unprotect(stored));
    }

    [Fact]
    public void Protect_UsesAFreshNonceEveryTime()
    {
        var protector = NewProtector();

        Assert.NotEqual(protector.Protect("tok"), protector.Protect("tok"));
    }

    [Fact]
    public void Unprotect_TamperedPayload_ThrowsCryptographicException()
    {
        var protector = NewProtector();
        var stored = protector.Protect("tok");
        var payload = Convert.FromBase64String(stored[AesGcmSecretProtector.Prefix.Length..]);
        payload[payload.Length / 2] ^= 0x01;
        var tampered = AesGcmSecretProtector.Prefix + Convert.ToBase64String(payload);

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tampered));
    }

    [Fact]
    public void Unprotect_WithDifferentKey_ThrowsCryptographicException()
    {
        var stored = NewProtector().Protect("tok");

        Assert.ThrowsAny<CryptographicException>(() => NewProtector().Unprotect(stored));
    }

    [Theory]
    [InlineData("enc:v1:")]
    [InlineData("enc:v1:AAAA")]
    [InlineData("enc:v1:not base64!!")]
    public void Unprotect_MalformedPayload_ThrowsCryptographicException(string stored)
    {
        Assert.ThrowsAny<CryptographicException>(() => NewProtector().Unprotect(stored));
    }

    [Fact]
    public void Unprotect_LegacyPlaintext_IsReturnedUnchanged()
    {
        Assert.Equal("plain", NewProtector().Unprotect("plain"));
    }

    [Fact]
    public void IsProtected_RecognizesOnlyThePrefix()
    {
        var protector = NewProtector();

        Assert.False(protector.IsProtected(null));
        Assert.False(protector.IsProtected(""));
        Assert.False(protector.IsProtected("plain"));
        Assert.True(protector.IsProtected("enc:v1:x"));
    }

    [Fact]
    public void Constructor_RejectsKeysThatAreNot32Bytes()
    {
        Assert.Throws<ArgumentException>(() => new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(16)));
    }

    [Fact]
    public void Resolve_MissingKeyInDevelopment_UsesDeterministicDerivedKey()
    {
        var first = SettingsEncryptionKey.Resolve(null, allowDevelopmentFallback: true, out var firstDev);
        var second = SettingsEncryptionKey.Resolve("  ", allowDevelopmentFallback: true, out var secondDev);

        Assert.Equal(32, first.Length);
        Assert.True(firstDev);
        Assert.True(secondDev);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Resolve_MissingKeyOutsideDevelopment_ThrowsWithGuidance()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SettingsEncryptionKey.Resolve(null, false, out _));

        Assert.Contains("SETTINGS_ENCRYPTION_KEY", ex.Message, StringComparison.Ordinal);
        Assert.Contains("openssl rand -base64 32", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ValidKey_ReturnsItsBytes()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);

        var key = SettingsEncryptionKey.Resolve(Convert.ToBase64String(bytes), false, out var dev);

        Assert.Equal(bytes, key);
        Assert.False(dev);
    }

    [Fact]
    public void Resolve_KeyOfWrongLength_ThrowsWithoutEchoingIt()
    {
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        var ex = Assert.Throws<InvalidOperationException>(() => SettingsEncryptionKey.Resolve(value, true, out _));

        Assert.Contains("SETTINGS_ENCRYPTION_KEY", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_KeyThatIsNotBase64_ThrowsWithoutEchoingIt()
    {
        const string value = "not-base64!!";

        var ex = Assert.Throws<InvalidOperationException>(() => SettingsEncryptionKey.Resolve(value, true, out _));

        Assert.Contains("SETTINGS_ENCRYPTION_KEY", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddPkgDiffSecrets_ProductionWithoutKey_FailsWhenResolved()
    {
        using var provider = BuildProvider("Production", key: null);

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ISecretProtector>());
        Assert.Contains("SETTINGS_ENCRYPTION_KEY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddPkgDiffSecrets_DevelopmentWithoutKey_Resolves()
    {
        using var provider = BuildProvider("Development", key: null);

        var protector = provider.GetRequiredService<ISecretProtector>();

        Assert.Equal("tok", protector.Unprotect(protector.Protect("tok")));
    }

    [Fact]
    public void AddPkgDiffSecrets_ProductionWithKey_Resolves()
    {
        using var provider = BuildProvider("Production", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        Assert.IsType<AesGcmSecretProtector>(provider.GetRequiredService<ISecretProtector>());
    }

    [Fact]
    public void AddPkgDiffSecrets_ProductionWithInvalidKey_FailsWhenResolved()
    {
        using var provider = BuildProvider("Production", "not-base64!!");

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ISecretProtector>());
        Assert.DoesNotContain("not-base64!!", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reader_NonSecretSetting_ReturnsPlainValue()
    {
        var (reader, unitOfWork, _) = BuildReader(NewProtector());
        await unitOfWork.SystemSettings.AddAsync(new SystemSettings { Key = "AutoPilot:Enabled", Value = "true", DataType = "Bool" });

        Assert.Equal("true", await reader.GetAsync("AutoPilot:Enabled"));
    }

    [Fact]
    public async Task Reader_SecretSetting_ReturnsDecryptedValue()
    {
        var protector = NewProtector();
        var (reader, unitOfWork, _) = BuildReader(protector);
        await unitOfWork.SystemSettings.AddAsync(new SystemSettings { Key = "SocialMedia:Twitter:AccessToken", Value = protector.Protect("tok"), IsSecret = true });

        Assert.Equal("tok", await reader.GetAsync("SocialMedia:Twitter:AccessToken"));
    }

    [Fact]
    public async Task Reader_MissingKey_ReturnsNull()
    {
        var (reader, _, _) = BuildReader(NewProtector());

        Assert.Null(await reader.GetAsync("SocialMedia:Twitter:AccessToken"));
    }

    [Fact]
    public async Task Reader_ValueEncryptedWithAnotherKey_ReturnsNullWarnsAndKeepsTheRow()
    {
        var foreign = NewProtector().Protect("tok");
        var (reader, unitOfWork, logger) = BuildReader(NewProtector());
        var row = new SystemSettings { Key = "SocialMedia:Twitter:AccessToken", Value = foreign, IsSecret = true };
        await unitOfWork.SystemSettings.AddAsync(row);

        var value = await reader.GetAsync("SocialMedia:Twitter:AccessToken");

        Assert.Null(value);
        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("SocialMedia:Twitter:AccessToken", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(foreign, warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tok", warning.Message.Replace("SocialMedia:Twitter:AccessToken", string.Empty), StringComparison.Ordinal);
        Assert.Equal(foreign, row.Value);
        Assert.Single(unitOfWork.SystemSettings.Items);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    private static (SystemSettingsReader Reader, FakeUnitOfWork UnitOfWork, CapturingLogger<SystemSettingsReader> Logger) BuildReader(ISecretProtector protector)
    {
        var unitOfWork = new FakeUnitOfWork();
        var logger = new CapturingLogger<SystemSettingsReader>();
        return (new SystemSettingsReader(unitOfWork, protector, logger), unitOfWork, logger);
    }

    private static ServiceProvider BuildProvider(string environmentName, string? key)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:SettingsEncryptionKey"] = key
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(environmentName));
        services.AddPkgDiffSecrets();
        return services.BuildServiceProvider();
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

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception) + " " + exception?.Message));
        }
    }
}
