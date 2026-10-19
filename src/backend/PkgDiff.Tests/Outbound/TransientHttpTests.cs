using System.Net;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.Tests.Outbound;

public class TransientHttpTests
{
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public void A_server_side_failure_is_retried(HttpStatusCode status)
    {
        Assert.True(TransientHttp.IsTransient(new HttpRequestException("failed", null, status)));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Gone)]
    public void An_answer_the_server_gave_on_purpose_is_not_retried(HttpStatusCode status)
    {
        Assert.False(TransientHttp.IsTransient(new HttpRequestException("failed", null, status)));
    }

    [Fact]
    public void A_failed_connection_and_the_clients_own_timeout_are_retried()
    {
        Assert.True(TransientHttp.IsTransient(new HttpRequestException("connection refused")));
        Assert.True(TransientHttp.IsTransient(new IOException("connection reset")));
        Assert.True(TransientHttp.IsTransient(new TaskCanceledException("timed out", new TimeoutException())));
    }

    [Fact]
    public void A_cancelled_request_a_blocked_target_and_a_bug_are_not_retried()
    {
        Assert.False(TransientHttp.IsTransient(new TaskCanceledException("the caller gave up")));
        Assert.False(TransientHttp.IsTransient(new OperationCanceledException()));
        Assert.False(TransientHttp.IsTransient(new SsrfBlockedException("blocked")));
        Assert.False(TransientHttp.IsTransient(new HttpRequestException("blocked", new SsrfBlockedException("blocked"))));
        Assert.False(TransientHttp.IsTransient(new NullReferenceException()));
        Assert.False(TransientHttp.IsTransient(new InvalidOperationException("parse failed")));
    }
}
