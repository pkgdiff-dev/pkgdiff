using System.Net;
using System.Net.Sockets;

namespace PkgDiff.Infrastructure.Security;

public static class AddressPolicy
{
    private static readonly IPNetwork[] BlockedNetworks = new[]
    {
        "0.0.0.0/8",
        "10.0.0.0/8",
        "100.64.0.0/10",
        "127.0.0.0/8",
        "169.254.0.0/16",
        "172.16.0.0/12",
        "192.0.0.0/24",
        "192.0.2.0/24",
        "192.168.0.0/16",
        "198.18.0.0/15",
        "198.51.100.0/24",
        "203.0.113.0/24",
        "224.0.0.0/4",
        "240.0.0.0/4",
        "::/128",
        "::1/128",
        "fc00::/7",
        "fe80::/10",
        "ff00::/8",
        "2001:db8::/32",
        "fec0::/10",
        "2001::/32",
        "64:ff9b:1::/48",
    }.Select(IPNetwork.Parse).ToArray();

    private static readonly IPNetwork Nat64Network = IPNetwork.Parse("64:ff9b::/96");
    private static readonly IPNetwork Ipv4CompatibleNetwork = IPNetwork.Parse("::/96");
    private static readonly IPNetwork SixToFourNetwork = IPNetwork.Parse("2002::/16");

    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // Embedded IPv4 (mapped, NAT64, IPv4-compatible or 6to4) must be judged as the IPv4 address it actually reaches.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (Nat64Network.Contains(address) || Ipv4CompatibleNetwork.Contains(address))
            {
                address = new IPAddress(address.GetAddressBytes()[12..16]);
            }
            else if (SixToFourNetwork.Contains(address))
            {
                address = new IPAddress(address.GetAddressBytes()[2..6]);
            }
        }

        foreach (var network in BlockedNetworks)
        {
            if (network.BaseAddress.AddressFamily == address.AddressFamily && network.Contains(address))
            {
                return false;
            }
        }

        return true;
    }
}
