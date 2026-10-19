using System.Net;
using System.Net.Sockets;

namespace PkgDiff.API.Options;

public sealed class TrustedNetworks
{
    public const string DefaultNetworks = "172.28.0.0/16";

    public TrustedNetworks(IEnumerable<System.Net.IPNetwork> networks)
    {
        Networks = networks.ToList().AsReadOnly();
    }

    public IReadOnlyList<System.Net.IPNetwork> Networks { get; }

    public bool Contains(IPAddress address)
    {
        var normalized = Normalize(address);
        return Networks.Any(n =>
            n.BaseAddress.AddressFamily == normalized.AddressFamily && n.Contains(normalized));
    }

    // The bridge gateway carries host-originated traffic, so it is never an internal peer
    public bool IsGateway(IPAddress address)
    {
        var normalized = Normalize(address);
        if (normalized.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        foreach (var network in Networks)
        {
            if (network.BaseAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                continue;
            }

            var bytes = network.BaseAddress.GetAddressBytes();
            var gateway = ((uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3]) + 1;
            var gatewayAddress = new IPAddress(new[]
            {
                (byte)(gateway >> 24), (byte)(gateway >> 16), (byte)(gateway >> 8), (byte)gateway
            });

            if (gatewayAddress.Equals(normalized))
            {
                return true;
            }
        }

        return false;
    }

    public static TrustedNetworks Parse(string? csv)
    {
        var raw = string.IsNullOrWhiteSpace(csv) ? DefaultNetworks : csv;
        var networks = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(System.Net.IPNetwork.Parse)
            .ToList();
        return new TrustedNetworks(networks);
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
