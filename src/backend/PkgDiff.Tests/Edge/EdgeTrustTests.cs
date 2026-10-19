using System.Net;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PkgDiff.API.Extensions;
using PkgDiff.API.Middleware;
using PkgDiff.API.Options;
using PkgDiff.API.Services;
using PkgDiff.Domain.Entities;
using PkgDiff.Tests.TestHost;

namespace PkgDiff.Tests.Edge;

public class EdgeTrustTests
{
    private static async Task<WebApplication> StartClientIpHostAsync(
        IDictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        builder.Services.AddPkgDiffForwardedHeaders(builder.Configuration);

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.Run(ctx => ctx.Response.WriteAsync(ctx.GetClientIpAddress() + "|" + ctx.Request.Scheme));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task<HttpContext> SendRequestAsync(
        WebApplication app,
        string path,
        Action<HttpContext> configure)
    {
        return await app.GetTestServer().SendAsync(ctx =>
        {
            ctx.Request.Path = path;
            configure(ctx);
            // TestServer treats a null peer as a trusted proxy, so a missing peer would let spoofing tests pass for the wrong reason.
            Assert.NotNull(ctx.Connection.RemoteIpAddress);
        }, TestContext.Current.CancellationToken);
    }

    private static async Task<string> SendAsync(WebApplication app, Action<HttpContext> configure)
    {
        var context = await SendRequestAsync(app, "/", configure);
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UntrustedPeer_SpoofedForwardedFor_IsIgnored()
    {
        await using var app = await StartClientIpHostAsync();

        var result = await SendAsync(app, ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("8.8.8.8");
            ctx.Request.Headers["X-Forwarded-For"] = "1.2.3.4";
        });

        Assert.Equal("8.8.8.8|http", result);
    }

    [Fact]
    public async Task UntrustedPeer_SpoofedRealIpHeader_IsIgnored()
    {
        await using var app = await StartClientIpHostAsync();

        var result = await SendAsync(app, ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("8.8.8.8");
            ctx.Request.Headers["X-Real-IP"] = "1.2.3.4";
        });

        Assert.Equal("8.8.8.8|http", result);
    }

    [Fact]
    public async Task TrustedProxy_UsesRightMostForwardedForEntry_AndForwardedProto()
    {
        await using var app = await StartClientIpHostAsync();

        var result = await SendAsync(app, ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("172.28.0.5");
            ctx.Request.Headers["X-Forwarded-For"] = "9.9.9.9, 1.2.3.4";
            ctx.Request.Headers["X-Forwarded-Proto"] = "https";
        });

