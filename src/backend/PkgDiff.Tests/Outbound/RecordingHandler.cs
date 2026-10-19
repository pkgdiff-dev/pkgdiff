using System.Collections.Concurrent;
using System.Net;

namespace PkgDiff.Tests.Outbound;

public sealed class RecordingHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Uri> _requests = new();

    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.NotFound;

    public string Body { get; set; } = string.Empty;

    public IReadOnlyList<Uri> Requests => _requests.ToArray();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri != null)
        {
            _requests.Enqueue(request.RequestUri);
        }

        return Task.FromResult(new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(Body),
            RequestMessage = request
        });
    }
}
