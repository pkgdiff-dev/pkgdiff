using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Data;

namespace PkgDiff.API.Extensions;

public static class DatabaseSeeder
{
    public static async Task SeedDataAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseSeeder");
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        // Seed Roles
        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole("Admin")), "create role Admin");
        }

        if (!await roleManager.RoleExistsAsync("Viewer"))
        {
            EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole("Viewer")), "create role Viewer");
        }

        // Compose passes ${ADMIN_PASSWORD:-}, so a blank value means "not set".
        var adminPassword = GetConfiguredSecret(configuration, "AdminSettings:AdminPassword");
        var viewerPassword = GetConfiguredSecret(configuration, "AdminSettings:ViewerPassword");

        await SeedAdminUserAsync(userManager, adminPassword, logger);

        // Seed Viewer User (optional)
        var viewerUser = await userManager.FindByNameAsync("pkgdiff_viewer");
        if (viewerUser == null && viewerPassword != null)
        {
            viewerUser = new ApplicationUser
            {
                UserName = "pkgdiff_viewer",
                Email = "viewer@pkgdiff.dev",
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };

            EnsureSucceeded(await userManager.CreateAsync(viewerUser, viewerPassword), "create viewer user pkgdiff_viewer");
            logger.LogInformation("Viewer user {UserName} created", "pkgdiff_viewer");
        }

        if (viewerUser != null)
        {
            if (!await userManager.IsInRoleAsync(viewerUser, "Viewer"))
            {
                EnsureSucceeded(await userManager.AddToRoleAsync(viewerUser, "Viewer"), "assign role Viewer to pkgdiff_viewer");
                logger.LogInformation("Role {Role} assigned to {UserName}", "Viewer", "pkgdiff_viewer");
            }
        }
        else
        {
            logger.LogInformation("Viewer user not seeded: AdminSettings:ViewerPassword (env VIEWER_PASSWORD) is not set.");
        }

        // ──────────────────────────────
        // Seed Role Permissions
        // ──────────────────────────────

        // 1. Admin Role (Grant ALL permissions)
        var adminRole = await roleManager.FindByNameAsync("Admin");
        if (adminRole != null)
        {
            var allPermissions = PkgDiff.Shared.Constants.Permissions.GetAllPermissions();
            var existingClaims = await roleManager.GetClaimsAsync(adminRole);
            var newClaimsCount = 0;

            foreach (var permission in allPermissions)
            {
                if (!existingClaims.Any(c => c.Type == "Permission" && c.Value == permission))
                {
                    var result = await roleManager.AddClaimAsync(adminRole, new System.Security.Claims.Claim("Permission", permission));
                    EnsureSucceeded(result, $"add permission {permission} to role Admin");
                    newClaimsCount++;
                }
            }
            if (newClaimsCount > 0) logger.LogInformation("Added {Count} new permissions to role {Role}", newClaimsCount, "Admin");
        }

        // 2. Viewer Role (Grant VIEW permissions)
        var viewerRole = await roleManager.FindByNameAsync("Viewer");
        if (viewerRole != null)
        {
            var viewPermissions = new[]
            {
                PkgDiff.Shared.Constants.Permissions.Users.View,
                PkgDiff.Shared.Constants.Permissions.Roles.View,
                PkgDiff.Shared.Constants.Permissions.Packages.View,
                PkgDiff.Shared.Constants.Permissions.Registries.View,
                PkgDiff.Shared.Constants.Permissions.Settings.View,
                PkgDiff.Shared.Constants.Permissions.Security.ViewLogs,
                PkgDiff.Shared.Constants.Permissions.Health.View,
                PkgDiff.Shared.Constants.Permissions.Jobs.View,
                PkgDiff.Shared.Constants.Permissions.VersionExcludePatterns.View,
                PkgDiff.Shared.Constants.Permissions.ScrapeLogs.View
            };

            var existingClaims = await roleManager.GetClaimsAsync(viewerRole);
            var newClaimsCount = 0;

            foreach (var permission in viewPermissions)
            {
                if (!existingClaims.Any(c => c.Type == "Permission" && c.Value == permission))
                {
                    var result = await roleManager.AddClaimAsync(viewerRole, new System.Security.Claims.Claim("Permission", permission));
                    EnsureSucceeded(result, $"add permission {permission} to role Viewer");
                    newClaimsCount++;
                }
            }
            if (newClaimsCount > 0) logger.LogInformation("Added {Count} new permissions to role {Role}", newClaimsCount, "Viewer");
        }

        // Seed System Settings: insert missing known keys with their flags, repair flags of existing
        // known keys and encrypt plaintext secrets. Idempotent: a second run changes nothing and never re-encrypts.
        // The protector is resolved first: in Production a missing SETTINGS_ENCRYPTION_KEY stops startup here.
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        var existingSettings = await context.SystemSettings.ToListAsync();
        var createdSettings = SystemSettingsMaintenance.CreateMissing(existingSettings.Select(s => s.Key));
        if (createdSettings.Count > 0)
        {
            await context.SystemSettings.AddRangeAsync(createdSettings);
        }

        var changedSettings = SystemSettingsMaintenance.Apply(existingSettings.Concat(createdSettings), protector);
        if (createdSettings.Count > 0 || changedSettings > 0)
        {
            await context.SaveChangesAsync();
            logger.LogInformation("System settings: {Added} added, {Changed} updated", createdSettings.Count, changedSettings);
        }

        // Seed Registries
        if (!await context.Registries.AnyAsync())
        {
            var registries = new[]
            {
                new Registry { Value = "nuget", Label = "NuGet", Icon = "/icons/nuget.svg", Disabled = false, DisplayOrder = 1 },
                new Registry { Value = "npm", Label = "npm", Icon = "/icons/npm.svg", Disabled = false, DisplayOrder = 2 },
                new Registry { Value = "pypi", Label = "PyPI", Icon = "/icons/pypi.svg", Disabled = false, DisplayOrder = 3 },
                new Registry { Value = "maven", Label = "Maven", Icon = "/icons/maven.svg", Disabled = true, DisplayOrder = 4 }
            };

            await context.Registries.AddRangeAsync(registries);
            await context.SaveChangesAsync();
            logger.LogInformation("Registries seeded");
        }
        else
        {
            var pypiRegistry = await context.Registries.FirstOrDefaultAsync(r => r.Value == "pypi");
            if (pypiRegistry != null && pypiRegistry.Disabled)
            {
                pypiRegistry.Disabled = false;
                await context.SaveChangesAsync();
                logger.LogInformation("Registry {Registry} enabled", "pypi");
            }
        }

        // Seed Version Exclude Patterns
        var expectedPatterns = new[]
        {
            new { Id = 1, Pattern = "dev", Description = "Development builds (e.g., 1.0.0-dev-123)" },
            new { Id = 2, Pattern = "ci", Description = "Continuous Integration builds (e.g., 1.0.0-ci-456)" },
            new { Id = 3, Pattern = "build", Description = "Build number versions (e.g., 1.0.0-build-789)" },
            new { Id = 4, Pattern = "nightly", Description = "Nightly builds (e.g., 1.0.0-nightly-20240101)" },
            new { Id = 5, Pattern = "snapshot", Description = "Snapshot versions (e.g., 1.0.0-snapshot)" },
            new { Id = 6, Pattern = "local", Description = "Local builds (e.g., 1.0.0-local)" },
            new { Id = 7, Pattern = "test", Description = "Test builds (e.g., 1.0.0-test-123)" },
            new { Id = 8, Pattern = "rc", Description = "Release Candidate versions (e.g., 1.0.0-rc1)" },
            new { Id = 9, Pattern = "alpha", Description = "Alpha pre-release versions (e.g., 1.0.0-alpha)" },
            new { Id = 10, Pattern = "beta", Description = "Beta pre-release versions (e.g., 1.0.0-beta)" },
            new { Id = 11, Pattern = "preview", Description = "Preview versions (e.g., 1.0.0-preview1)" },
            new { Id = 12, Pattern = "exp", Description = "Experimental builds (e.g., 1.0.0-exp)" },
            new { Id = 13, Pattern = "hotfix", Description = "Hotfix builds (e.g., 1.0.1-hotfix)" },
            new { Id = 14, Pattern = "patch", Description = "Patch builds (e.g., 1.0.0-patch-1)" },
            new { Id = 15, Pattern = "canary", Description = "Canary builds (e.g., 1.0.0-canary)" },
            new { Id = 16, Pattern = "release", Description = "Release versions (e.g., 1.0.0-release)" },
            new { Id = 17, Pattern = "stable", Description = "Stable versions (e.g., 1.0.0-stable)" },
            new { Id = 18, Pattern = "final", Description = "Final versions (e.g., 1.0.0-final)" },
            new { Id = 19, Pattern = "qa", Description = "QA builds (e.g., 1.0.0-qa)" },
            new { Id = 20, Pattern = "uat", Description = "UAT builds (e.g., 1.0.0-uat)" },
            new { Id = 21, Pattern = "integration", Description = "Integration builds (e.g., 1.0.0-integration)" },
            new { Id = 22, Pattern = "staging", Description = "Staging builds (e.g., 1.0.0-staging)" },
            new { Id = 23, Pattern = "prod", Description = "Production builds (e.g., 1.0.0-prod)" },
            new { Id = 24, Pattern = "debug", Description = "Debug builds (e.g., 1.0.0-debug)" },
            new { Id = 25, Pattern = "rc.", Description = "Release Candidate shorthand (e.g., 1.0.0-rc.1)" },
            new { Id = 26, Pattern = "pr", Description = "Pull Request builds (e.g., 1.0.0-pr-123)" },
            new { Id = 27, Pattern = "feature", Description = "Feature branch builds (e.g., 1.0.0-feature-login)" },
            new { Id = 28, Pattern = "bugfix", Description = "Bugfix branch builds (e.g., 1.0.0-bugfix-issue123)" },
            new { Id = 29, Pattern = "release-branch", Description = "Release branch builds (e.g., 1.0.0-release-branch)" },
            new { Id = 30, Pattern = "m", Description = "Milestone builds (e.g., 1.0.0-m1)" }
        };

        var existingPatterns = await context.VersionExcludePatterns.ToListAsync();
        var addedCount = 0;
        var updatedCount = 0;

        foreach (var expected in expectedPatterns)
        {
            var existing = existingPatterns.FirstOrDefault(p => p.Id == expected.Id);
            if (existing == null)
            {
                context.VersionExcludePatterns.Add(new VersionExcludePattern
                {
                    Id = expected.Id,
                    Pattern = expected.Pattern,
                    Description = expected.Description,
                    IsActive = true
                });
                addedCount++;
            }
            else if (existing.Pattern != expected.Pattern)
            {
                existing.Pattern = expected.Pattern;
                existing.Description = expected.Description;
                updatedCount++;
            }
        }

        if (addedCount > 0 || updatedCount > 0)
        {
            await context.SaveChangesAsync();
            logger.LogInformation("Version exclude patterns seeded: {Added} added, {Updated} updated", addedCount, updatedCount);
        }
    }
    // Bootstrap only: once any user holds Admin, a demoted or deleted pkgdiff_admin is an administrator's
    // decision and is never re-promoted or recreated here.
    public static async Task SeedAdminUserAsync(UserManager<ApplicationUser> userManager, string? adminPassword, ILogger logger)
    {
        if ((await userManager.GetUsersInRoleAsync("Admin")).Count > 0)
        {
            if (adminPassword != null)
            {
                logger.LogWarning("AdminSettings:AdminPassword (env ADMIN_PASSWORD) is set but ignored because an administrator already exists; remove it from the environment.");
            }

            return;
        }

        var adminUser = await userManager.FindByNameAsync("pkgdiff_admin");
        if (adminUser == null)
        {
            if (adminPassword == null)
            {
                throw new InvalidOperationException(
                    "No administrator exists and AdminSettings:AdminPassword (env ADMIN_PASSWORD) is not set. Set ADMIN_PASSWORD and restart.");
            }

            adminUser = new ApplicationUser
            {
                UserName = "pkgdiff_admin",
                Email = "admin@pkgdiff.dev",
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };

            EnsureSucceeded(await userManager.CreateAsync(adminUser, adminPassword), "create admin user pkgdiff_admin");
            logger.LogInformation("Admin user {UserName} created", "pkgdiff_admin");
        }

        // Reached only when no administrator exists at all (fresh install or an interrupted first seed)
        EnsureSucceeded(await userManager.AddToRoleAsync(adminUser, "Admin"), "assign role Admin to pkgdiff_admin");
        logger.LogInformation("Role {Role} assigned to {UserName}", "Admin", "pkgdiff_admin");
    }

    public static string? GetConfiguredSecret(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (result.Succeeded)
        {
            return;
        }

        var descriptions = string.Join("; ", result.Errors.Select(e => e.Description));
        throw new InvalidOperationException($"Database seeding failed to {action}: {descriptions}");
    }
}
