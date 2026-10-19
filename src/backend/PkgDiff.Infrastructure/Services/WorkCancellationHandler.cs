using PkgDiff.Application.Services;

namespace PkgDiff.Infrastructure.Services;

// Added to every HTTP client: an outbound request ends when the work it belongs to is cancelled,
// whether or not the caller passed a token of its own.
public sealed class WorkCancellationHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var workToken = WorkCancellation.Token;
        if (!workToken.CanBeCanceled || workToken == cancellationToken)
            return await base.SendAsync(request, cancellationToken);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, workToken);
        return await base.SendAsync(request, linked.Token);
    }
}
