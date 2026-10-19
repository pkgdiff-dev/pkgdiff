using System.Net;
using PkgDiff.Infrastructure.Security;

namespace PkgDiff.Infrastructure.Services;

public static class TransientHttp
{
    // Worth another attempt: the connection failed, the server answered 408, 429 or 5xx, or the
    // client's own timeout fired. Not worth one: an answer the server gave on purpose (404, 403),
    // a target the SSRF guard blocked, a request the caller cancelled, or a bug in our own code.
    public static bool IsTransient(Exception exception)
    {
        if (exception is SsrfBlockedException || exception.InnerException is SsrfBlockedException)
            return false;

        return exception switch
        {
            HttpRequestException http => http.StatusCode is null
                || (int)http.StatusCode >= 500
                || http.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests,
            TaskCanceledException canceled => canceled.InnerException is TimeoutException,
            IOException => true,
            _ => false
        };
    }
}
