using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rex.Core;

/// <summary>An attached USB device node that Windows reports a problem for, as the scan read it.</summary>
public sealed record UsbDeviceNode(string InstanceId, string Description, IReadOnlyList<string> HardwareIds, int ProblemCode);

public enum UsbProblemKind
{
    /// <summary>
    /// The hub gave up enumerating the device: "Unknown USB Device (Device Descriptor Request
    /// Failed)" and its siblings, which Windows reports as "USB device not recognised".
    /// </summary>
    FailedEnumeration,

    /// <summary>A device from a phone maker that Windows could not start or find a driver for.</summary>
    PhoneDevice,
}

/// <summary>A USB node that looks like a phone Windows could not use.</summary>
public sealed record UsbProblem(UsbDeviceNode Node, UsbProblemKind Kind, string Vendor)
{
    public string InstanceId => Node.InstanceId;

    /// <summary>
    /// Whether the auto-repair task reaches it. The task can only name devices by the generic
    /// failure ids, so a phone that enumerated under its own vendor id needs the prompted repair.
    /// </summary>
    public bool AutoRepairable => Node.HardwareIds.Any(UsbProblems.IsFailedEnumerationId);

    /// <summary>What Windows calls it and what is wrong, for the notice, the log and the CLI.</summary>
    public string Describe()
    {
        var name = Node.Description.Length > 0 ? Node.Description : Vendor.Length > 0 ? Vendor + " USB device" : "A USB device";
        return $"{name}: {UsbProblems.ProblemText(Node.ProblemCode)}";
    }
}

/// <summary>
/// Recognises a phone that Windows could not read over USB. <see cref="Classify(UsbDeviceNode)"/>
/// is pure; the scan that feeds it lives in <see cref="IUsbDeviceSource"/>.
/// </summary>
public static class UsbProblems
{
    /// <summary>
    /// The hardware ids Windows' usb.inf gives a device the hub could not enumerate, in usb.inf's
    /// order. The instance id of such a node reads USB\VID_0000&amp;PID_000n, but these ids are what
    /// pnputil's /deviceid matches.
    /// </summary>
    public static IReadOnlyList<string> FailedEnumerationIds { get; } =
    [
        @"USB\UNKNOWN",
        @"USB\RESET_FAILURE",
        @"USB\DEVICE_DESCRIPTOR_FAILURE",
        @"USB\CONFIG_DESCRIPTOR_FAILURE",
        @"USB\SET_ADDRESS_FAILURE",
        @"USB\DEVICE_DESCRIPTOR_VALIDATION_FAILURE",
        @"USB\CONFIGURATION_DESCRIPTOR_VALIDATION_FAILURE",
        @"USB\PORT_LINK_SSINACTIVE",
        @"USB\PORT_LINK_COMPLIANCE_MODE",
    ];

    /// <summary>What usb.inf names those devices, for a node whose ids were not readable.</summary>
    public static IReadOnlyList<string> FailurePhrases { get; } =
    [
        "Device Failed Enumeration",
        "Port Reset Failed",
        "Device Descriptor Request Failed",
        "Configuration Descriptor Request Failed",
        "Set Address Failed",
        "Invalid Device Descriptor",
        "Invalid Configuration Descriptor",
        "Link In SSInactive",
        "Link in Compliance Mode",
    ];

    /// <summary>
    /// USB vendor ids of Android phone makers. Makers whose id also covers keyboards, docks,
    /// hubs or motherboards (Lenovo, ASUS, Microsoft, Sony's consoles) are left out on purpose:
    /// a broken dock is not a phone.
    /// </summary>
    public static IReadOnlyDictionary<string, string> AndroidVendors { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["04E8"] = "Samsung",
        ["18D1"] = "Google",
        ["22B8"] = "Motorola",
        ["1004"] = "LG",
        ["0BB4"] = "HTC",
        ["0FCE"] = "Sony",
        ["12D1"] = "Huawei",
        ["2717"] = "Xiaomi",
        ["2A70"] = "OnePlus",
        ["22D9"] = "OPPO",
        ["2D95"] = "vivo",
        ["19D2"] = "ZTE",
        ["2E04"] = "Nokia",
        ["1949"] = "Amazon",
        ["2A45"] = "Meizu",
    };

