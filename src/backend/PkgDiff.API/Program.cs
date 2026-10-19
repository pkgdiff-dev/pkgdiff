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

builder.Services.AddPkgDiffAuthentication(builder.Configuration);
builder.Services.AddPkgDiffAuthorization();

builder.Services.AddHttpClient();
builder.Services.AddBusinessServices();
builder.Services.AddPkgDiffBackgroundJobs(builder.Configuration);
builder.Services.AddPkgDiffCaching(builder.Configuration);
builder.Services.AddPkgDiffDiagnostics();

builder.Services.AddPkgDiffForwardedHeaders(builder.Configuration);
builder.Services.AddPkgDiffCors();

var app = builder.Build();

// Fail fast on an invalid JWT secret before migrations, seeding or any other startup work.
_ = app.Services.GetRequiredService<IOptions<JwtSettings>>().Value;

await app.MigrateDatabaseAsync();
await app.LogGitHubStatusAsync();

// Pipeline order matters; forwarded headers first so later middleware sees the real client IP
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
    // No HTTPS redirect: TLS ends at the host nginx
    app.UseCors(HostingServiceCollectionExtensions.ProductionCorsPolicy);
    app.UseHsts();
}

app.UseDefaultFiles();
app.UseStaticFiles();

// Must run before authentication
app.UseMiddleware<RateLimitingMiddleware>();
app.UseMiddleware<IpBlockingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<MetricsAccessMiddleware>();
app.UseMetricServer();
app.UseHttpMetrics();

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

await app.ScheduleRecurringJobsAsync();

app.Run();

// Lets the integration tests start the real host
public partial class Program;
