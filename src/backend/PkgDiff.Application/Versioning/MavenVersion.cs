namespace PkgDiff.Application.Versioning;

// A Maven Central version ordered as Maven's ComparableVersion does it:
// alpha < beta < milestone < rc < snapshot < release (ga, final) < sp < any other qualifier
public sealed class MavenVersion : IComparable<MavenVersion>, IComparable
{
    private const int MaxLength = 100;

    private static readonly string[] Qualifiers = { "alpha", "beta", "milestone", "rc", "snapshot", "", "sp" };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["ga"] = "",
        ["final"] = "",
        ["release"] = "",
        ["cr"] = "rc"
    };

    private static readonly HashSet<string> PrereleaseWords = new(StringComparer.Ordinal)
    {
        "alpha", "beta", "milestone", "rc", "snapshot", "preview", "pre", "ea", "dev", "incubating"
    };

    private readonly ListItem _items;

    private MavenVersion(ListItem items, bool isPrerelease)
    {
        _items = items;
        IsPrerelease = isPrerelease;
    }

    public bool IsPrerelease { get; }

    public static bool TryParse(string? value, out MavenVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            return false;

        value = value.Trim();
        if (!char.IsAsciiDigit(value[0]) || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_' or '+')))
            return false;

        var items = Parse(value.ToLowerInvariant(), out var isPrerelease);
        version = new MavenVersion(items, isPrerelease);
        return true;
    }

    public int CompareTo(MavenVersion? other) => other is null ? 1 : _items.CompareTo(other._items);

    public int CompareTo(object? obj) => CompareTo(obj as MavenVersion);

    private static ListItem Parse(string version, out bool isPrerelease)
    {
        isPrerelease = false;
        var root = new ListItem();
        var list = root;
        var stack = new Stack<ListItem>();
        stack.Push(list);

        var isDigit = false;
        var start = 0;

        for (var i = 0; i < version.Length; i++)
        {
            var c = version[i];
            if (c == '.')
            {
                list.Add(i == start ? IntItem.Zero : ParseItem(isDigit, version[start..i], ref isPrerelease));
                start = i + 1;
            }
            else if (c is '-' or '_' or '+')
            {
                list.Add(i == start ? IntItem.Zero : ParseItem(isDigit, version[start..i], ref isPrerelease));
                start = i + 1;
                list.Add(list = new ListItem());
                stack.Push(list);
            }
            else if (char.IsAsciiDigit(c))
            {
                if (!isDigit && i > start)
                {
                    // "m1" is milestone 1: a single letter right before a digit is a short qualifier
                    list.Add(StringItem.Of(version[start..i], followedByDigit: true, ref isPrerelease));
                    start = i;
                    list.Add(list = new ListItem());
                    stack.Push(list);
                }
                isDigit = true;
            }
            else
            {
                if (isDigit && i > start)
                {
                    list.Add(ParseItem(true, version[start..i], ref isPrerelease));
                    start = i;
                    list.Add(list = new ListItem());
                    stack.Push(list);
                }
                isDigit = false;
            }
        }

        if (version.Length > start)
            list.Add(ParseItem(isDigit, version[start..], ref isPrerelease));

        while (stack.Count > 0)
            stack.Pop().Normalize();

        return root;
    }

    private static Item ParseItem(bool isDigit, string text, ref bool isPrerelease)
    {
        if (isDigit)
        {
            var trimmed = text.TrimStart('0');
            return new IntItem(trimmed.Length == 0 ? "0" : trimmed);
        }
        return StringItem.Of(text, followedByDigit: false, ref isPrerelease);
    }

    private abstract class Item
    {
        public abstract bool IsNull { get; }

        public abstract int CompareTo(Item? other);
    }

    private sealed class IntItem : Item
    {
        public static readonly IntItem Zero = new("0");

        // Digits without leading zeros, so longer means larger and equal length compares as text
        private readonly string _digits;

        public IntItem(string digits) => _digits = digits;

        public override bool IsNull => _digits == "0";

        public override int CompareTo(Item? other) => other switch
        {
            null => IsNull ? 0 : 1,
            IntItem number => _digits.Length != number._digits.Length
                ? _digits.Length.CompareTo(number._digits.Length)
                : string.CompareOrdinal(_digits, number._digits),
            _ => 1
        };
    }

    private sealed class StringItem : Item
    {
        private readonly string _value;

        private StringItem(string value) => _value = value;

        public static StringItem Of(string value, bool followedByDigit, ref bool isPrerelease)
        {
            if (followedByDigit && value.Length == 1)
            {
                value = value[0] switch
                {
                    'a' => "alpha",
                    'b' => "beta",
                    'm' => "milestone",
                    _ => value
                };
            }

            if (Aliases.TryGetValue(value, out var alias))
                value = alias;

            if (PrereleaseWords.Contains(value))
                isPrerelease = true;

            return new StringItem(value);
        }

        public override bool IsNull => Comparable(_value) == Comparable(string.Empty);

        public override int CompareTo(Item? other) => other switch
        {
            null => string.CompareOrdinal(Comparable(_value), Comparable(string.Empty)),
            StringItem text => string.CompareOrdinal(Comparable(_value), Comparable(text._value)),
            _ => -1
        };

        // Known qualifiers by their rank; any other comes after all of them, in text order
        private static string Comparable(string qualifier)
        {
            var index = Array.IndexOf(Qualifiers, qualifier);
            return index >= 0 ? index.ToString() : $"{Qualifiers.Length}-{qualifier}";
        }
    }

    private sealed class ListItem : Item
    {
        private readonly List<Item> _items = new();

        public void Add(Item item) => _items.Add(item);

        public override bool IsNull => _items.Count == 0;

        // 1.0.0 is 1, and 1-ga is 1: trailing zeros and release words do not count
        public void Normalize()
        {
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                var item = _items[i];
                if (item.IsNull)
                    _items.RemoveAt(i);
                else if (item is not ListItem)
                    break;
            }
        }

        public override int CompareTo(Item? other)
        {
            switch (other)
            {
                case null:
                    return _items.Count == 0 ? 0 : _items[0].CompareTo(null);
                case IntItem:
                    return -1;
                case StringItem:
                    return 1;
            }

            var right = ((ListItem)other)._items;
            for (var i = 0; i < Math.Max(_items.Count, right.Count); i++)
            {
                var l = i < _items.Count ? _items[i] : null;
                var r = i < right.Count ? right[i] : null;
                var result = l is null
                    ? (r is null ? 0 : -r.CompareTo(null))
                    : l.CompareTo(r);
                if (result != 0)
                    return result;
            }
            return 0;
        }
    }
}
