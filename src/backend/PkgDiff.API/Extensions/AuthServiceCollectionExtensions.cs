using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.Options;

namespace PkgDiff.API.Extensions;

public static class AuthServiceCollectionExtensions
{
    // Nothing here may throw at registration time: EF design-time tooling builds the host without secrets.
    public static IServiceCollection AddPkgDiffAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .Validate(JwtSettings.HasMinimumLength,
                "JwtSettings:SecretKey (env JWT_SECRET) must be at least 32 bytes (UTF-8).")
            .Validate(s => !JwtSettings.IsPlaceholder(s),
                "JwtSettings:SecretKey (env JWT_SECRET) is a placeholder value; generate one with 'openssl rand -base64 48'.")
            .ValidateOnStart();

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<JwtSettings>>().Value);

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, jwt) =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.SecretKey))
                };

                // A signed token is not enough: the user must exist, be unlocked and match the security stamp
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                                     ?? context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                        var stamp = context.Principal?.FindFirstValue(AuthService.SecurityStampClaim);
                        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(stamp))
                        {
                            context.Fail("The session is no longer valid.");
                            return;
                        }

                        var services = context.HttpContext.RequestServices;
                        var cache = services.GetRequiredService<IMemoryCache>();
                        var valid = await cache.GetOrCreateAsync($"session:{userId}:{stamp}", async entry =>
                        {
                            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
                            var user = await users.FindByIdAsync(userId);
                            return user is not null
                                   && !await users.IsLockedOutAsync(user)
                                   && await users.GetSecurityStampAsync(user) == stamp;
                        });

                        if (!valid)
                            context.Fail("The session is no longer valid.");
                    }
                };
            });

        return services;
    }

    public static IServiceCollection AddPkgDiffAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.GetAllPermissions())
            {
                options.AddPolicy(permission, policy =>
                    policy.RequireAssertion(context =>
                        context.User.IsInRole("Admin") ||
                        context.User.HasClaim(c => c.Type == "Permission" && c.Value == permission)));
            }
        });

        return services;
    }
}