    /// <summary>
    /// Problem codes that mean a phone's device is attached but unusable: it cannot start (10),
    /// has no driver (28), its driver could not load (31), or Windows stopped it after it
    /// reported a problem (43).
    /// </summary>
    public static IReadOnlySet<int> PhoneProblemCodes { get; } = new HashSet<int> { 10, 28, 31, 43 };

    /// <summary>CM_PROB_DISABLED: someone turned the device off on purpose, so it is left alone.</summary>
    public const int DisabledByUser = 22;

    public static bool IsFailedEnumerationId(string id) =>
        FailedEnumerationIds.Contains(id.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a node is a phone Windows could not use, and which kind; null for anything else.</summary>
    public static UsbProblem? Classify(UsbDeviceNode node)
    {
        if (node.ProblemCode <= 0 || node.ProblemCode == DisabledByUser ||
            !node.InstanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var vendor = VendorId(node.InstanceId) ?? node.HardwareIds.Select(VendorId).FirstOrDefault(v => v is not null);

        // VID 0000 is never a real maker: the hub uses it for a device it could not read at all.
        if (vendor == "0000" || node.HardwareIds.Any(IsFailedEnumerationId) ||
            FailurePhrases.Any(phrase => node.Description.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return new UsbProblem(node, UsbProblemKind.FailedEnumeration, string.Empty);
        }

        return vendor is not null && AndroidVendors.TryGetValue(vendor, out var maker) && PhoneProblemCodes.Contains(node.ProblemCode)
            ? new UsbProblem(node, UsbProblemKind.PhoneDevice, maker)
            : null;
    }

    public static IReadOnlyList<UsbProblem> Classify(IEnumerable<UsbDeviceNode> nodes) =>
        nodes.Select(Classify).OfType<UsbProblem>().ToArray();

    /// <summary>"USB\VID_04E8&amp;PID_6860&amp;MI_01\6&amp;..." reads as 04E8; null when the id names no vendor.</summary>
    public static string? VendorId(string id)
    {
        for (var at = id.IndexOf("VID_", StringComparison.OrdinalIgnoreCase); at >= 0; at = id.IndexOf("VID_", at + 1, StringComparison.OrdinalIgnoreCase))
        {
            var boundary = at == 0 || id[at - 1] is '\\' or '&';
            if (boundary && at + 8 <= id.Length && id.AsSpan(at + 4, 4).ToString().All(Uri.IsHexDigit))
            {
                return id.Substring(at + 4, 4).ToUpperInvariant();
            }
        }

        return null;
    }

    public static string ProblemText(int code) => code switch
    {
        10 => "it cannot start (code 10)",
        28 => "its driver is not installed (code 28)",
        31 => "its driver could not be loaded (code 31)",
        43 => "Windows stopped it because it reported problems (code 43)",
        _ => $"Windows reports problem code {code}",
    };
}

/// <summary>Where the USB problem scan reads from: Windows itself, or a test's file.</summary>
public interface IUsbDeviceSource
{
    /// <summary>Every attached USB device node Windows reports a problem for. Never throws.</summary>
    IReadOnlyList<UsbDeviceNode> ProblemNodes();
}

public static class UsbDeviceSource
{
    /// <summary>
    /// A JSON file the end-to-end tests point at instead of real hardware:
    /// <c>[{"instanceId":"USB\\VID_0000&amp;PID_0002\\5&amp;1","description":"...","hardwareIds":["USB\\DEVICE_DESCRIPTOR_FAILURE"],"problemCode":43}]</c>.
    /// It is read again on every scan, so a test can change it while the app runs.
    /// </summary>
    public const string FakeVariable = "REX_FAKE_USB_PROBLEMS";

    public static IUsbDeviceSource FromEnvironment() =>
        Environment.GetEnvironmentVariable(FakeVariable) is { Length: > 0 } path
            ? new FileUsbDeviceSource(path)
            : new WindowsUsbDeviceSource();
}

/// <summary>Nodes listed in a JSON file (see <see cref="UsbDeviceSource.FakeVariable"/>); no file means none.</summary>
public sealed class FileUsbDeviceSource(string path) : IUsbDeviceSource
{
    public IReadOnlyList<UsbDeviceNode> ProblemNodes()
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            // A file being rewritten by the test reads as nothing for one scan.
            return [];
        }
    }

    public static IReadOnlyList<UsbDeviceNode> Parse(string json) =>
        (JsonNode.Parse(json) as JsonArray ?? new JsonArray())
            .OfType<JsonObject>()
            .Select(node => new UsbDeviceNode(
                (string?)node["instanceId"] ?? string.Empty,
                (string?)node["description"] ?? string.Empty,
                (node["hardwareIds"] as JsonArray ?? new JsonArray()).Select(id => (string?)id ?? string.Empty).Where(id => id.Length > 0).ToArray(),
                (int?)node["problemCode"] ?? 0))
            .Where(node => node.InstanceId.Length > 0)
            .ToArray();
}

/// <summary>
/// The native part, kept thin: attached devices on the USB enumerator with a problem code,
/// straight from CfgMgr32, with the ids and name Windows gives them. Reads only.
/// </summary>
public sealed class WindowsUsbDeviceSource : IUsbDeviceSource
{
    private const int Success = 0x00;              // CR_SUCCESS
    private const int BufferSmall = 0x1A;          // CR_BUFFER_SMALL
    private const uint FilterEnumerator = 0x001;   // CM_GETIDLIST_FILTER_ENUMERATOR
    private const uint FilterPresent = 0x100;      // CM_GETIDLIST_FILTER_PRESENT
    private const uint HasProblem = 0x400;         // DN_HAS_PROBLEM
    private const uint DeviceDescription = 0x01;   // CM_DRP_DEVICEDESC
    private const uint HardwareId = 0x02;          // CM_DRP_HARDWAREID
    private const uint FriendlyName = 0x0D;        // CM_DRP_FRIENDLYNAME

