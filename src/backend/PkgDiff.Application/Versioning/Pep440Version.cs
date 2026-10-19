using System.Text.RegularExpressions;

namespace PkgDiff.Application.Versioning;

// A PyPI version as PEP 440 defines it: [N!]N(.N)*[{a|b|rc}N][.postN][.devN][+local]
public sealed class Pep440Version : IComparable<Pep440Version>, IComparable
{
    private const int MaxLength = 100;

    private static readonly Regex Pattern = new(
        @"^\s*v?
          (?:(?<epoch>[0-9]+)!)?
          (?<release>[0-9]+(?:\.[0-9]+)*)
          (?:[-_\.]?(?<pre_l>alpha|a|beta|b|preview|pre|c|rc)[-_\.]?(?<pre_n>[0-9]+)?)?
          (?:(?:-(?<post_n1>[0-9]+))|(?:[-_\.]?(?<post_l>post|rev|r)[-_\.]?(?<post_n2>[0-9]+)?))?
          (?:[-_\.]?(?<dev_l>dev)[-_\.]?(?<dev_n>[0-9]+)?)?
          (?:\+(?<local>[a-z0-9]+(?:[-_\.][a-z0-9]+)*))?
          \s*$",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    private readonly long _epoch;
    private readonly long[] _release;
    private readonly (int Phase, long Number)? _pre;
    private readonly long? _post;
    private readonly long? _dev;
    private readonly string[]? _local;

    private Pep440Version(long epoch, long[] release, (int, long)? pre, long? post, long? dev, string[]? local)
    {
        _epoch = epoch;
        _release = release;
        _pre = pre;
        _post = post;
        _dev = dev;
        _local = local;
    }

    // Development releases count as pre-releases; post releases do not
    public bool IsPrerelease => _pre != null || _dev != null;

    public static bool TryParse(string? value, out Pep440Version version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            return false;

        Match match;
        try
        {
            match = Pattern.Match(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }

        if (!match.Success)
            return false;

        if (!TryNumber(match.Groups["epoch"], 0, out var epoch))
            return false;

        var segments = match.Groups["release"].Value.Split('.');
        var release = new long[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!long.TryParse(segments[i], out release[i]))
                return false;
        }

        (int, long)? pre = null;
        if (match.Groups["pre_l"].Success)
        {
            if (!TryNumber(match.Groups["pre_n"], 0, out var number))
                return false;
            pre = (PrePhase(match.Groups["pre_l"].Value), number);
        }

        long? post = null;
        if (match.Groups["post_n1"].Success || match.Groups["post_l"].Success)
        {
            var group = match.Groups["post_n1"].Success ? match.Groups["post_n1"] : match.Groups["post_n2"];
            if (!TryNumber(group, 0, out var number))
                return false;
            post = number;
        }

        long? dev = null;
        if (match.Groups["dev_l"].Success)
        {
            if (!TryNumber(match.Groups["dev_n"], 0, out var number))
                return false;
            dev = number;
        }

        var local = match.Groups["local"].Success
            ? match.Groups["local"].Value.ToLowerInvariant().Split('-', '_', '.')
            : null;

        version = new Pep440Version(epoch, release, pre, post, dev, local);
        return true;
    }

    public int CompareTo(Pep440Version? other)
    {
        if (other is null)
            return 1;

        var result = _epoch.CompareTo(other._epoch);
        if (result != 0)
            return result;

        result = CompareRelease(_release, other._release);
        if (result != 0)
            return result;

        result = PreKey().CompareTo(other.PreKey());
        if (result != 0)
            return result;

        // No post release sorts before any post release
        result = (_post ?? -1).CompareTo(other._post ?? -1);
        if (result != 0)
            return result;

        // No dev release sorts after any dev release
        result = (_dev ?? long.MaxValue).CompareTo(other._dev ?? long.MaxValue);
        if (result != 0)
            return result;

        return CompareLocal(_local, other._local);
    }

    public int CompareTo(object? obj) => CompareTo(obj as Pep440Version);

    private static bool TryNumber(Group group, long missing, out long number)
    {
        number = missing;
        return !group.Success || long.TryParse(group.Value, out number);
    }

    private static int PrePhase(string letter) => letter.ToLowerInvariant() switch
    {
        "a" or "alpha" => 0,
        "b" or "beta" => 1,
        _ => 2
    };

    // 1.0.dev1 comes before 1.0a1, and 1.0 (no pre-release) after every 1.0 pre-release
    private (int Rank, int Phase, long Number) PreKey()
    {
        if (_pre is { } pre)
            return (0, pre.Phase, pre.Number);

        return _post == null && _dev != null ? (-1, 0, 0) : (1, 0, 0);
    }

    // Trailing zeros do not count: 1.0 and 1.0.0 are the same release
    private static int CompareRelease(long[] left, long[] right)
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

    // No local part sorts first; a numeric segment sorts after a text one; a longer label after its own prefix
    private static int CompareLocal(string[]? left, string[]? right)
    {
        if (left == null || right == null)
            return (left == null ? 0 : 1) - (right == null ? 0 : 1);

        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var leftIsNumber = long.TryParse(left[i], out var leftNumber);
            var rightIsNumber = long.TryParse(right[i], out var rightNumber);

            var result = leftIsNumber && rightIsNumber
                ? leftNumber.CompareTo(rightNumber)
                : leftIsNumber != rightIsNumber
                    ? (leftIsNumber ? 1 : -1)
                    : string.CompareOrdinal(left[i], right[i]);
            if (result != 0)
                return result;
        }

        return left.Length.CompareTo(right.Length);
    }
}
