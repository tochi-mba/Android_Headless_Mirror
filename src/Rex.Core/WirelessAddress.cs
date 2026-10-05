using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Rex.Core;

/// <summary>
/// An address typed in for a phone on Wi-Fi: an address on this PC's own network (10.x, 172.16-31.x,
/// 192.168.x, or a link-local 169.254.x), with a port or without. Anything that would send ADB
/// across the internet is refused, in words, rather than tried.
/// </summary>
public static class WirelessAddress
{
    /// <summary>The address as ADB takes it ("192.168.1.20:5555"), or null with the reason it cannot be used.</summary>
    public static (string? Address, string? Why) Read(string? text, int defaultPort)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return (null, "Type the phone's address, like 192.168.1.20.");
        }

        var host = trimmed;
        var port = defaultPort;
        var colon = trimmed.LastIndexOf(':');
        if (colon >= 0)
        {
            host = trimmed[..colon];
            if (!int.TryParse(trimmed[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)
            {
                return (null, "The port is a number from 1 to 65535.");
            }
        }

        if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != AddressFamily.InterNetwork || host.Count(c => c == '.') != 3)
        {
            return (null, "Use the phone's IPv4 address, like 192.168.1.20. Wireless debugging on the phone shows it.");
        }

        return IsOnThisNetwork(address)
            ? ($"{address}:{port.ToString(CultureInfo.InvariantCulture)}", null)
            : (null, "That address is not on a home or office network. Only addresses like 192.168.x.x, 10.x.x.x or 172.16-31.x.x are used.");
    }

    private static bool IsOnThisNetwork(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 10
            || b[0] == 172 && b[1] is >= 16 and <= 31
            || b[0] == 192 && b[1] == 168
            || b[0] == 169 && b[1] == 254;
    }
}
