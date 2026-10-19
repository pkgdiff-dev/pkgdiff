namespace PkgDiff.Shared.Constants;

// The registries the application serves, by the name used in URLs and in the Registries table
public static class RegistryNames
{
    public const string NuGet = "nuget";
    public const string Npm = "npm";
    public const string PyPI = "pypi";
    public const string PubDev = "pub.dev";

    public static readonly IReadOnlyList<string> All = new[] { NuGet, Npm, PyPI, PubDev };

    // Other spellings a registry is also known by
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pub"] = PubDev
        };

    // The registry a spelling stands for; null when it is none of them
    public static string? Canonical(string? registry)
    {
        if (string.IsNullOrEmpty(registry))
            return null;

        if (Aliases.TryGetValue(registry, out var canonical))
            return canonical;

        return All.FirstOrDefault(name => name.Equals(registry, StringComparison.OrdinalIgnoreCase));
    }

    // Every spelling of the same registry: its row may be stored under any of them
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
