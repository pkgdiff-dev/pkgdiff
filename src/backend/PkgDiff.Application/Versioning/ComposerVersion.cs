using System.Text.RegularExpressions;

namespace PkgDiff.Application.Versioning;

// A Packagist version as Composer orders it: dev < alpha < beta < RC < stable < patch
public sealed class ComposerVersion : IComparable<ComposerVersion>, IComparable
{
    private const int MaxLength = 100;
    private const int MaxReleaseSegments = 4;
    private const int StableRank = 4;

    private static readonly Regex Pattern = new(
        @"^v?
          (?<release>[0-9]+(?:\.[0-9]+)*)
          (?:[._-]?(?<stability>stable|beta|b|rc|alpha|a|patch|pl|p)(?<number>[.-]?[0-9]+(?:[.-][0-9]+)*)?)?
          (?<dev>[.-]?dev)?
          (?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    private readonly long[] _release;
    private readonly int _rank;
    private readonly long[] _number;
    private readonly bool _dev;

    private ComposerVersion(long[] release, int rank, long[] number, bool dev)
    {
        _release = release;
        _rank = rank;
        _number = number;
        _dev = dev;
    }

    public bool IsPrerelease => _rank < StableRank || _dev;

    public static bool TryParse(string? value, out ComposerVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            return false;

        Match match;
        try
        {
            match = Pattern.Match(value.Trim());
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }

        if (!match.Success)
            return false;

        var segments = match.Groups["release"].Value.Split('.');
        if (segments.Length > MaxReleaseSegments || !TryNumbers(segments, out var release))
            return false;

        var number = Array.Empty<long>();
        if (match.Groups["number"].Success
            && !TryNumbers(match.Groups["number"].Value.Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries), out number))
            return false;

        var dev = match.Groups["dev"].Success;
        var rank = match.Groups["stability"].Success
            ? RankOf(match.Groups["stability"].Value)
            : dev ? 0 : StableRank;

        version = new ComposerVersion(release, rank, number, dev && match.Groups["stability"].Success);
        return true;
    }

    public int CompareTo(ComposerVersion? other)
    {
        if (other is null)
            return 1;

        var result = CompareNumbers(_release, other._release);
        if (result != 0)
            return result;

        result = _rank.CompareTo(other._rank);
        if (result != 0)
            return result;

        result = CompareNumbers(_number, other._number);
        if (result != 0)
            return result;

        // 1.0.0-beta1-dev comes before 1.0.0-beta1
        return other._dev.CompareTo(_dev);
    }

    public int CompareTo(object? obj) => CompareTo(obj as ComposerVersion);

    private static int RankOf(string stability) => stability.ToLowerInvariant() switch
    {
        "alpha" or "a" => 1,
        "beta" or "b" => 2,
        "rc" => 3,
        "stable" => StableRank,
        _ => 5
    };

    private static bool TryNumbers(string[] parts, out long[] numbers)
    {
        numbers = new long[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!long.TryParse(parts[i], out numbers[i]))
                return false;
        }
        return true;
    }

    // Trailing zeros do not count: 1.0 and 1.0.0.0 are the same release
    private static int CompareNumbers(long[] left, long[] right)
    {
        var length = Math.Max(left.Length, right.Length);
        for (var i = 0; i < length; i++)
        {
            var result = (i < left.Length ? left[i] : 0).CompareTo(i < right.Length ? right[i] : 0);
            if (result != 0)
                return result;
        }
        return 0;
    }
}
