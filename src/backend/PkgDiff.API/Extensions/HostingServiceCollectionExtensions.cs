using System.Threading.Channels;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PkgDiff.API.Filters;
using PkgDiff.API.Jobs;
using PkgDiff.API.Middleware;
using PkgDiff.API.Options;
using PkgDiff.API.Services;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Jobs;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Infrastructure.Data;
using PkgDiff.Infrastructure.Repositories;
using StackExchange.Redis;

namespace PkgDiff.API.Extensions;

public static class HostingServiceCollectionExtensions
{
    public const string DevelopmentCorsPolicy = "AllowAngularDev";
    public const string ProductionCorsPolicy = "AllowProduction";

    public static IServiceCollection AddPkgDiffSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new() { Title = "PkgDiff API", Version = "v1" });

            options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Description = "Enter your JWT token. Example: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
                Name = "Authorization",
                In = Microsoft.OpenApi.ParameterLocation.Header,
                Type = Microsoft.OpenApi.SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            options.AddSecurityRequirement(document => new Microsoft.OpenApi.OpenApiSecurityRequirement
            {
                [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        });

        return services;
    }

    public static IServiceCollection AddPkgDiffPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly("PkgDiff.Infrastructure")));

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;

                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        // Kept in Postgres to survive recreation; encrypted so a database dump alone cannot forge tokens
        services.AddDataProtection()
            .SetApplicationName("pkgdiff")
            .PersistKeysToDbContext<ApplicationDbContext>()
            .ProtectKeysWithSettingsEncryptionKey();

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }

    public static IServiceCollection AddPkgDiffBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddHangfire((provider, config) => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseFilter(new JobTriggerFilter(provider.GetRequiredService<IHttpContextAccessor>()))
            .UsePostgreSqlStorage(options =>
                options.UseNpgsqlConnection(configuration.GetConnectionString("DefaultConnection"))));

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = 10;
        });

        // Dependency audits get two dedicated workers
        services.AddHangfireServer(options =>
        {
            options.ServerName = $"{Environment.MachineName}:audits";
            options.Queues = new[] { PkgDiff.Application.Interfaces.IDependencyAuditService.AuditQueue };
            options.WorkerCount = 2;
        });

        services.AddScoped<IJobScheduler, HangfireJobScheduler>();
        services.AddScoped<PackageAnalysisJob>();
        services.AddScoped<PackageRefreshJob>();
        services.AddScoped<DatabaseCleanupJob>();

        services.AddSingleton<HangfireDashboardAccess>();
        services.AddScoped<IAdminRoleChecker, IdentityAdminRoleChecker>();

        return services;
    }

    public static IServiceCollection AddPkgDiffCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();

        var cacheBackend = configuration["CacheSettings:Backend"] ?? "Memory";
        var redisConnectionString = configuration["Redis:ConnectionString"];
        var useRedis = cacheBackend.Equals("Redis", StringComparison.OrdinalIgnoreCase)
                       && !string.IsNullOrWhiteSpace(redisConnectionString);

        if (useRedis)
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = configuration["Redis:InstanceName"] ?? "pkgdiff:";
            });
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString!));
            services.AddSingleton<IRateLimitCounterStore, RedisRateLimitCounterStore>();
        }
        else
        {
            services.AddDistributedMemoryCache();
            services.AddSingleton<IRateLimitCounterStore, MemoryRateLimitCounterStore>();
        }

        services.AddOptions<InboundRateLimitOptions>()
            .Bind(configuration.GetSection(InboundRateLimitOptions.SectionName))
            .Validate(options => options.IsValid(), "RateLimiting: every PerMinute and PerFiveMinutes value must be greater than zero.")
            .ValidateOnStart();

        return services;
    }

    public static IServiceCollection AddPkgDiffDiagnostics(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // /health is liveness; /health/ready also needs the database and Redis
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: new[] { HealthResponses.ReadyTag })
            .AddCheck<RedisHealthCheck>("redis", tags: new[] { HealthResponses.ReadyTag });

        // Bounded: drops the oldest pending entries instead of growing memory when the DB lags
        services.AddSingleton(Channel.CreateBounded<RequestLog>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        }));
        services.AddHostedService<RequestLogBatchService>();

        return services;
    }

    public static IServiceCollection AddPkgDiffCors(this IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(DevelopmentCorsPolicy, policy =>
            {
                policy.WithOrigins("http://localhost:4200")
                      .WithHeaders("Content-Type", "Authorization", "Accept")
                      .WithMethods("GET", "POST", "PUT", "DELETE")
                      .AllowCredentials();
            });

            options.AddPolicy(ProductionCorsPolicy, policy =>
            {
                policy.WithOrigins(
                        "https://pkgdiff.dev",
                        "https://www.pkgdiff.dev")
                    .WithHeaders("Content-Type", "Authorization", "Accept")
                    .WithMethods("GET", "POST", "PUT", "DELETE")
                    .AllowCredentials();
            });
        });

        return services;
    }
}
