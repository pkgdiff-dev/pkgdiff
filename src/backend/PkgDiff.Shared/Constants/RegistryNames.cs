namespace PkgDiff.Shared.Constants;

public static class RegistryNames
{
    public const string NuGet = "nuget";
    public const string Npm = "npm";
    public const string PyPI = "pypi";
    public const string PubDev = "pub.dev";

    public static readonly IReadOnlyList<string> All = new[] { NuGet, Npm, PyPI, PubDev };

    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pub"] = PubDev
        };

    public static string? Canonical(string? registry)
    {
        if (string.IsNullOrEmpty(registry))
            return null;

        if (Aliases.TryGetValue(registry, out var canonical))
            return canonical;

        return All.FirstOrDefault(name => name.Equals(registry, StringComparison.OrdinalIgnoreCase));
    }

    // A registry's row may be stored under any of its spellings
    public static IReadOnlyList<string> SpellingsOf(string registry)
    {
        var canonical = Canonical(registry);
        if (canonical is null)
            return new[] { registry.ToLowerInvariant() };

        return Aliases
            .Where(alias => alias.Value == canonical)
            .Select(alias => alias.Key)
            .Prepend(canonical)
            .ToList();
    }
}
