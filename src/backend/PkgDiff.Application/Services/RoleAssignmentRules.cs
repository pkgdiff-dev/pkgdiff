namespace PkgDiff.Application.Services;

public static class RoleAssignmentRules
{
    public const string DefaultRole = "Viewer";
    public const string AdminRole = "Admin";

    public static (bool Allowed, int StatusCode, string? Error) Evaluate(
        IReadOnlyCollection<string> requestedRoles,
        bool callerIsAdmin,
        IReadOnlyCollection<string> existingRoles)
    {
        if (requestedRoles.Count == 0)
        {
            return (false, 400, "At least one role is required.");
        }

        foreach (var role in requestedRoles)
        {
            if (!existingRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                return (false, 400, $"Role '{role}' does not exist.");
            }
        }

        if (!callerIsAdmin && requestedRoles.Any(r => string.Equals(r, AdminRole, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, 403, "Only administrators can assign the Admin role.");
        }

        return (true, 200, null);
    }

    // Permission policies pass on the claim alone, so a non-Admin must never obtain (through a role they assign,
    // or an account they take over) a permission they do not already hold
    public static (bool Allowed, int StatusCode, string? Error) EvaluatePermissionSubset(
        IEnumerable<string> permissions,
        bool callerIsAdmin,
        IReadOnlyCollection<string> callerPermissions,
        string error)
    {
        if (callerIsAdmin)
        {
            return (true, 200, null);
        }

        return permissions.Any(p => !callerPermissions.Contains(p, StringComparer.Ordinal))
            ? (false, 403, error)
            : (true, 200, null);
    }

    public static (bool Allowed, int StatusCode, string? Error) EvaluateRemoval(
        IReadOnlyCollection<string> rolesToRemove,
        bool callerIsAdmin,
        int adminCount)
    {
        if (!rolesToRemove.Any(r => string.Equals(r, AdminRole, StringComparison.OrdinalIgnoreCase)))
        {
            return (true, 200, null);
        }

        if (!callerIsAdmin)
        {
            return (false, 403, "Only administrators can remove the Admin role.");
        }

        if (adminCount <= 1)
        {
            return (false, 400, "The last administrator cannot lose the Admin role.");
        }

        return (true, 200, null);
    }

    public static (bool Allowed, int StatusCode, string? Error) EvaluateDeletion(
        bool targetIsAdmin,
        bool callerIsAdmin,
        int adminCount)
    {
        if (!targetIsAdmin)
        {
            return (true, 200, null);
        }

        if (!callerIsAdmin)
        {
            return (false, 403, "Only administrators can delete an administrator.");
        }

        if (adminCount <= 1)
        {
            return (false, 400, "The last administrator cannot be deleted.");
        }

        return (true, 200, null);
    }
}
