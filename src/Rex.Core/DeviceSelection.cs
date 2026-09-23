namespace Rex.Core;

/// <summary>Picks which connected phone to mirror. A preferred serial is a preference, never a lock.</summary>
public static class DeviceSelection
{
    public static AdbDevice? Select(IReadOnlyList<AdbDevice> devices, string preferredSerial, bool preferUsb)
    {
        var ready = devices.Where(x => x.IsReady).ToArray();
        if (ready.Length == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredSerial))
        {
            var match = ready.FirstOrDefault(x => x.Serial == preferredSerial);
            if (match is not null)
            {
                return match;
            }
        }

        if (preferUsb)
        {
            var usb = ready.FirstOrDefault(x => !x.IsTcp);
            if (usb is not null)
            {
                return usb;
            }
        }

        return ready[0];
    }

    /// <summary>The configured serial wins over the learned one; both are only preferences.</summary>
    public static string EffectivePreferredSerial(RexConfig config, StateStore state) =>
        string.IsNullOrWhiteSpace(config.Session.PreferredSerial) ? state.PreferredSerial : config.Session.PreferredSerial;
}

/// <summary>Human wording for what ADB is reporting, so the UI never shows a bare state token.</summary>
public static class DeviceStateText
{
    public const string UsbBlockedHeader = "Phone found · repair USB driver";
    public const string UsbBlockedText =
        "The phone is plugged in, but Windows attached a generic driver to its USB debugging interface, so ADB cannot see it. " +
        "Repair the USB driver (Windows asks for administrator approval once).";

    public static string Header(bool setupRequired, IReadOnlyList<AdbDevice> devices, bool usbBlocked = false)
    {
        if (setupRequired)
        {
            return "Setup required";
        }

        if (devices.Any(x => x.IsUnauthorized))
        {
            return "Phone found · approve USB debugging";
        }

        if (devices.Any(x => x.IsReady))
        {
            return "Phone ready";
        }

        return devices.Count == 0 && usbBlocked ? UsbBlockedHeader : "No phone connected";
    }

    public static string Describe(IReadOnlyList<AdbDevice> devices, bool usbBlocked = false)
    {
        if (devices.Count == 0)
        {
            return usbBlocked ? UsbBlockedText : "Connect an Android phone with USB debugging turned on.";
        }

        if (devices.Any(x => x.IsReady))
        {
            return "Phone ready.";
        }

        if (devices.Any(x => x.IsUnauthorized))
        {
            return "Phone found. Unlock it and tap Allow on the USB debugging prompt (tick 'Always allow from this computer').";
        }

        if (devices.Any(x => x.State == "offline"))
        {
            return "Phone is offline to ADB. Reconnect the cable, or reboot the phone if it stays offline.";
        }

        if (devices.Any(x => x.State == "no permissions"))
        {
            return "Windows cannot access the phone (no permissions). Check the USB driver and connection mode.";
        }

        return $"Phone reports '{devices[0].State}'. Waiting for it to become ready.";
    }
}
