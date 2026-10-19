using Microsoft.AspNetCore.HttpOverrides;
using PkgDiff.API.Options;

namespace PkgDiff.API.Extensions;

public static class ForwardedHeadersExtensions
{
    public static IServiceCollection AddPkgDiffForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Parse eagerly so a malformed CIDR fails at startup instead of on the first request.
        var trustedNetworks = TrustedNetworks.Parse(configuration["ForwardedHeaders:TrustedNetworks"]);
        services.AddSingleton(trustedNetworks);

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;

            // The framework defaults (127.0.0.0/8 and ::1) stay in place for local development.
            foreach (var network in trustedNetworks.Networks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });

        return services;
    }
}
