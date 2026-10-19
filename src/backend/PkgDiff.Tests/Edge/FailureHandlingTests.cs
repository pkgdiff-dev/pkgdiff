using System.Net;
using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.API.Middleware;
using PkgDiff.API.Services;
using PkgDiff.Application.Jobs;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Data;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Edge;

public class FailureHandlingTests
{
    private static async Task<(HttpStatusCode Status, JsonElement Body)> GetFromThrowingAppAsync(Exception exception)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        await using var app = builder.Build();
        app.UseExceptionHandler();
        app.Run(_ => throw exception);
        await app.StartAsync(TestContext.Current.CancellationToken);

        var response = await app.GetTestClient().GetAsync("/api/package/analyze", TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, JsonDocument.Parse(json).RootElement.Clone());
    }

    [Fact]
    public async Task An_uncaught_exception_becomes_a_500_that_says_nothing_about_its_cause()
    {
        var (status, body) = await GetFromThrowingAppAsync(new InvalidOperationException("connection string Host=db;Password=hunter2"));

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", body.GetProperty("error").GetString());
        Assert.Equal("/api/package/analyze", body.GetProperty("instance").GetString());
        Assert.DoesNotContain("hunter2", body.GetRawText());
        Assert.DoesNotContain("InvalidOperationException", body.GetRawText());
    }

    [Fact]
    public async Task A_caller_fault_keeps_its_message_and_gets_a_400()
    {
        var (status, body) = await GetFromThrowingAppAsync(new ArgumentException("Invalid version format: abc"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Invalid version format: abc", body.GetProperty("error").GetString());
        Assert.Equal("Invalid version format: abc", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Missing_and_forbidden_map_to_their_status_codes()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await GetFromThrowingAppAsync(new KeyNotFoundException("row 7"))).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetFromThrowingAppAsync(new UnauthorizedAccessException("no"))).Status);
    }

    [Fact]
    public async Task A_refresh_dispatch_that_fails_fails_the_job()
    {
        var unitOfWork = new FakeUnitOfWork();
        // Recently viewed: the dispatcher refreshes only packages looked at in the last 30 days
        await unitOfWork.CachedPackages.AddAsync(new CachedPackage { PackageId = "left-pad", Registry = new Registry { Value = "npm" }, LastViewedAt = DateTime.UtcNow });
        var job = new PackageAnalysisJob(unitOfWork, NullLogger<PackageAnalysisJob>.Instance, new HangfireJobScheduler(new FailingJobClient()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync());
    }

    [Fact]
    public async Task Readiness_reports_an_unreachable_database_and_tolerates_a_missing_redis()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Timeout=1;Command Timeout=1")
            .Options;
        await using var context = new ApplicationDbContext(options);

        var database = await new DatabaseHealthCheck(context)
            .CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        var redis = await new RedisHealthCheck(new ServiceCollection().BuildServiceProvider())
            .CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, database.Status);
        Assert.Equal(HealthStatus.Healthy, redis.Status);
    }

    [Fact]
    public async Task The_readiness_body_names_each_check_and_carries_no_exception_text()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["database"] = new(HealthStatus.Unhealthy, "The database is not reachable.", TimeSpan.Zero,
                    new InvalidOperationException("Password=hunter2"), null),
                ["redis"] = new(HealthStatus.Healthy, null, TimeSpan.Zero, null, null)
            },
            TimeSpan.Zero);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await HealthResponses.WriteAsync(context, report);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Equal("""{"status":"unhealthy","checks":{"database":"unhealthy","redis":"healthy"}}""", body);
    }

    private sealed class FailingJobClient : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => throw new InvalidOperationException("the job storage is down");

        public bool ChangeState(string jobId, IState state, string expectedState) => throw new NotSupportedException();
    }
}
