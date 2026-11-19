using System.Net;
using System.Net.Sockets;

namespace WatchThisForMe.Infrastructure.Sources;

// SSRF protection: a watch's address is chosen by a user (or by the AI during an investigation),
// but the fetch runs on OUR server. Without this check, "watch http://localhost:5003/api/watches"
// makes the server read its own private data, or the cloud's internal metadata service.
// Only addresses on the public internet may be fetched.
public static class PublicAddressPolicy
{
    // Azure's internal "wire server" (platform DNS, health probes). Public-looking, but internal.
    private static readonly IPAddress AzureWireServer = IPAddress.Parse("168.63.129.16");

    public static bool IsPublic(IPAddress address)
    {
        // "::ffff:127.0.0.1" is 127.0.0.1 wearing an IPv6 costume; judge the real address.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicIPv4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPublicIPv6(address),
            _ => false
        };
    }

    private static bool IsPublicIPv4(byte[] b) => !(
        b[0] == 0                                     // 0.0.0.0/8 "this network"
        || b[0] == 10                                 // 10.0.0.0/8 private
        || b[0] == 127                                // 127.0.0.0/8 this machine
        || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // 100.64.0.0/10 carrier-grade NAT
        || (b[0] == 169 && b[1] == 254)               // 169.254.0.0/16 link-local, cloud metadata
        || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)  // 172.16.0.0/12 private
        || (b[0] == 192 && b[1] == 168)               // 192.168.0.0/16 private (home/office)
        || (b[0] == 192 && b[1] == 0 && b[2] == 0)    // 192.0.0.0/24 protocol assignments
        || (b[0] == 198 && (b[1] == 18 || b[1] == 19)) // 198.18.0.0/15 benchmarking
        || b[0] >= 224                                // multicast, reserved, broadcast
        || new IPAddress(b).Equals(AzureWireServer));

    private static bool IsPublicIPv6(IPAddress address)
    {
        if (address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6Loopback)
            || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal
            || address.IsIPv6Multicast)
        {
            return false;
        }

        // Addresses that carry an IPv4 address inside (NAT64 64:ff9b::/96, 6to4 2002::/16):
        // judge the IPv4 address they lead to.
        var b = address.GetAddressBytes();
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b)
        {
            return IsPublicIPv4(b[12..16]);
        }
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsPublicIPv4(b[2..6]);
        }

        return true;
    }
}

// Thrown when a fetch would reach a non-public address. Never retried: it will never succeed.
public class BlockedAddressException(string message) : Exception(message)
{
    public const string FriendlyMessage =
        "This address points to a private or internal network, which can't be watched. Use a public web address.";
}
