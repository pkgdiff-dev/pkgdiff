using PkgDiff.Application.Services;

namespace PkgDiff.Infrastructure.Services;

// Ends every outbound request when its work is cancelled, even without a caller token
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
