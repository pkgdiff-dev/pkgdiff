using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using PkgDiff.API.Extensions;

namespace PkgDiff.Tests.Identity;

public class SeederRulesTests
{
    private const string AdminPasswordKey = "AdminSettings:AdminPassword";

    private static IConfiguration BuildConfiguration(string? value)
    {
        var settings = new Dictionary<string, string?>();
        if (value is not null)
        {
            settings[AdminPasswordKey] = value;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    [Fact]
    public void GetConfiguredSecret_MissingKey_ReturnsNull()
    {
        Assert.Null(DatabaseSeeder.GetConfiguredSecret(BuildConfiguration(null), AdminPasswordKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetConfiguredSecret_BlankValue_ReturnsNull(string value)
    {
        Assert.Null(DatabaseSeeder.GetConfiguredSecret(BuildConfiguration(value), AdminPasswordKey));
    }

    [Fact]
    public void GetConfiguredSecret_ConfiguredValue_ReturnsIt()
    {
        var value = "Test-" + Guid.NewGuid().ToString("N");

        Assert.Equal(value, DatabaseSeeder.GetConfiguredSecret(BuildConfiguration(value), AdminPasswordKey));
    }

    [Fact]
    public void EnsureSucceeded_Success_DoesNotThrow()
    {
        DatabaseSeeder.EnsureSucceeded(IdentityResult.Success, "x");
    }

    [Fact]
    public void EnsureSucceeded_Failure_ThrowsWithActionAndIdentityErrors()
    {
        const string description = "Passwords must have at least one digit ('0'-'9').";
        const string action = "create admin user pkgdiff_admin";
        var failed = IdentityResult.Failed(new IdentityError { Description = description });

        var ex = Assert.Throws<InvalidOperationException>(() => DatabaseSeeder.EnsureSucceeded(failed, action));

        Assert.Contains(action, ex.Message);
        Assert.Contains(description, ex.Message);
    }
}
