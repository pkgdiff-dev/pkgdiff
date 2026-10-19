using Microsoft.AspNetCore.DataProtection;
using PkgDiff.API.Services;

namespace PkgDiff.Tests.Validation;

public class ProgressTokensTests
{
    private readonly IDataProtectionProvider _provider = new EphemeralDataProtectionProvider();

    [Fact]
    public void IsValid_TokenForSameConnection_ReturnsTrue()
    {
        var token = ProgressTokens.Create(_provider, "conn-1");

        Assert.True(ProgressTokens.IsValid(_provider, "conn-1", token));
    }

    [Fact]
    public void Create_DoesNotExposeTheConnectionIdInPlainText()
    {
        var token = ProgressTokens.Create(_provider, "conn-1");

        Assert.DoesNotContain("conn-1", token);
    }

    [Fact]
    public void IsValid_TokenForAnotherConnection_ReturnsFalse()
    {
        var token = ProgressTokens.Create(_provider, "conn-1");

        Assert.False(ProgressTokens.IsValid(_provider, "conn-2", token));
    }

    [Fact]
    public void IsValid_TamperedToken_ReturnsFalse()
    {
        var token = ProgressTokens.Create(_provider, "conn-1");
        var middle = token.Length / 2;
        var flipped = token[middle] == 'A' ? 'B' : 'A';
        var tampered = token[..middle] + flipped + token[(middle + 1)..];

        Assert.False(ProgressTokens.IsValid(_provider, "conn-1", tampered));
    }

    [Theory]
    [InlineData(null, "token")]
    [InlineData("", "token")]
    [InlineData("conn-1", null)]
    [InlineData("conn-1", "")]
    [InlineData("conn-1", "not a data protection payload!")]
    public void IsValid_MissingOrMalformedInput_ReturnsFalse(string? connectionId, string? token)
    {
        Assert.False(ProgressTokens.IsValid(_provider, connectionId, token));
    }

    [Fact]
    public void IsValid_TokenProtectedWithDifferentPurpose_ReturnsFalse()
    {
        var foreignToken = _provider.CreateProtector("PkgDiff.Other.Purpose").Protect("conn-1");

        Assert.False(ProgressTokens.IsValid(_provider, "conn-1", foreignToken));
    }

    [Fact]
    public void IsValid_TokenFromAnotherKeyRing_ReturnsFalse()
    {
        var token = ProgressTokens.Create(new EphemeralDataProtectionProvider(), "conn-1");

        Assert.False(ProgressTokens.IsValid(_provider, "conn-1", token));
    }
}
