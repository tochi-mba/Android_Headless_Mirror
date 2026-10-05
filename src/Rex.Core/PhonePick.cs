using System.Text.RegularExpressions;

namespace Rex.Core;

/// <summary>
/// Which phone goes beside the main one. A phone on USB and on Wi-Fi at once is listed by ADB
/// twice, under two serials; it is still one phone, and it is never shown beside itself. Its
/// hardware serial decides: known from <c>ro.serialno</c> once read, and before that taken from
/// the ADB serial itself (a USB serial is the hardware serial; a Wi-Fi pairing name carries it).
/// </summary>
public static partial class PhonePick
{
    /// <summary>
    /// The phone to show beside <paramref name="main"/>, or null: a ready phone that is not the
    /// main one under another name, nor one turned down for now. The remembered one comes first,
    /// then a phone on USB, then the first listed; of one phone on two transports, USB wins.
    /// </summary>
    public static AdbDevice? Other(
        IReadOnlyList<AdbDevice> devices,
        AdbDevice main,
        IReadOnlyDictionary<string, string> hardware,
        string? preferred,
        IReadOnlySet<string> declined)
    {
        var mainHardware = HardwareOf(main, hardware);
        var candidates = devices
            .Where(d => d.IsReady && d.Serial != main.Serial && !declined.Contains(d.Serial))
            .Where(d => !SameHardware(HardwareOf(d, hardware), mainHardware))
            // One phone on two transports is one candidate: its USB serial, when it has one.
            .GroupBy(d => HardwareOf(d, hardware) is { Length: > 0 } known ? known : d.Serial, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(d => d.IsTcp).First())
            .ToArray();

        return candidates.FirstOrDefault(d => d.Serial == preferred)
            ?? candidates.FirstOrDefault(d => !d.IsTcp)
            ?? candidates.FirstOrDefault();
    }

    /// <summary>A phone's hardware serial as far as it is known, or empty when it cannot be told yet.</summary>
    public static string HardwareOf(AdbDevice device, IReadOnlyDictionary<string, string> hardware)
    {
        if (hardware.TryGetValue(device.Serial, out var known) && known.Length > 0)
        {
            return known;
        }

        if (!device.IsTcp)
        {
            return device.Serial;
        }

        var paired = PairingName().Match(device.Serial);
        return paired.Success ? paired.Groups["serial"].Value : string.Empty;
    }

    private static bool SameHardware(string a, string b) =>
        a.Length > 0 && b.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Wireless debugging's own names: <c>adb-R5CR10ABCDE-Xy12zW._adb-tls-connect._tcp</c>.</summary>
    [GeneratedRegex(@"^adb-(?<serial>[A-Za-z0-9]+)-[^.]*\._adb-tls-connect\._tcp", RegexOptions.IgnoreCase)]
    private static partial Regex PairingName();
}

/// <summary>
/// Names that tell phones apart. Two phones of the same model would both read "Galaxy S21 Ultra";
/// each then carries the end of its own serial ("Galaxy S21 Ultra ·F7PP"), everywhere a phone is
/// named: the chip, the captions, the notices, <c>rex phones</c>.
/// </summary>
public static class PhoneNames
{
    public const int SerialEnd = 4;

    /// <summary>
    /// Each phone's name (by its ADB serial), with the end of its hardware serial (or, not yet
    /// known, its ADB serial) when another phone has the same name.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Distinct(IEnumerable<(string Serial, string Name, string Hardware)> phones)
    {
        var list = phones.ToArray();
        var shared = list.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return list.ToDictionary(
            p => p.Serial,
            p => shared.Contains(p.Name) ? $"{p.Name} ·{End(p.Hardware.Length > 0 ? p.Hardware : p.Serial)}" : p.Name,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The end of a serial that is a phone's own: its last characters, or for a phone known only by
    /// its Wi-Fi address, the address's last two parts ("1.15"), which is what a person can check.
    /// </summary>
    public static string End(string serial)
    {
        var host = serial.Contains(':', StringComparison.Ordinal) ? serial[..serial.LastIndexOf(':')] : serial;
        if (System.Net.IPAddress.TryParse(host, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var parts = host.Split('.');
            return parts[^2] + "." + parts[^1];
        }

        return host.Length <= SerialEnd ? host : host[^SerialEnd..];
    }
}
