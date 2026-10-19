using PkgDiff.Application.Versioning;

namespace PkgDiff.Tests.Versioning;

public class PackageVersionTests
{
    private static PackageVersion Parse(VersionScheme scheme, string value)
    {
        Assert.True(PackageVersion.TryParse(scheme, value, out var version), $"'{value}' should parse as {scheme}");
        return version!;
    }

    [Fact]
    public void Pep440_versions_sort_in_the_order_the_specification_gives()
    {
        // The ordering example of PEP 440, extended with an epoch
        string[] ordered =
        [
            "1.dev0", "1.0.dev456", "1.0a1", "1.0a2.dev456", "1.0a12.dev456", "1.0a12", "1.0b1.dev456", "1.0b2",
            "1.0b2.post345.dev456", "1.0b2.post345", "1.0rc1.dev456", "1.0rc1", "1.0", "1.0+abc.5", "1.0+abc.7",
            "1.0+5", "1.0.post456.dev34", "1.0.post456", "1.0.15", "1.1.dev1", "2.0", "10.0", "1!0.5"
        ];

        var shuffled = ordered.Reverse().OrderBy(value => value.Length).ToList();
        var sorted = PackageVersions.ParseAll(VersionScheme.Pep440, shuffled, includePrerelease: true, out var skipped);

        Assert.Empty(skipped);
        Assert.Equal(ordered, sorted.Select(v => v.Value));
        for (var i = 1; i < sorted.Count; i++)
            Assert.True(sorted[i - 1] < sorted[i], $"{sorted[i - 1]} should be older than {sorted[i]}");
    }

    [Theory]
    [InlineData("1.0", "1.0.0")]
    [InlineData("1.0rc1", "1.0RC1")]
    [InlineData("1.0rc1", "1.0-rc.1")]
    [InlineData("1.0rc1", "1.0c1")]
    [InlineData("1.0a1", "1.0alpha1")]
    [InlineData("1.0.post1", "1.0-1")]
    [InlineData("1.0.post1", "1.0.rev1")]
    [InlineData("1.0.dev0", "1.0.dev")]
    [InlineData("1.0", "v1.0")]
    public void Pep440_spellings_of_one_version_are_equal(string left, string right)
    {
        Assert.Equal(Parse(VersionScheme.Pep440, left), Parse(VersionScheme.Pep440, right));
        Assert.Equal(0, Parse(VersionScheme.Pep440, left).CompareTo(Parse(VersionScheme.Pep440, right)));
    }

    [Theory]
    [InlineData("1.0a1", true)]
    [InlineData("1.0b2", true)]
    [InlineData("1.0rc1", true)]
    [InlineData("1.0.dev3", true)]
    [InlineData("1.0.post1.dev2", true)]
    [InlineData("1.0", false)]
    [InlineData("2.0.post1", false)]
    [InlineData("1!2.0", false)]
    [InlineData("2024.10.4", false)]
    public void Pep440_post_releases_are_final_and_dev_releases_are_not(string value, bool isPrerelease)
    {
        Assert.Equal(isPrerelease, Parse(VersionScheme.Pep440, value).IsPrerelease);
    }

    [Theory]
    [InlineData(VersionScheme.Pep440, "")]
    [InlineData(VersionScheme.Pep440, "not-a-version")]
    [InlineData(VersionScheme.Pep440, "1.0-")]
    [InlineData(VersionScheme.Pep440, "1.0+")]
    [InlineData(VersionScheme.Pep440, "1..0")]
    [InlineData(VersionScheme.Pep440, "99999999999999999999999.0")]
    [InlineData(VersionScheme.SemVer, "latest")]
    [InlineData(VersionScheme.SemVer, "1.0rc1")]
    [InlineData(VersionScheme.NuGet, "1.0.0.0.0")]
    [InlineData(VersionScheme.SemVer, "")]
    public void A_string_that_is_not_a_version_of_the_scheme_does_not_parse(VersionScheme scheme, string value)
    {
        Assert.False(PackageVersion.TryParse(scheme, value, out var version));
        Assert.Null(version);
    }

    [Fact]
    public void An_overlong_pep440_string_is_rejected_without_being_matched()
    {
        Assert.False(PackageVersion.TryParse(VersionScheme.Pep440, "1." + new string('0', 200), out _));
    }

    [Fact]
    public void SemVer_versions_order_numerically_and_put_pre_releases_before_their_release()
    {
        string[] ordered = ["1.2.0", "1.9.0", "1.10.0-alpha.1", "1.10.0-alpha.2", "1.10.0-next.0", "1.10.0", "2.0.0-0", "2.0.0"];

        var sorted = PackageVersions.ParseAll(VersionScheme.SemVer, ordered.Reverse(), includePrerelease: true, out _);

        Assert.Equal(ordered, sorted.Select(v => v.Value));
        Assert.Equal(new[] { "1.2.0", "1.9.0", "1.10.0", "2.0.0" },
            PackageVersions.ParseAll(VersionScheme.SemVer, ordered, includePrerelease: false, out _).Select(v => v.Value));
    }

    [Fact]
    public void A_version_keeps_the_spelling_of_its_registry()
    {
        Assert.Equal("1.0.0+1", Parse(VersionScheme.SemVer, "1.0.0+1").Value);
        Assert.Equal("1.0rc1", Parse(VersionScheme.Pep440, " 1.0rc1 ").Value);
        Assert.Equal(NuGet.Versioning.NuGetVersion.Parse("1.0.0-Beta+build").ToString(), Parse(VersionScheme.NuGet, "1.0.0-Beta+build").Value);
        Assert.Equal("1.0rc1", Parse(VersionScheme.Pep440, "1.0rc1").ToString());
    }

    [Fact]
    public void ParseAll_reports_what_it_could_not_parse_instead_of_failing()
    {
        var sorted = PackageVersions.ParseAll(VersionScheme.Pep440, ["2.0", "garbage", "1.0", "1.0rc1", ""], includePrerelease: false, out var skipped);

        Assert.Equal(new[] { "1.0", "2.0" }, sorted.Select(v => v.Value));
        Assert.Equal(new[] { "garbage", "" }, skipped);
    }
}
