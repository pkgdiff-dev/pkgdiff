using System.Net;
using PkgDiff.Infrastructure.Security;

namespace PkgDiff.Infrastructure.Services;

public static class TransientHttp
{
    // Retry connection failures, 408/429/5xx and client timeouts; not deliberate answers,
    // SSRF-blocked targets, caller cancellation or bugs in our own code
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
