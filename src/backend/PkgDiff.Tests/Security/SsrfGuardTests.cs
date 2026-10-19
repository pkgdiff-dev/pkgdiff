using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PkgDiff.Infrastructure.Security;

namespace PkgDiff.Tests.Security;

public class SsrfGuardTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("100.64.0.0")]
    [InlineData("100.127.255.255")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.0")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.8")]
    [InlineData("192.0.2.1")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.0")]
    [InlineData("198.19.255.255")]
    [InlineData("198.51.100.7")]
    [InlineData("203.0.113.9")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    // WR-05: IPv4-compatible, 6to4, deprecated site-local, Teredo and local-use NAT64
    [InlineData("::7f00:1")]
    [InlineData("::a9fe:a9fe")]
    [InlineData("::a00:1")]
    [InlineData("2002:7f00:1::1")]
    [InlineData("2002:a9fe:a9fe::")]
    [InlineData("2002:c0a8:101::1")]
    [InlineData("fec0::1")]
    [InlineData("feff::1")]
    [InlineData("2001::1")]
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2")]
    [InlineData("64:ff9b:1::a00:1")]
    public void IsPublic_NonPublicAddress_ReturnsFalse(string address)
    {
        Assert.False(AddressPolicy.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.0")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.0")]
    [InlineData("198.17.255.255")]
    [InlineData("198.20.0.0")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]
    [InlineData("::808:808")]
    [InlineData("2002:808:808::1")]
    [InlineData("2001:4860:4860::8888")]
    public void IsPublic_PublicAddress_ReturnsTrue(string address)
    {
        Assert.True(AddressPolicy.IsPublic(IPAddress.Parse(address)));
    }

    private static async Task<(WebApplication App, Uri BaseUri)> StartServerAsync(
        string url,
        Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(url);
        builder.Logging.ClearProviders();
        var app = builder.Build();
        map(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return (app, new Uri(app.Urls.First()));
    }

    private static HttpClient CreateLoopbackOnlyClient(ConcurrentBag<IPAddress> seen)
    {
        var handler = SsrfGuard.CreateHandler(ip =>
        {
            seen.Add(ip);
            return ip.Equals(IPAddress.Loopback);
        }, _ => true);
        return new HttpClient(handler);
    }

    [Fact]
    public async Task CrossHostRedirect_ToDisallowedAddress_IsRevalidatedAndBlocked()
    {
        // The second host is bound to 127.0.0.2 so that, without re-validation, the redirect would succeed.
        var (target, targetUri) = await StartServerAsync(
            "http://127.0.0.2:0",
            app => app.MapGet("/", () => "internal"));
        try
        {
            var (origin, originUri) = await StartServerAsync(
                "http://127.0.0.1:0",
                app => app.MapGet("/cross", () => Results.Redirect(new Uri(targetUri, "/").ToString())));
            try
            {
                var seen = new ConcurrentBag<IPAddress>();
                using var client = CreateLoopbackOnlyClient(seen);

                var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(
                    new Uri(originUri, "/cross"), TestContext.Current.CancellationToken));

                Assert.IsType<SsrfBlockedException>(ex.InnerException);
                Assert.Contains(IPAddress.Parse("127.0.0.1"), seen);
                Assert.Contains(IPAddress.Parse("127.0.0.2"), seen);
            }
            finally
            {
                await origin.DisposeAsync();
            }
        }
        finally
        {
            await target.DisposeAsync();
        }
    }

    private static void MapHops(WebApplication app)
    {
        app.MapGet("/hop/{n:int}", (int n) => n <= 0 ? Results.Text("done") : Results.Redirect($"/hop/{n - 1}"));
    }

    [Fact]
    public async Task SameHostRedirect_IsFollowed()
    {
        var (app, baseUri) = await StartServerAsync("http://127.0.0.1:0", MapHops);
        try
        {
            using var client = CreateLoopbackOnlyClient(new ConcurrentBag<IPAddress>());

            using var response = await client.GetAsync(new Uri(baseUri, "/hop/1"), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task RedirectChain_IsNotFollowedPastFiveHops()
    {
        var (app, baseUri) = await StartServerAsync("http://127.0.0.1:0", MapHops);
        try
        {
            using var client = CreateLoopbackOnlyClient(new ConcurrentBag<IPAddress>());

            using var response = await client.GetAsync(new Uri(baseUri, "/hop/6"), TestContext.Current.CancellationToken);

            Assert.Equal(5, SsrfGuard.MaxRedirects);
            Assert.InRange((int)response.StatusCode, 300, 399);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task ResponseLargerThanCap_FailsBufferedRead()
    {
        var (app, baseUri) = await StartServerAsync(
            "http://127.0.0.1:0",
            server => server.MapGet("/big", () => Results.Text(new string('a', 3 * 1024 * 1024))));
        try
        {
            using var client = CreateLoopbackOnlyClient(new ConcurrentBag<IPAddress>());
            client.MaxResponseContentBufferSize = SsrfGuard.MaxResponseBytes;

            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetStringAsync(
                new Uri(baseUri, "/big"), TestContext.Current.CancellationToken));
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("8.8.8.8", "10.0.0.1")]
    [InlineData("10.0.0.1", "8.8.8.8")]
    public async Task MixedDnsAnswer_IsBlockedRegardlessOfOrder(string first, string second)
    {
        var answer = new[] { IPAddress.Parse(first), IPAddress.Parse(second) };
        using var client = new HttpClient(SsrfGuard.CreateHandler(
            AddressPolicy.IsPublic,
            port => port is 80 or 443,
            (_, _) => Task.FromResult(answer)));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(
            "http://mixed.example.test/", TestContext.Current.CancellationToken));

        Assert.IsType<SsrfBlockedException>(ex.InnerException);
    }

    [Fact]
    public async Task EmptyDnsAnswer_IsBlocked()
    {
        using var client = new HttpClient(SsrfGuard.CreateHandler(
            AddressPolicy.IsPublic,
            port => port is 80 or 443,
            (_, _) => Task.FromResult(Array.Empty<IPAddress>())));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(
            "http://empty.example.test/", TestContext.Current.CancellationToken));

        Assert.IsType<SsrfBlockedException>(ex.InnerException);
    }

    [Fact]
    public async Task DisallowedPort_IsBlockedBeforeResolution()
    {
        var resolved = false;
        using var client = new HttpClient(SsrfGuard.CreateHandler(
            AddressPolicy.IsPublic,
            port => port is 80 or 443,
            (_, _) =>
            {
                resolved = true;
                return Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") });
            }));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(
            "http://public.example.test:8080/", TestContext.Current.CancellationToken));

        Assert.IsType<SsrfBlockedException>(ex.InnerException);
        Assert.False(resolved);
    }
}
