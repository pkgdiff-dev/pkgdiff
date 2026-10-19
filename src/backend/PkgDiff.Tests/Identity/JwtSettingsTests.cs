using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PkgDiff.API.Extensions;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.Options;

namespace PkgDiff.Tests.Identity;

public class JwtSettingsTests
{
    private const string LegacyPlaceholder = "YourSuperSecretKeyThatIsAtLeast32CharactersLong!";

    private static string RandomSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(36));

    private static ServiceProvider BuildProvider(string? secretKey)
    {
        var settings = new Dictionary<string, string?>
        {
            ["JwtSettings:Issuer"] = "pkgdiff-test-issuer",
            ["JwtSettings:Audience"] = "pkgdiff-test-audience"
        };
        if (secretKey is not null)
        {
            settings["JwtSettings:SecretKey"] = secretKey;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddPkgDiffAuthentication(configuration);
        services.AddPkgDiffAuthorization();
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(32, true)]
    [InlineData(0, false)]
    public void HasMinimumLength_UsesAsciiByteBoundary(int length, bool expected)
    {
        var settings = new JwtSettings { SecretKey = new string('a', length) };

        Assert.Equal(expected, JwtSettings.HasMinimumLength(settings));
    }

    [Fact]
    public void HasMinimumLength_CountsUtf8BytesNotCharacters()
    {
        // 16 characters, 32 UTF-8 bytes
        var settings = new JwtSettings { SecretKey = new string('é', 16) };

        Assert.Equal(32, Encoding.UTF8.GetByteCount(settings.SecretKey));
        Assert.True(JwtSettings.HasMinimumLength(settings));
    }

    [Theory]
    [InlineData(LegacyPlaceholder)]
    [InlineData("CHANGE_ME")]
    [InlineData("ExampleSecretKey123")]
    public void IsPlaceholder_RecognizesLockedPlaceholders(string value)
    {
        Assert.True(JwtSettings.IsPlaceholder(new JwtSettings { SecretKey = value }));
    }

    [Fact]
    public void IsPlaceholder_RandomSecret_IsNotPlaceholder()
    {
        var secret = RandomSecret();

        Assert.Equal(48, secret.Length);
        Assert.False(JwtSettings.IsPlaceholder(new JwtSettings { SecretKey = secret }));
    }

    [Fact]
    public void ShortSecret_FailsValidation_NamingJwtSecret()
    {
        using var provider = BuildProvider(new string('a', 31));

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<JwtSettings>>().Value);
        Assert.Contains("JWT_SECRET", ex.Message);
    }

    [Fact]
    public void MissingSecret_FailsValidation_NamingSettingAndEnvVar()
    {
        using var provider = BuildProvider(null);

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<JwtSettings>>().Value);
        Assert.Contains("JwtSettings:SecretKey", ex.Message);
        Assert.Contains("JWT_SECRET", ex.Message);
    }

    [Fact]
    public void LegacyPlaceholderSecret_FailsValidation_AsPlaceholder()
    {
        using var provider = BuildProvider(LegacyPlaceholder);

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<JwtSettings>>().Value);
        Assert.Contains("placeholder", ex.Message);
    }

    [Fact]
    public void ValidSecret_ConfiguresBearerSigningKeyFromSameSettings()
    {
        var secret = RandomSecret();
        using var provider = BuildProvider(secret);

        var settings = provider.GetRequiredService<IOptions<JwtSettings>>().Value;
        Assert.Equal(secret, settings.SecretKey);
        Assert.Same(settings, provider.GetRequiredService<JwtSettings>());

        var bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var parameters = bearer.TokenValidationParameters;
        Assert.True(parameters.ValidateIssuerSigningKey);
        Assert.True(parameters.ValidateIssuer);
        Assert.True(parameters.ValidateAudience);
        Assert.True(parameters.ValidateLifetime);
        Assert.Equal("pkgdiff-test-issuer", parameters.ValidIssuer);
        Assert.Equal("pkgdiff-test-audience", parameters.ValidAudience);
        var key = Assert.IsType<SymmetricSecurityKey>(parameters.IssuerSigningKey);
        Assert.Equal(Encoding.UTF8.GetBytes(secret), key.Key);
    }

    [Fact]
    public async Task PermissionPolicy_AllowsAdminRoleOrMatchingClaim_DeniesOthers()
    {
        using var provider = BuildProvider(RandomSecret());
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var admin = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, "Admin") }, "Test"));
        var withClaim = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("Permission", Permissions.Users.Create) }, "Test"));
        var neither = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, "Viewer"), new Claim("Permission", Permissions.Users.View) }, "Test"));

        Assert.True((await authorization.AuthorizeAsync(admin, Permissions.Users.Create)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(withClaim, Permissions.Users.Create)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(neither, Permissions.Users.Create)).Succeeded);
    }
}
