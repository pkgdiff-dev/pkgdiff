using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PkgDiff.API.Services;
using PkgDiff.Tests.Postgres;

namespace PkgDiff.Tests.Integration;

public sealed class HostFixture : IAsyncLifetime
{
    public PkgDiffHost? Host { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (!PostgresDatabase.IsConfigured)
            return;

        await PostgresDatabase.WaitUntilReachableAsync();
        Host = new PkgDiffHost();
    }

    public async ValueTask DisposeAsync()
    {
        if (Host == null)
            return;

        await Host.DisposeAsync();

        // Throws if Hangfire's static log provider still points at the disposed host
        _ = new global::Hangfire.AutomaticRetryAttribute();
    }
}

public class HostIntegrationTests : IClassFixture<HostFixture>
{
    private static readonly DateTime Published = new(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly HostFixture _fixture;

    public HostIntegrationTests(HostFixture fixture)
    {
        _fixture = fixture;
    }

    private PkgDiffHost Host
    {
        get
        {
            PostgresDatabase.SkipUnlessConfigured();
            return _fixture.Host!;
        }
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<HttpClient> SignedInAsync(string username, string password)
    {
        var client = Host.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var token = (await ReadJsonAsync(login)).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task The_host_starts_on_an_empty_database_and_reports_itself_ready()
    {
        using var client = Host.CreateClient();

        var live = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var checks = (await ReadJsonAsync(ready)).GetProperty("checks");
        Assert.Equal("healthy", checks.GetProperty("database").GetString());
        Assert.Equal("healthy", checks.GetProperty("redis").GetString());

        var registries = await ReadJsonAsync(await client.GetAsync("/api/package/registries", TestContext.Current.CancellationToken));
        Assert.Contains(registries.EnumerateArray(), r => r.GetProperty("value").GetString() == "npm");
        Assert.Empty(await Host.WithDatabaseAsync(db => db.Database.GetPendingMigrationsAsync()));
    }

    [Fact]
    public async Task Admin_endpoints_need_a_valid_token_and_the_permission_of_the_action()
    {
        using var anonymous = Host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/dashboard", TestContext.Current.CancellationToken)).StatusCode);

        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/users", TestContext.Current.CancellationToken)).StatusCode);

        using var wrongPassword = Host.CreateClient();
        var refused = await wrongPassword.PostAsJsonAsync(
            "/api/auth/login", new { username = "pkgdiff_admin", password = "wrong" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        using var admin = await SignedInAsync("pkgdiff_admin", PkgDiffHost.AdminPassword);
        var dashboard = await admin.GetAsync("/api/admin/dashboard", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.True((await ReadJsonAsync(dashboard)).TryGetProperty("cachedPackages", out _));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/security/ip-statistics", TestContext.Current.CancellationToken)).StatusCode);

        using var viewer = await SignedInAsync("pkgdiff_viewer", PkgDiffHost.ViewerPassword);
        foreach (var path in new[] { "/api/admin/users", "/api/admin/roles", "/api/admin/packages", "/api/admin/registries", "/api/admin/settings", "/api/admin/scrape-logs", "/api/admin/security/request-logs" })
            Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(path, TestContext.Current.CancellationToken)).StatusCode);

        var block = await viewer.PostAsJsonAsync(
            "/api/admin/security/block-ip", new { ipAddress = "203.0.113.50", reason = "test", blockType = "Permanent" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, block.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync("/api/admin/search-history", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync("/api/admin/scrape-logs/clear-resolved", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(0, await Host.WithDatabaseAsync(db => db.BlockedIps.CountAsync()));
    }

    [Fact]
    public async Task An_analysis_runs_end_to_end_and_records_the_visitor_behind_the_proxy()
    {
        Host.Npm.Publish("host-test-pad", ("1.0.0", Published.AddDays(-30)), ("1.1.0", Published));
        using var client = Host.CreateClient();

        // The Prometheus endpoint starts to follow the application's meter with its first scrape
        Assert.Equal(HttpStatusCode.OK, (await ScrapeMetricsAsync(client, "172.28.0.9")).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/package/analyze")
        {
            Content = JsonContent.Create(new { packageId = "host-test-pad", registry = "npm", currentVersion = "1.0.0", targetVersion = "1.1.0" })
        };
        // The compose network is trusted, so the forwarded address is the visitor
        request.Headers.Add(PkgDiffHost.PeerHeader, "172.28.0.7");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadJsonAsync(response);
        Assert.Equal(new[] { "1.1.0", "1.0.0" }, result.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("version").GetString()));
        Assert.All(result.GetProperty("versions").EnumerateArray(), v => Assert.Equal("NPM", v.GetProperty("releaseNotesSource").GetString()));

        var search = await Host.WithDatabaseAsync(db => db.SearchHistories.SingleAsync(s => s.PackageId == "host-test-pad"));
        Assert.Equal("203.0.113.9", search.IpAddress);

        var package = await Host.WithDatabaseAsync(db => db.CachedPackages.SingleAsync(p => p.PackageId == "host-test-pad"));
        Assert.Equal("1.1.0", package.LatestVersion);
        Assert.Equal(Published, package.LatestVersionDate);

        Assert.Single(Host.Jobs.Enqueued, job => job == "IPackageAnalysisService.ScrapePackageReleaseNotesAsync");

        var feed = await ReadJsonAsync(await client.GetAsync("/api/package/live-updates", TestContext.Current.CancellationToken));
        var entry = Assert.Single(feed.GetProperty("data").EnumerateArray(), e => e.GetProperty("packageId").GetString() == "host-test-pad");
        Assert.Equal("1.1.0", entry.GetProperty("version").GetString());
        Assert.Equal(Published, entry.GetProperty("updatedAt").GetDateTime().ToUniversalTime());

        var metrics = await (await ScrapeMetricsAsync(client, "172.28.0.9")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(metrics.Split('\n'), line =>
            line.StartsWith("pkgdiff_analyses{", StringComparison.Ordinal) && line.Contains("registry=\"npm\"") && line.Contains("outcome=\"ok\""));
        Assert.Contains("pkgdiff_analysis_duration_count{", metrics);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ScrapeMetricsAsync(client, "198.51.100.77")).StatusCode);
    }

    private static async Task<HttpResponseMessage> ScrapeMetricsAsync(HttpClient client, string peer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Add(PkgDiffHost.PeerHeader, peer);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_forwarded_address_from_an_untrusted_peer_is_ignored_and_bad_input_is_a_400()
    {
        using var client = Host.CreateClient();
        using var spoofed = new HttpRequestMessage(HttpMethod.Post, "/api/package/analyze")
        {
            Content = JsonContent.Create(new { packageId = "does-not-exist-on-the-stub", registry = "npm", currentVersion = "1.0.0", targetVersion = "2.0.0" })
        };
        spoofed.Headers.Add(PkgDiffHost.PeerHeader, "198.51.100.20");
        spoofed.Headers.Add("X-Forwarded-For", "203.0.113.77");

        var response = await client.SendAsync(spoofed, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not found", (await ReadJsonAsync(response)).GetProperty("error").GetString());

        var counters = (MemoryRateLimitCounterStore)Host.Services.GetRequiredService<IRateLimitCounterStore>();
        Assert.Equal(1, counters.GetCount("rl:1m:198.51.100.20"));
        Assert.Null(counters.GetCount("rl:1m:203.0.113.77"));
        Assert.Equal(0, await Host.WithDatabaseAsync(db => db.SearchHistories.CountAsync(s => s.PackageId == "does-not-exist-on-the-stub")));
    }
}
