namespace PkgDiff.Shared.Constants;

public static class Permissions
{
    public static class Users
    {
        public const string View = "Permissions.Users.View";
        public const string Create = "Permissions.Users.Create";
        public const string Edit = "Permissions.Users.Edit";
        public const string Delete = "Permissions.Users.Delete";
    }

    public static class Roles
    {
        public const string View = "Permissions.Roles.View";
        public const string Create = "Permissions.Roles.Create";
        public const string Edit = "Permissions.Roles.Edit";
        public const string Delete = "Permissions.Roles.Delete";
    }

    public static class Packages
    {
        public const string View = "Permissions.Packages.View";
        public const string Delete = "Permissions.Packages.Delete";
    }

    public static class Registries
    {
        public const string View = "Permissions.Registries.View";
        public const string Create = "Permissions.Registries.Create";
        public const string Edit = "Permissions.Registries.Edit";
        public const string Delete = "Permissions.Registries.Delete";
    }

    public static class Settings
    {
        public const string View = "Permissions.Settings.View";
        public const string Edit = "Permissions.Settings.Edit";
    }

    public static class Security
    {
        public const string ViewLogs = "Permissions.Security.ViewLogs";
        public const string ManageIps = "Permissions.Security.ManageIps";
        public const string ManageLogs = "Permissions.Security.ManageLogs";
    }

    public static class Health
    {
        public const string View = "Permissions.Health.View";
    }

    public static class Jobs
    {
        public const string View = "Permissions.Jobs.View";
        public const string Manage = "Permissions.Jobs.Manage";
    }

    public static class ScrapeLogs
    {
        public const string View = "Permissions.ScrapeLogs.View";
        public const string Manage = "Permissions.ScrapeLogs.Manage";
    }

    public static class VersionExcludePatterns
    {
        public const string View = "Permissions.VersionExcludePatterns.View";
        public const string Create = "Permissions.VersionExcludePatterns.Create";
        public const string Edit = "Permissions.VersionExcludePatterns.Edit";
        public const string Delete = "Permissions.VersionExcludePatterns.Delete";
    }

    public static class Blog
    {
        public const string View = "Permissions.Blog.View";
        public const string Manage = "Permissions.Blog.Manage";
    }

    public static class Contributors
    {
        public const string Manage = "Permissions.Contributors.Manage";
    }

    public static class SocialMedia
    {
        public const string View = "Permissions.SocialMedia.View";
        public const string Manage = "Permissions.SocialMedia.Manage";
    }

    public static List<string> GetAllPermissions()
    {
        var permissions = new List<string>();
        foreach (var module in typeof(Permissions).GetNestedTypes())
        {
            foreach (var field in module.GetFields())
            {
                permissions.Add(field.GetValue(null)?.ToString() ?? string.Empty);
            }
        }
        return permissions;
    }
}
