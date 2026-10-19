using System.Net;
using System.Net.Sockets;

namespace PkgDiff.Infrastructure.Security;

public static class SsrfGuard
{
    public const int MaxRedirects = 5;
    public const int MaxResponseBytes = 2 * 1024 * 1024;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    public static SocketsHttpHandler CreateHandler()
    {
        return CreateHandler(AddressPolicy.IsPublic, port => port is 80 or 443);
    }

    public static SocketsHttpHandler CreateHandler(
        Func<IPAddress, bool> isAllowedAddress,
        Func<int, bool> isAllowedPort,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        ArgumentNullException.ThrowIfNull(isAllowedAddress);
        ArgumentNullException.ThrowIfNull(isAllowedPort);
        var resolveHost = resolve ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));

        return new SocketsHttpHandler
        {
            // A proxy would make ConnectCallback connect to the proxy instead of the validated target.
            UseProxy = false,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = MaxRedirects,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AutomaticDecompression = DecompressionMethods.All,
            // Every new host (including redirect targets) re-enters this callback, and the socket
            // connects only to the addresses validated here, so DNS rebinding cannot swap the target.
            ConnectCallback = async (context, cancellationToken) =>
            {
                var endPoint = context.DnsEndPoint;
                if (!isAllowedPort(endPoint.Port))
                {
                    throw new SsrfBlockedException(
                        $"Outbound connection to {endPoint.Host}:{endPoint.Port} blocked: port not allowed.");
                }

                var addresses = await resolveHost(endPoint.Host, cancellationToken).ConfigureAwait(false);
                if (addresses.Length == 0 || !addresses.All(isAllowedAddress))
                {
                    throw new SsrfBlockedException(
                        $"Outbound connection to {endPoint.Host}:{endPoint.Port} blocked: non-public destination.");
                }

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(addresses, endPoint.Port, cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
    }
}
