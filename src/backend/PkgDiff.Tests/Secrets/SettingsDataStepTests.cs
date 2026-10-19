using System.Security.Cryptography;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Data;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Shared.Constants;

namespace PkgDiff.Tests.Secrets;

public class SettingsDataStepTests
{
    private readonly AesGcmSecretProtector _protector = new(RandomNumberGenerator.GetBytes(32));

    private static SystemSettings Legacy(string key, string value, string category = "") =>
        new() { Key = key, Value = value, Description = "legacy", DataType = "String", Category = category, IsSecret = false };

    [Fact]
    public void Apply_LegacySecretRow_BackfillsFlagsAndEncrypts()
    {
        var row = Legacy(SettingKeys.TwitterAccessToken, "tok");

        var changed = SystemSettingsMaintenance.Apply(new[] { row }, _protector);

        Assert.True(changed >= 1);
        Assert.Equal("Social Media: Twitter", row.Category);
        Assert.True(row.IsSecret);
        Assert.StartsWith("enc:v1:", row.Value, StringComparison.Ordinal);
        Assert.Equal("tok", _protector.Unprotect(row.Value));
    }

    [Fact]
    public void Apply_SecondRun_ChangesNothing()
    {
        var rows = new[]
        {
            Legacy(SettingKeys.TwitterAccessToken, "tok"),
            Legacy(SettingKeys.LinkedInClientSecret, "secret"),
            Legacy(SettingKeys.LinkedInClientId, "id"),
            Legacy(SettingKeys.CacheEnabled, "true")
        };
        SystemSettingsMaintenance.Apply(rows, _protector);
        var valuesAfterFirstRun = rows.Select(r => r.Value).ToArray();

        var changed = SystemSettingsMaintenance.Apply(rows, _protector);

        Assert.Equal(0, changed);
        Assert.Equal(valuesAfterFirstRun, rows.Select(r => r.Value).ToArray());
        Assert.All(rows.Where(r => r.IsSecret), r => Assert.Equal(1, CountOccurrences(r.Value, "enc:v1:")));
    }

    [Fact]
    public void Apply_TwitterApiKey_IsSecretAndEncrypted()
    {
        var row = Legacy(SettingKeys.TwitterApiKey, "k");

        SystemSettingsMaintenance.Apply(new[] { row }, _protector);

        Assert.True(row.IsSecret);
        Assert.True(_protector.IsProtected(row.Value));
        Assert.Equal("k", _protector.Unprotect(row.Value));
    }

    [Fact]
    public void Apply_LinkedInClientId_StaysNonSecretPlaintext()
    {
        var row = Legacy(SettingKeys.LinkedInClientId, "id");

        SystemSettingsMaintenance.Apply(new[] { row }, _protector);

        Assert.False(row.IsSecret);
        Assert.Equal("Social Media: LinkedIn", row.Category);
        Assert.Equal("id", row.Value);
    }

    [Fact]
    public void Apply_EmptySecret_StaysEmpty()
    {
        var row = Legacy(SettingKeys.TwitterAccessTokenSecret, "");

        SystemSettingsMaintenance.Apply(new[] { row }, _protector);

        Assert.True(row.IsSecret);
        Assert.Equal(string.Empty, row.Value);
    }

    [Fact]
    public void Apply_UnknownKey_IsUntouched()
    {
        var row = new SystemSettings { Key = "Custom:Key", Value = "x", Description = "mine", DataType = "String", Category = "Mine", IsSecret = false };

        var changed = SystemSettingsMaintenance.Apply(new[] { row }, _protector);

        Assert.Equal(0, changed);
        Assert.Equal("x", row.Value);
        Assert.Equal("Mine", row.Category);
        Assert.False(row.IsSecret);
    }

    [Fact]
    public void Apply_SecretEncryptedWithAnotherKey_IsLeftByteIdentical()
    {
        var foreign = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32)).Protect("tok");
        var row = new SystemSettings
        {
            Key = SettingKeys.TwitterAccessToken,
            Value = foreign,
            Description = "Twitter Access Token",
            DataType = "String",
            Category = "Social Media: Twitter",
            IsSecret = true
        };

        var changed = SystemSettingsMaintenance.Apply(new[] { row }, _protector);

        Assert.Equal(0, changed);
        Assert.Equal(foreign, row.Value);
    }

    [Fact]
    public void CreateMissing_SeedsAutoPostDisabled()
    {
        var existing = SettingKeys.All.Select(d => d.Key).Where(k => k != SettingKeys.SocialMediaAutoPostEnabled);

        var created = SystemSettingsMaintenance.CreateMissing(existing);

        var row = Assert.Single(created);
        Assert.Equal(SettingKeys.SocialMediaAutoPostEnabled, row.Key);
        Assert.Equal("false", row.Value);
        Assert.Equal("Bool", row.DataType);
        Assert.Equal("Social Media", row.Category);
        Assert.False(row.IsSecret);
    }

    [Fact]
    public void CreateMissing_EmptyDatabase_CreatesEveryKnownKeyWithFlags()
    {
        var created = SystemSettingsMaintenance.CreateMissing(Array.Empty<string>());

        Assert.Equal(SettingKeys.All.Count, created.Count);
        Assert.All(created, r => Assert.False(string.IsNullOrWhiteSpace(r.Category)));
        Assert.Contains(created, r => r.Key == SettingKeys.AutoPilotDailyFrequency && r.Value == "3" && r.DataType == "Int");
        Assert.Contains(created, r => r.Key == SettingKeys.HangfireWorkerCount && r.DataType == "Int");
        Assert.Contains(created, r => r.Key == SettingKeys.TwitterApiKey && r.IsSecret);
        Assert.Contains(created, r => r.Key == SettingKeys.LinkedInAuthorUrn && !r.IsSecret);
    }

    [Fact]
    public void CreateMissing_ThenApply_OnFreshRows_ChangesNothing()
    {
        var created = SystemSettingsMaintenance.CreateMissing(Array.Empty<string>());
        Assert.NotEmpty(created);

        Assert.Equal(0, SystemSettingsMaintenance.Apply(created, _protector));
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
