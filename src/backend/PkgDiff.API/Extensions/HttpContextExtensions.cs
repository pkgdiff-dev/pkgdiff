namespace PkgDiff.API.Extensions;

public static class HttpContextExtensions
{
    // RemoteIpAddress is already rewritten by UseForwardedHeaders for trusted proxies only,
    // so request headers are never consulted here.
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
