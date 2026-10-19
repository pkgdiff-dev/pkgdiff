using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using PkgDiff.Application.Interfaces;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.API.Extensions;

public static class SecretsServiceCollectionExtensions
{
    public const string SettingsEncryptionKeyConfigKey = "Security:SettingsEncryptionKey";

    public static IServiceCollection AddPkgDiffSecrets(this IServiceCollection services)
    {
        // The key is validated when the protector is first resolved, never during registration,
        // so the EF design-time host keeps working without SETTINGS_ENCRYPTION_KEY.
        services.AddSingleton<ISecretProtector>(sp => new AesGcmSecretProtector(ResolveSettingsKey(sp, warnOnDevelopmentFallback: true)));

        services.AddScoped<ISystemSettingsReader, SystemSettingsReader>();

        return services;
    }

    // Same key policy as the settings: required outside Development, derived dev-only key in Development.
    // Resolved lazily when the key ring is first loaded, never during registration.
    public static IDataProtectionBuilder ProtectKeysWithSettingsEncryptionKey(this IDataProtectionBuilder builder)
    {
        builder.Services.AddSingleton(sp => new KeyRingEncryptionKey(ResolveSettingsKey(sp, warnOnDevelopmentFallback: false)));
        builder.Services.AddOptions<KeyManagementOptions>()
            .Configure<IServiceProvider>((options, sp) =>
                options.XmlEncryptor = new AesGcmXmlEncryptor(sp.GetRequiredService<KeyRingEncryptionKey>()));

        return builder;
    }

    private static byte[] ResolveSettingsKey(IServiceProvider sp, bool warnOnDevelopmentFallback)
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        var environment = sp.GetRequiredService<IHostEnvironment>();

        var key = SettingsEncryptionKey.Resolve(
            configuration[SettingsEncryptionKeyConfigKey],
            environment.IsDevelopment(),
            out var usedDevelopmentFallback);

        if (usedDevelopmentFallback && warnOnDevelopmentFallback)
        {
            sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(SecretsServiceCollectionExtensions))
                .LogWarning("Security:SettingsEncryptionKey is not set; using a development-only derived key. Never run Production like this.");
        }

        return key;
    }
}
