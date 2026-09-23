using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;

namespace Rex.Core;

/// <summary>One USB function that speaks the ADB protocol, as Windows has it registered.</summary>
public sealed record AdbInterface(string InstanceId, string Description, string Driver, bool Present, bool Registered)
{
    /// <summary>
    /// Plugged in, but invisible to adb: Windows bound its generic WinUSB driver without the
    /// Android ADB interface GUID that adb enumerates by. Happens when a phone re-enumerates its
    /// USB functions in a new order (a different USB mode, a new port) and Windows installs a
    /// fresh binding for the new interface number.
    /// </summary>
    public bool Unreachable => Present && !Registered;
}

/// <summary>Finds ADB interfaces in the Windows device registry and can register them for adb.</summary>
public static class UsbAdbInterfaces
{
    public const string AdbInterfaceGuid = "{F72FE0D4-CBCB-407D-8814-9ED673D0DD6B}";

    /// <summary>USB class ff / subclass 42 / protocol 01 is the ADB function on every Android device.</summary>
    private const string AdbCompatibleId = "Class_ff&SubClass_42&Prot_01";
    private const string EnumUsb = @"SYSTEM\CurrentControlSet\Enum\USB";

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Every ADB interface Windows has ever registered, present or not. Never throws.</summary>
    public static IReadOnlyList<AdbInterface> Scan()
    {
        var result = new List<AdbInterface>();
        try
        {
            using var usb = Registry.LocalMachine.OpenSubKey(EnumUsb);
            if (usb is null)
            {
                return result;
            }

            foreach (var deviceName in usb.GetSubKeyNames())
            {
                using var device = usb.OpenSubKey(deviceName);
                if (device is null)
                {
                    continue;
                }

                foreach (var instanceName in device.GetSubKeyNames())
                {
                    using var instance = device.OpenSubKey(instanceName);
                    if (instance?.GetValue("CompatibleIDs") is not string[] compatible || !IsAdbFunction(compatible))
                    {
                        continue;
                    }

                    var id = $@"USB\{deviceName}\{instanceName}";
                    using var parameters = instance.OpenSubKey("Device Parameters");
                    result.Add(new AdbInterface(
                        id,
                        DescriptionOf(instance),
                        instance.GetValue("Service") as string ?? string.Empty,
                        IsPresent(id),
                        parameters is not null && HasAdbGuid(ReadGuids(parameters))));
                }
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            // The scan is advisory; a registry the user cannot read simply yields nothing.
        }

        return result;
    }

    public static IReadOnlyList<AdbInterface> Unreachable() => Scan().Where(x => x.Unreachable).ToArray();

    public static bool IsAdbFunction(IEnumerable<string> compatibleIds) =>
        compatibleIds.Any(id => id.Contains(AdbCompatibleId, StringComparison.OrdinalIgnoreCase));

    public static bool HasAdbGuid(IEnumerable<string> guids) =>
        guids.Any(guid => guid.Trim().Equals(AdbInterfaceGuid, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Registers the ADB interface GUID on every unreachable interface and restarts it so adb
    /// sees the phone. Needs administrator rights (the registry keys live under HKLM).
    /// </summary>
    public static async Task<IReadOnlyList<string>> RepairAsync(IProcessRunner runner, CancellationToken cancellationToken = default)
    {
        var repaired = new List<string>();
        foreach (var adbInterface in Unreachable())
        {
            var relative = adbInterface.InstanceId["USB\\".Length..];
            using (var parameters = Registry.LocalMachine.CreateSubKey($@"{EnumUsb}\{relative}\Device Parameters", writable: true))
            {
                var guids = ReadGuids(parameters).Where(g => g.Length > 0).Append(AdbInterfaceGuid).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                parameters.SetValue("DeviceInterfaceGUIDs", guids, RegistryValueKind.MultiString);
            }

            var restart = await runner.RunAsync(Path.Combine(Environment.SystemDirectory, "pnputil.exe"), ["/restart-device", adbInterface.InstanceId], TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            if (!restart.Ok)
            {
                throw new InvalidOperationException($"Windows could not restart {adbInterface.InstanceId}: {restart.FailureText}");
            }

            repaired.Add(adbInterface.InstanceId);
        }

        return repaired;
    }

    private static IEnumerable<string> ReadGuids(RegistryKey parameters)
    {
        var many = parameters.GetValue("DeviceInterfaceGUIDs") switch
        {
            string[] values => values,
            string value => [value],
            _ => [],
        };
        return parameters.GetValue("DeviceInterfaceGUID") is string single ? many.Append(single) : many;
    }

    /// <summary>"@winusb.inf,%usb\ms_comp_winusb.devicedesc%;WinUsb Device" reads as "WinUsb Device".</summary>
    private static string DescriptionOf(RegistryKey instance)
    {
        var text = instance.GetValue("FriendlyName") as string ?? instance.GetValue("DeviceDesc") as string ?? string.Empty;
        var separator = text.LastIndexOf(';');
        return separator >= 0 ? text[(separator + 1)..] : text;
    }

    /// <summary>CM_LOCATE_DEVNODE_NORMAL only finds devices that are currently attached.</summary>
    private static bool IsPresent(string instanceId) => CM_Locate_DevNodeW(out _, instanceId, 0) == 0;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint deviceInstance, string deviceInstanceId, uint flags);
}
