using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PkgDiff.API.Extensions;
using PkgDiff.API.Filters;
using PkgDiff.API.Hubs;
using PkgDiff.API.Middleware;
using PkgDiff.API.Services;
using PkgDiff.Shared.Options;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddPkgDiffSwagger();

builder.Services.AddPkgDiffPersistence(builder.Configuration);

// JWT authentication (validated JwtSettings) and permission policies
builder.Services.AddPkgDiffAuthentication(builder.Configuration);
builder.Services.AddPkgDiffAuthorization();

builder.Services.AddHttpClient();
builder.Services.AddBusinessServices();
builder.Services.AddPkgDiffBackgroundJobs(builder.Configuration);
builder.Services.AddPkgDiffCaching(builder.Configuration);
builder.Services.AddPkgDiffDiagnostics();

// Forwarded headers (trusted proxy networks from ForwardedHeaders:TrustedNetworks)
builder.Services.AddPkgDiffForwardedHeaders(builder.Configuration);
builder.Services.AddPkgDiffCors();

var app = builder.Build();

// Fail fast on an invalid JWT secret before migrations, seeding or any other startup work.
_ = app.Services.GetRequiredService<IOptions<JwtSettings>>().Value;

await app.MigrateDatabaseAsync();
await app.LogGitHubStatusAsync();

// The HTTP request pipeline; the order below matters.
// Must run first so every later middleware sees the real client IP and scheme.
app.UseForwardedHeaders();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseCors(HostingServiceCollectionExtensions.DevelopmentCorsPolicy);
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseHttpsRedirection();
}
else
{
    // No HTTPS redirect here: TLS ends at the host nginx, which redirects HTTP itself; the container has no HTTPS port
    app.UseCors(HostingServiceCollectionExtensions.ProductionCorsPolicy);
    app.UseHsts();
}

app.UseDefaultFiles();
app.UseStaticFiles();

// Security middleware (must be before authentication)
app.UseMiddleware<RateLimitingMiddleware>();
app.UseMiddleware<IpBlockingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseAuthentication(); // Must be before UseAuthorization
app.UseAuthorization();

// Prometheus metrics: internal scrape (trusted network, not the bridge gateway) or Admin only
app.UseMiddleware<MetricsAccessMiddleware>();
app.UseMetricServer(); // Exposes /metrics
app.UseHttpMetrics();  // Captures HTTP metrics

// Hangfire dashboard, active Admins only: /hangfire?ticket= becomes a scoped cookie, re-checked on every request
app.UseMiddleware<HangfireTicketMiddleware>();
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = Array.Empty<IDashboardAuthorizationFilter>(),
    AsyncAuthorization = new[] { new HangfireAuthorizationFilter() },
    DashboardTitle = "PkgDiff Background Jobs"
});

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(HealthResponses.ReadyTag),
    ResponseWriter = HealthResponses.WriteAsync
});

app.MapControllers();
app.MapHub<AnalysisHub>("/analysisHub");
app.MapFallbackToFile("index.html");

await app.SeedDatabaseAsync();

// After all middleware and initialization
await app.ScheduleRecurringJobsAsync();

app.Run();

// Lets the integration tests start the real host
public partial class Program;