        Assert.Equal("1.2.3.4|https", result);
    }

    [Fact]
    public async Task TrustedIpv4MappedPeer_IsMatchedAgainstIpv4Network()
    {
        await using var app = await StartClientIpHostAsync();

        var result = await SendAsync(app, ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:172.28.0.5");
            ctx.Request.Headers["X-Forwarded-For"] = "1.2.3.4";
        });

        Assert.Equal("1.2.3.4|http", result);
    }

    [Fact]
    public async Task TrustedPeer_WithoutForwardedFor_KeepsPeerAddress()
    {
        await using var app = await StartClientIpHostAsync();

        var result = await SendAsync(app, ctx =>
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("172.28.0.5"));

        Assert.Equal("172.28.0.5|http", result);
    }

    [Theory]
    [InlineData("172.28.0.0", "1.2.3.4")]
    [InlineData("172.28.255.255", "1.2.3.4")]
    [InlineData("172.27.255.255", "172.27.255.255")]
    [InlineData("172.29.0.0", "172.29.0.0")]
    public async Task TrustedNetworkBoundary_DecidesWhetherForwardedForIsHonored(string peer, string expectedClientIp)
    {
        await using var app = await StartClientIpHostAsync();

        var result = await SendAsync(app, ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            ctx.Request.Headers["X-Forwarded-For"] = "1.2.3.4";
        });

        Assert.Equal(expectedClientIp + "|http", result);
    }

    [Fact]
    public async Task UntrustedIpv4MappedPeer_IsNormalizedToDottedIpv4()
    {
        await using var app = await StartClientIpHostAsync();

        var result = await SendAsync(app, ctx =>
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:203.0.113.9"));

        Assert.Equal("203.0.113.9|http", result);
    }

    [Fact]
    public async Task ConfiguredTrustedNetworks_ReplaceTheDefaultSubnet()
    {
        await using var app = await StartClientIpHostAsync(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:TrustedNetworks"] = "10.10.0.0/16"
        });

        var result = await SendAsync(app, ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("172.28.0.5");
            ctx.Request.Headers["X-Forwarded-For"] = "1.2.3.4";
        });

        Assert.Equal("172.28.0.5|http", result);
    }

    [Fact]
    public void InvalidTrustedNetworkCidr_FailsAtRegistration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ForwardedHeaders:TrustedNetworks"] = "172.28.0.0/99"
            })
            .Build();

        Assert.Throws<FormatException>(() =>
            new ServiceCollection().AddPkgDiffForwardedHeaders(configuration));
    }

    [Theory]
    [InlineData("172.28.0.1", true)]
    [InlineData("::ffff:172.28.0.1", true)]
    [InlineData("172.28.0.5", false)]
    [InlineData("8.8.8.1", false)]
    public void TrustedNetworks_IdentifiesBridgeGateway(string address, bool expected)
    {
        var networks = TrustedNetworks.Parse(null);

        Assert.Equal(expected, networks.IsGateway(IPAddress.Parse(address)));
    }

    private static async Task<WebApplication> StartRateLimitHostAsync(IDictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        builder.Services.Configure<InboundRateLimitOptions>(
            builder.Configuration.GetSection(InboundRateLimitOptions.SectionName));
        builder.Services.AddPkgDiffForwardedHeaders(builder.Configuration);
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<IRateLimitCounterStore, MemoryRateLimitCounterStore>();

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.UseMiddleware<RateLimitingMiddleware>();
        app.Run(ctx => ctx.Response.WriteAsync("ok"));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static int? ReadCounter(WebApplication app, string key) =>
        ((MemoryRateLimitCounterStore)app.Services.GetRequiredService<IRateLimitCounterStore>()).GetCount(key);

    [Fact]
    public async Task RateLimiting_TwoVisitorsBehindTrustedProxy_UseDistinctCounterKeys()
    {
        await using var app = await StartRateLimitHostAsync();

        await SendRequestAsync(app, "/api/package/analyze", ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("172.28.0.7");
            ctx.Request.Headers["X-Forwarded-For"] = "1.1.1.1";
        });
        await SendRequestAsync(app, "/api/package/analyze", ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("172.28.0.7");
            ctx.Request.Headers["X-Forwarded-For"] = "2.2.2.2";
        });

        Assert.Equal(1, ReadCounter(app, "rl:1m:1.1.1.1"));
        Assert.Equal(1, ReadCounter(app, "rl:1m:2.2.2.2"));
        Assert.Null(ReadCounter(app, "rl:1m:172.28.0.7"));
    }

    [Fact]
    public async Task RateLimiting_SpoofedRealIpHeaderFromUntrustedPeer_CountsAgainstPeer()
    {
        await using var app = await StartRateLimitHostAsync();

        await SendRequestAsync(app, "/api/package/analyze", ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("8.8.8.8");
            ctx.Request.Headers["X-Real-IP"] = "1.2.3.4";
        });

        Assert.Equal(1, ReadCounter(app, "rl:1m:8.8.8.8"));
        Assert.Null(ReadCounter(app, "rl:1m:1.2.3.4"));
    }

    [Fact]
    public async Task RateLimiting_AdminRoutes_HaveTheirOwnTierAndDoNotUseUpTheGeneralOne()
    {
        await using var app = await StartRateLimitHostAsync();

        // More than the general tier allows in a minute, as the admin panel and the Hangfire dashboard do
        for (var i = 0; i < 100; i++)
            Assert.Equal(200, await SendFromVisitorAsync(app, i % 2 == 0 ? "/api/admin/stats" : "/hangfire/stats", signedIn: true));

        Assert.Equal(100, ReadCounter(app, "rl:admin:1m:9.9.9.9"));
        Assert.Null(ReadCounter(app, "rl:1m:9.9.9.9"));
        Assert.Equal(200, await SendFromVisitorAsync(app, "/api/package/analyze"));
    }

    [Fact]
    public async Task RateLimiting_AdminTier_RejectsAboveItsLimit()
    {
        await using var app = await StartRateLimitHostAsync();

        for (var i = 0; i < 240; i++)
            Assert.Equal(200, await SendFromVisitorAsync(app, "/api/admin/stats", signedIn: true));

        Assert.Equal(429, await SendFromVisitorAsync(app, "/api/Admin/stats", signedIn: true));
        Assert.Equal(200, await SendFromVisitorAsync(app, "/api/package/analyze"));
    }

    [Fact]
    public async Task RateLimiting_TiersAreReadFromConfiguration()
    {
        await using var app = await StartRateLimitHostAsync(new Dictionary<string, string?>
        {
            ["RateLimiting:General:PerMinute"] = "2",
            ["RateLimiting:PdfExport:PerMinute"] = "1",
            ["RateLimiting:Admin:PerFiveMinutes"] = "1"
        });

        Assert.Equal(200, await SendFromVisitorAsync(app, "/api/admin/stats", signedIn: true));
        Assert.Equal(429, await SendFromVisitorAsync(app, "/api/admin/stats", signedIn: true));

        Assert.Equal(200, await SendFromVisitorAsync(app, "/api/Package/pypi/requests/export/pdf"));
        Assert.Equal(429, await SendFromVisitorAsync(app, "/api/Package/pypi/requests/export/pdf"));
        Assert.Equal(429, await SendFromVisitorAsync(app, "/api/package/analyze"));
    }

    // signedIn: the request carries a bearer token, which only gives an admin address the admin tier
    private static async Task<int> SendFromVisitorAsync(WebApplication app, string path, bool signedIn = false)
    {
        var context = await SendRequestAsync(app, path, ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("9.9.9.9");
            if (signedIn)
                ctx.Request.Headers.Authorization = "Bearer test";
        });
        return context.Response.StatusCode;
    }

    [Fact]
    public async Task RateLimiting_PdfExportAfterOrdinaryBrowsing_IsNotRejected()
    {
        await using var app = await StartRateLimitHostAsync();

        for (var i = 0; i < 20; i++)
            Assert.Equal(200, await SendFromVisitorAsync(app, "/api/package/analyze"));

        Assert.Equal(200, await SendFromVisitorAsync(app, "/api/Package/pypi/requests/export/pdf"));
        Assert.Equal(1, ReadCounter(app, "rl:pdf:1m:9.9.9.9"));
    }

    [Fact]
    public async Task RateLimiting_SixthPdfExportWithinAMinute_IsRejected()
    {
        await using var app = await StartRateLimitHostAsync();

        for (var i = 0; i < 5; i++)
            Assert.Equal(200, await SendFromVisitorAsync(app, "/api/Package/pypi/requests/export/pdf"));

        Assert.Equal(429, await SendFromVisitorAsync(app, "/api/Package/pypi/requests/export/pdf"));
        Assert.Equal(200, await SendFromVisitorAsync(app, "/api/package/analyze"));
    }

    [Fact]
    public async Task RateLimiting_PdfExportOverTheGeneralLimit_IsRejectedWithoutCountingAsExport()
    {
        await using var app = await StartRateLimitHostAsync();

        for (var i = 0; i < 60; i++)
            Assert.Equal(200, await SendFromVisitorAsync(app, "/api/package/analyze"));

        Assert.Equal(429, await SendFromVisitorAsync(app, "/api/package/analyze"));
        Assert.Equal(429, await SendFromVisitorAsync(app, "/api/Package/pypi/requests/export/pdf"));
        Assert.Null(ReadCounter(app, "rl:pdf:1m:9.9.9.9"));
    }

    private static async Task<(WebApplication App, Channel<RequestLog> Channel)> StartRequestLogHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddPkgDiffForwardedHeaders(builder.Configuration);
        var channel = Channel.CreateUnbounded<RequestLog>();
        builder.Services.AddSingleton(channel);

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.UseMiddleware<RequestLoggingMiddleware>();
        // No body write: TestServer.SendAsync returns once the response starts, which would race the
        // middleware's post-response log write. An unstarted response completes after the pipeline.
        app.Run(_ => Task.CompletedTask);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return (app, channel);
    }

    [Theory]
    [InlineData("/api/package/analyze", false)]
    [InlineData("/api/admin/security/request-logs", false)]
    [InlineData("/health", true)]
    [InlineData("/metrics", true)]
    [InlineData("/swagger/index.html", true)]
    [InlineData("/hangfire/js/app.js", true)]
    [InlineData("/hangfire", true)]
    [InlineData("/analysisHub/negotiate", true)]
    [InlineData("/favicon.ico", true)]
    [InlineData("/main.js", true)]
    [InlineData("/styles.css", true)]
    public void RequestLogging_ShouldSkip_DecidesByPathOnly(string path, bool expected)
    {
        Assert.Equal(expected, RequestLoggingMiddleware.ShouldSkip(path));
    }

    [Fact]
    public async Task RequestLogging_InternalPeerRequest_IsLoggedWithNormalizedIp()
    {
        var (app, channel) = await StartRequestLogHostAsync();
        await using var host = app;

        await SendRequestAsync(app, "/api/package/analyze", ctx =>
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:172.28.0.7"));

        Assert.True(channel.Reader.TryRead(out var log));
        Assert.Equal("172.28.0.7", log!.IpAddress);
        Assert.Equal("/api/package/analyze", log.Endpoint);
    }

    [Fact]
    public async Task RequestLogging_BotUserAgent_IsLogged()
    {
        var (app, channel) = await StartRequestLogHostAsync();
        await using var host = app;

        await SendRequestAsync(app, "/api/package/analyze", ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("8.8.8.8");
            ctx.Request.Headers.UserAgent = "Mozilla/5.0 (compatible; Googlebot/2.1)";
        });

        Assert.True(channel.Reader.TryRead(out var log));
        Assert.Equal("8.8.8.8", log!.IpAddress);
    }

    private static async Task<WebApplication> StartMetricsHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddPkgDiffForwardedHeaders(builder.Configuration);
        TestAuthHandler.Register(builder.Services);
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<MetricsAccessMiddleware>();
        app.Run(ctx => ctx.Response.WriteAsync("ok"));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    [Fact]
    public async Task Metrics_InternalScrapeFromContainerPeer_IsAllowed()
    {
        await using var app = await StartMetricsHostAsync();

        var context = await SendRequestAsync(app, "/metrics", ctx =>
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("172.28.0.9"));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Metrics_BridgeGatewayPeer_IsTreatedAsExternal()
    {
        await using var app = await StartMetricsHostAsync();

        var context = await SendRequestAsync(app, "/metrics", ctx =>
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("172.28.0.1"));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Metrics_ForwardedVisitorThroughTrustedProxy_IsRejectedWithoutAuth()
    {
        await using var app = await StartMetricsHostAsync();

        var context = await SendRequestAsync(app, "/metrics", ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("172.28.0.5");
            ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
        });

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(null, StatusCodes.Status401Unauthorized)]
    [InlineData("Viewer", StatusCodes.Status403Forbidden)]
    [InlineData("Admin", StatusCodes.Status200OK)]
    public async Task Metrics_ExternalPeer_RequiresAdmin(string? role, int expectedStatus)
    {
        await using var app = await StartMetricsHostAsync();

        var context = await SendRequestAsync(app, "/metrics", ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("8.8.8.8");
            if (role is not null)
            {
                ctx.Request.Headers[TestAuthHandler.UserHeader] = "user-1";
                ctx.Request.Headers[TestAuthHandler.RolesHeader] = role;
            }
        });

        Assert.Equal(expectedStatus, context.Response.StatusCode);
    }

    [Fact]
    public async Task Metrics_GateDoesNotAffectOtherPaths()
    {
        await using var app = await StartMetricsHostAsync();

        var context = await SendRequestAsync(app, "/api/anything", ctx =>
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("8.8.8.8"));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task RequestLogging_SignalRHubNegotiate_IsNotLogged()
    {
        var (app, channel) = await StartRequestLogHostAsync();
        await using var host = app;

        await SendRequestAsync(app, "/analysisHub/negotiate", ctx =>
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("8.8.8.8"));

        Assert.False(channel.Reader.TryRead(out _));
    }
}
