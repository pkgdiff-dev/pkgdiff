namespace PkgDiff.API.Extensions;

public static class HttpContextExtensions
{
    // Already rewritten by UseForwardedHeaders for trusted proxies; headers are never read here
    public static string GetClientIpAddress(this HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip is null)
        {
            return "0.0.0.0";
        }

        return (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }
}
