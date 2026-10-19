using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.API.Extensions;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Identity;

// WR-02: the seeder bootstraps the first administrator only and never overrides admin decisions afterwards.
public class AdminSeedingTests
{
    private const string AdminUserName = "pkgdiff_admin";

    private static readonly string Password = "Seed-" + Guid.NewGuid().ToString("N");

    private static InMemoryIdentityStore CreateStore()
    {
        var store = new InMemoryIdentityStore();
        store.AddRole("Admin");
        store.AddRole("Viewer");
        return store;
    }

    private static Task SeedAsync(InMemoryIdentityStore store, string? adminPassword) =>
        DatabaseSeeder.SeedAdminUserAsync(store.CreateUserManager(), adminPassword, NullLogger.Instance);

    private static async Task<Domain.Entities.ApplicationUser?> FindAdminUserAsync(InMemoryIdentityStore store) =>
        await store.CreateUserManager().FindByNameAsync(AdminUserName);

    [Fact]
    public async Task FreshInstall_CreatesPkgdiffAdminWithTheAdminRole()
    {
        var store = CreateStore();

        await SeedAsync(store, Password);

        var admin = await FindAdminUserAsync(store);
        Assert.NotNull(admin);
        Assert.Equal(["Admin"], store.RolesOf(admin!));
    }

    [Fact]
    public async Task FreshInstall_WithoutPassword_FailsStartup()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() => SeedAsync(store, null));
    }

    [Fact]
    public async Task DemotedPkgdiffAdmin_IsNotRePromoted_WhenAnotherAdminExists()
    {
        var store = CreateStore();
        store.AddUser("other-admin", "Admin");
        var demoted = store.AddUser(AdminUserName, "Viewer");

        await SeedAsync(store, Password);

        Assert.Equal(["Viewer"], store.RolesOf(demoted));
    }

    [Fact]
    public async Task DeletedPkgdiffAdmin_IsNotRecreated_WhenAnotherAdminExistsAndPasswordIsStillSet()
    {
        var store = CreateStore();
        store.AddUser("other-admin", "Admin");

        await SeedAsync(store, Password);

        Assert.Null(await FindAdminUserAsync(store));
    }

    [Fact]
    public async Task DeletedPkgdiffAdmin_DoesNotFailStartup_WhenAnotherAdminExistsAndPasswordIsUnset()
    {
        var store = CreateStore();
        store.AddUser("other-admin", "Admin");

        await SeedAsync(store, null);

        Assert.Null(await FindAdminUserAsync(store));
    }

    [Fact]
    public async Task ExistingPkgdiffAdminWithoutAnyAdmin_IsPromoted_SoTheSystemStaysManageable()
    {
        var store = CreateStore();
        var admin = store.AddUser(AdminUserName);

        await SeedAsync(store, null);

        Assert.Equal(["Admin"], store.RolesOf(admin));
    }
}
