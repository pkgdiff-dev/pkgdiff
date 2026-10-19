namespace PkgDiff.Shared.Validation;

public static class PackageUrlEncoder
{
    public static string EscapeSegment(string value) => Uri.EscapeDataString(value);

    // npm addresses scoped packages as one path segment: @scope%2Fname
    public static string EscapeNpmPackagePath(string packageId)
    {
        if (packageId.StartsWith('@'))
        {
            var slashIndex = packageId.IndexOf('/');
            if (slashIndex > 0)
            {
                var scope = packageId[1..slashIndex];
                var name = packageId[(slashIndex + 1)..];
                return "@" + EscapeSegment(scope) + "%2F" + EscapeSegment(name);
            }
        }

        return EscapeSegment(packageId);
    }
}
