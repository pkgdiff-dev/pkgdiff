using System.Security.Cryptography;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Secrets;

public class SettingsApiTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly AesGcmSecretProtector _protector = new(RandomNumberGenerator.GetBytes(32));
    private readonly AdminSettingsService _service;

    private readonly SystemSettings _twitterToken;
    private readonly SystemSettings _linkedInSecret;
    private readonly SystemSettings _cacheEnabled;

    public SettingsApiTests()
    {
        _service = new AdminSettingsService(_uow, _protector);

        _twitterToken = AddSetting(SettingKeys.TwitterAccessToken, _protector.Protect("tok"), SettingKeys.CategoryTwitter, isSecret: true);
        _linkedInSecret = AddSetting(SettingKeys.LinkedInClientSecret, string.Empty, SettingKeys.CategoryLinkedIn, isSecret: true);
        _cacheEnabled = AddSetting(SettingKeys.CacheEnabled, "true", SettingKeys.CategoryGeneral, isSecret: false);
    }

    private SystemSettings AddSetting(string key, string value, string category, bool isSecret)
    {
        var entity = new SystemSettings
        {
            Key = key,
            Value = value,
            Description = key,
            DataType = "String",
            Category = category,
            IsSecret = isSecret
        };
        _uow.SystemSettings.AddAsync(entity).GetAwaiter().GetResult();
        return entity;
    }

    private static void AssertNoPlaintext(SystemSettingDto dto, params string[] plaintexts)
    {
        foreach (var plaintext in plaintexts)
        {
            Assert.DoesNotContain(plaintext, dto.Value, StringComparison.Ordinal);
            Assert.DoesNotContain(plaintext, dto.Description, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GetSettings_MasksSecretValues_AndReportsHasValue()
    {
        var settings = await _service.GetSettingsAsync();

        var token = settings.Single(s => s.Key == SettingKeys.TwitterAccessToken);
        Assert.Equal(string.Empty, token.Value);
        Assert.True(token.HasValue);
        Assert.True(token.IsSecret);

        var clientSecret = settings.Single(s => s.Key == SettingKeys.LinkedInClientSecret);
        Assert.Equal(string.Empty, clientSecret.Value);
        Assert.False(clientSecret.HasValue);
        Assert.True(clientSecret.IsSecret);

        var cache = settings.Single(s => s.Key == SettingKeys.CacheEnabled);
        Assert.Equal("true", cache.Value);
        Assert.True(cache.HasValue);
        Assert.False(cache.IsSecret);

        foreach (var dto in settings)
        {
            AssertNoPlaintext(dto, "tok", "enc:v1:");
        }
    }

    [Fact]
    public async Task UpdateSecret_WithNewValue_StoresCiphertext_AndReturnsMaskedDto()
    {
        var result = await _service.UpdateSettingAsync(_linkedInSecret.Id, new UpdateSettingRequest { Value = "new" });

        Assert.StartsWith("enc:v1:", _linkedInSecret.Value, StringComparison.Ordinal);
        Assert.Equal("new", _protector.Unprotect(_linkedInSecret.Value));
        Assert.Equal(string.Empty, result.Value);
        Assert.True(result.HasValue);
        Assert.True(result.IsSecret);
        AssertNoPlaintext(result, "new", "enc:v1:");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task UpdateSecret_WithEmptyValue_KeepsStoredCiphertext(string? value)
    {
        var before = _twitterToken.Value;

        var result = await _service.UpdateSettingAsync(_twitterToken.Id, new UpdateSettingRequest { Value = value! });

        Assert.Equal(before, _twitterToken.Value);
        Assert.Equal("tok", _protector.Unprotect(_twitterToken.Value));
        Assert.Equal(string.Empty, result.Value);
        Assert.True(result.HasValue);
        AssertNoPlaintext(result, "tok", "enc:v1:");
    }

    [Fact]
    public async Task UpdateSecret_WithClear_EmptiesStoredValue()
    {
        var result = await _service.UpdateSettingAsync(_twitterToken.Id, new UpdateSettingRequest { Value = "", Clear = true });

        Assert.Equal(string.Empty, _twitterToken.Value);
        Assert.Equal(string.Empty, result.Value);
        Assert.False(result.HasValue);
        Assert.True(result.IsSecret);
    }

    [Fact]
    public async Task UpdateNonSecret_StoresPlainValue_AndReturnsIt()
    {
        var result = await _service.UpdateSettingAsync(_cacheEnabled.Id, new UpdateSettingRequest { Value = "false" });

        Assert.Equal("false", _cacheEnabled.Value);
        Assert.Equal("false", result.Value);
        Assert.True(result.HasValue);
        Assert.False(result.IsSecret);
    }

    [Fact]
    public async Task UpdateSetting_UnknownId_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.UpdateSettingAsync(9999, new UpdateSettingRequest { Value = "x" }));
    }
}
