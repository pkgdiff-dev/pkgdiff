using System.Text;

namespace PkgDiff.Shared.Options;

public sealed class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public const int MinimumSecretBytes = 32;

    // Known template/example values that must never sign production tokens.
    public static readonly IReadOnlyList<string> Placeholders = new[]
    {
        "YourSuperSecretKeyThatIsAtLeast32CharactersLong!",
        "CHANGE_ME",
        "ExampleSecretKey123"
    };

    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    public static bool HasMinimumLength(JwtSettings settings)
    {
        return Encoding.UTF8.GetByteCount(settings.SecretKey ?? string.Empty) >= MinimumSecretBytes;
    }

    public static bool IsPlaceholder(JwtSettings settings)
    {
        return Placeholders.Any(p => string.Equals(p, settings.SecretKey, StringComparison.Ordinal));
    }
}
