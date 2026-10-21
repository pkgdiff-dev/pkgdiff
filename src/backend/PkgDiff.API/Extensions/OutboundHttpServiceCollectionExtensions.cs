using System.Net.Http;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.API.Extensions;

public static class OutboundHttpServiceCollectionExtensions
{
    private const int GitHubApiMaxResponseBytes = 16 * 1024 * 1024;
    private const int RegistryMaxResponseBytes = 64 * 1024 * 1024;
    private const int PubDevMaxResponseBytes = 8 * 1024 * 1024;
    private const int SecurityApiMaxResponseBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan FixedHostTimeout = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddPkgDiffOutboundHttp(this IServiceCollection services)
    {
        services.ConfigureHttpClientDefaults(builder => builder
            .ConfigurePrimaryHttpMessageHandler(SsrfGuard.CreateHandler)
            .AddHttpMessageHandler(() => new WorkCancellationHandler())
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SsrfGuard.DefaultTimeout;
                client.MaxResponseContentBufferSize = SsrfGuard.MaxResponseBytes;
            }));

        // Headers (User-Agent, Authorization) are set by the consuming services
        services.AddHttpClient(HttpClientNames.GitHubApi, client =>
        {
            client.Timeout = FixedHostTimeout;
            client.MaxResponseContentBufferSize = GitHubApiMaxResponseBytes;
        });

        services.AddHttpClient(HttpClientNames.ExternalHtml, client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "PkgDiff/1.0");
        });

        // Trusted registry hosts get larger caps: real packuments exceed 2 MiB
        services.AddHttpClient(HttpClientNames.NpmRegistry, client =>
        {
            client.BaseAddress = new Uri("https://registry.npmjs.org/");
            client.Timeout = FixedHostTimeout;
            client.MaxResponseContentBufferSize = RegistryMaxResponseBytes;
        });

        services.AddHttpClient(HttpClientNames.PyPIRegistry, client =>
        {
            client.BaseAddress = new Uri("https://pypi.org/pypi/");
            client.Timeout = FixedHostTimeout;
            client.MaxResponseContentBufferSize = RegistryMaxResponseBytes;
        });

        // Packagist asks API clients to name themselves with a way to reach them
        services.AddHttpClient(HttpClientNames.PackagistRepository, client =>
        {
            client.BaseAddress = new Uri("https://repo.packagist.org/");
            client.Timeout = FixedHostTimeout;
            client.MaxResponseContentBufferSize = RegistryMaxResponseBytes;
            client.DefaultRequestHeaders.Add("User-Agent", "PkgDiff/1.0 (+https://pkgdiff.dev)");
        });

        services.AddHttpClient(HttpClientNames.MavenCentral, client =>
        {
            client.BaseAddress = new Uri("https://repo1.maven.org/maven2/");
            client.Timeout = FixedHostTimeout;
            client.MaxResponseContentBufferSize = RegistryMaxResponseBytes;
            client.DefaultRequestHeaders.Add("User-Agent", "PkgDiff/1.0 (+https://pkgdiff.dev)");
        });

        // Only publish dates come from the search API, so a slow answer must not hold up the package page
        services.AddHttpClient(HttpClientNames.MavenSearch, client =>
        {
            client.BaseAddress = new Uri("https://search.maven.org/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.MaxResponseContentBufferSize = RegistryMaxResponseBytes;
            client.DefaultRequestHeaders.Add("User-Agent", "PkgDiff/1.0 (+https://pkgdiff.dev)");
        });

        services.AddHttpClient<IPubDevService, PubDevService>(HttpClientNames.PubDev, client =>
        {
            client.BaseAddress = new Uri("https://pub.dev/");
            client.Timeout = FixedHostTimeout;
            client.MaxResponseContentBufferSize = PubDevMaxResponseBytes;
        });

        services.AddScoped<IHtmlDocumentLoader, HtmlDocumentLoader>();

        // Security providers: called from background jobs only
        AddSecurityApi(services, HttpClientNames.Osv, "https://api.osv.dev/");
        AddSecurityApi(services, HttpClientNames.DepsDev, "https://api.deps.dev/");
        AddSecurityApi(services, HttpClientNames.Scorecard, "https://api.securityscorecards.dev/");
        AddSecurityApi(services, HttpClientNames.Socket, "https://api.socket.dev/");

        // The admin health page must not hang on a slow upstream
        services.AddHttpClient(HttpClientNames.HealthProbe, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        // The one intentional internal target (host.docker.internal), restricted by ufw
        services.AddHttpClient<IOllamaService, OllamaService>((provider, client) =>
            {
                var baseUrl = provider.GetRequiredService<IConfiguration>()["Ollama:BaseUrl"] ?? "http://localhost:11434";
                client.BaseAddress = new Uri(baseUrl);
                // LLM generation can be slow on CPU-only servers
                client.Timeout = TimeSpan.FromMinutes(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false });

        return services;
    }

    private static void AddSecurityApi(IServiceCollection services, string name, string baseAddress)
    {
        services.AddHttpClient(name, client =>
        {
            client.BaseAddress = new Uri(baseAddress);
            client.Timeout = FixedHostTimeout;
            client.MaxResponseContentBufferSize = SecurityApiMaxResponseBytes;
            client.DefaultRequestHeaders.Add("User-Agent", "pkgdiff/1.0 (+https://pkgdiff.dev)");
        });
    }
}
