using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PkgDiff.Application.Interfaces;
using PkgDiff.Infrastructure.Data;
using PkgDiff.Shared.DTOs;
using PkgDiff.Tests.Postgres;

namespace PkgDiff.Tests.Integration;

// The real application (Program.cs: every registration, the whole pipeline, migrations, seeding,
// the job schedule) on a PostgreSQL database of its own. Only the outside world is replaced: the
// npm registry and the job queue.
public sealed class PkgDiffHost : WebApplicationFactory<Program>
{
    public const string PeerHeader = "X-Test-Peer";
    public const string AdminPassword = "Integration-Admin-1";
    public const string ViewerPassword = "Integration-Viewer-1";
    public const string TrustedNetwork = "172.28.0.0/16";

    private readonly string _connectionString = new NpgsqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable(PostgresDatabase.ConnectionVariable))
    {
        Database = $"pkgdiff_host_{Guid.NewGuid():N}"
    }.ConnectionString;

    public StubNpmRegistry Npm { get; } = new();

    public RecordingJobScheduler Jobs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting("JwtSettings:SecretKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        builder.UseSetting("JwtSettings:Issuer", "PkgDiffTests");
        builder.UseSetting("JwtSettings:Audience", "PkgDiffTests");
        builder.UseSetting("Security:SettingsEncryptionKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("AdminSettings:AdminPassword", AdminPassword);
        builder.UseSetting("AdminSettings:ViewerPassword", ViewerPassword);
        builder.UseSetting("CacheSettings:Backend", "Memory");
        builder.UseSetting("ForwardedHeaders:TrustedNetworks", TrustedNetwork);
        // The tests sign in more often than the production sign-in tier allows one address
        builder.UseSetting("RateLimiting:Login:PerMinute", "1000");
        builder.UseSetting("RateLimiting:Login:PerFiveMinutes", "1000");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter, PeerAddressFilter>();
            services.RemoveAll<INpmService>();
            services.AddSingleton<INpmService>(Npm);
            services.RemoveAll<IJobScheduler>();
            services.AddSingleton<IJobScheduler>(Jobs);
        });
    }

    public async Task<T> WithDatabaseAsync<T>(Func<ApplicationDbContext, Task<T>> read)
    {
        using var scope = Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public override async ValueTask DisposeAsync()
    {
        // Hangfire keeps its log provider in a static. Left pointing at this host, every Hangfire object
        // built later in the test run would ask the disposed logger factory for a logger and fail.
        global::Hangfire.Logging.LogProvider.SetCurrentLogProvider(null);

        await base.DisposeAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_connectionString).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureDeletedAsync();
    }

    // TestServer has no socket, so a request has no peer address. A test names the peer it wants
    // to be in a header, and this puts it on the connection before the application's own pipeline.
    private sealed class PeerAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(PeerHeader, out var peer))
                    context.Connection.RemoteIpAddress = IPAddress.Parse(peer.ToString());
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

public sealed class StubNpmRegistry : INpmService
{
    private readonly ConcurrentDictionary<string, Dictionary<string, DateTime>> _packages = new();

    public void Publish(string packageId, params (string Version, DateTime Published)[] versions) =>
        _packages[packageId] = versions.ToDictionary(v => v.Version, v => v.Published);

    public Task<IEnumerable<string>> GetPackageVersionsAsync(string packageId, bool includePrerelease = false) =>
        Task.FromResult<IEnumerable<string>>(_packages.TryGetValue(packageId, out var versions) ? versions.Keys.ToList() : []);

    public Task<IEnumerable<string>> GetVersionsInRangeAsync(
        string packageId, string fromVersion, string toVersion, bool includePrerelease = false) =>
        throw new NotSupportedException();

    public Task<bool> PackageExistsAsync(string packageId) => Task.FromResult(_packages.ContainsKey(packageId));

    public Task<DateTime?> GetPackagePublishedDateAsync(string packageId, string version) =>
        Task.FromResult<DateTime?>(
            _packages.TryGetValue(packageId, out var versions) && versions.TryGetValue(version, out var published) ? published : null);

    public Task<PackageMetadata> GetPackageMetadataAsync(string packageId)
    {
        if (!_packages.TryGetValue(packageId, out var versions))
            throw new ArgumentException($"Package '{packageId}' not found on npm.");

        return Task.FromResult(new PackageMetadata
        {
            PackageId = packageId,
            Registry = "npm",
            LatestVersion = versions.MaxBy(v => v.Value).Key,
            Description = "stub package",
            Versions = versions.Select(v => new VersionSummary { Version = v.Key, PublishedDate = v.Value }).ToList()
        });
    }
}

public sealed class RecordingJobScheduler : IJobScheduler
{
    public ConcurrentQueue<string> Enqueued { get; } = new();

    public string Enqueue<T>(Expression<Func<T, Task>> job)
    {
        var call = (MethodCallExpression)job.Body;
        Enqueued.Enqueue($"{typeof(T).Name}.{call.Method.Name}");
        return Enqueued.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public string Schedule<T>(Expression<Func<T, Task>> job, TimeSpan delay) => Enqueue(job);
}