    public IReadOnlyList<UsbDeviceNode> ProblemNodes()
    {
        var nodes = new List<UsbDeviceNode>();
        foreach (var id in PresentIds("USB"))
        {
            if (CM_Locate_DevNodeW(out var node, id, 0) != Success ||
                CM_Get_DevNode_Status(out var status, out var problem, node, 0) != Success ||
                (status & HasProblem) == 0 || problem == 0)
            {
                continue;
            }

            var name = Property(node, FriendlyName).FirstOrDefault() ?? Property(node, DeviceDescription).FirstOrDefault() ?? string.Empty;
            nodes.Add(new UsbDeviceNode(id, name, Property(node, HardwareId), (int)problem));
        }

        return nodes;
    }

    private static string[] PresentIds(string enumerator)
    {
        const uint flags = FilterEnumerator | FilterPresent;

        // The list can grow between asking for its size and reading it (a device arriving).
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_ID_List_SizeW(out var length, enumerator, flags) != Success || length == 0)
            {
                return [];
            }

            var buffer = new char[length];
            var result = CM_Get_Device_ID_ListW(enumerator, buffer, length, flags);
            if (result == Success)
            {
                return Split(buffer);
            }

            if (result != BufferSmall)
            {
                return [];
            }
        }

        return [];
    }

    /// <summary>A string or multi-string device property; empty when the device has none.</summary>
    private static string[] Property(uint node, uint property)
    {
        uint length = 0;
        var probe = CM_Get_DevNode_Registry_PropertyW(node, property, out _, null, ref length, 0);
        if ((probe != BufferSmall && probe != Success) || length == 0)
        {
            return [];
        }

        var buffer = new byte[length];
        return CM_Get_DevNode_Registry_PropertyW(node, property, out _, buffer, ref length, 0) == Success
            ? Split(Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(length, (uint)buffer.Length)).ToCharArray())
            : [];
    }

    private static string[] Split(char[] multiString) =>
        new string(multiString).Split('\0', StringSplitOptions.RemoveEmptyEntries);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint length, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint node, string instanceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_Registry_PropertyW(uint node, uint property, out uint type, [Out] byte[]? buffer, ref uint length, uint flags);
}
