namespace Rex.Mirror.Views.Settings;

/// <summary>The setting-to-control contract used by search, tests and the generated settings reference.</summary>
internal static class SettingsCatalogue
{
    public static readonly IReadOnlyDictionary<string, string> Controls = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Mirror.MaxSize"] = "MaxSize",
        ["Mirror.MaxFps"] = "MaxFps",
        ["Mirror.VideoBitRate"] = "BitRate",
        ["Mirror.VideoCodec"] = "VideoCodec",
        ["Mirror.Audio"] = "Audio",
        ["Mirror.AudioDup"] = "AudioDup",
        ["Mirror.AudioSource"] = "AudioSource",
        ["Mirror.AudioBitRate"] = "AudioBitRate",
        ["Mirror.VideoBufferMs"] = "VideoBuffer",
        ["Mirror.DownsizeOnError"] = "DownsizeOnError",
        ["Mirror.RenderDriver"] = "RenderDriver",
        ["Mirror.RecordFormat"] = "RecordFormat",
        ["Mirror.RecordOnStart"] = "Record",
        ["Mirror.ExtraArgs"] = "ExtraArgs",
        ["Mirror.CompatibilityKeyboard"] = "CompatibilityKeyboard",

        ["Session.TurnScreenOff"] = "TurnScreenOff",
        ["Session.StayAwake"] = "StayAwake",
        ["Session.PowerOffOnClose"] = "PowerOffOnClose",
        ["Session.RestartOnUnexpectedExit"] = "RestartOnCrash",
        ["Session.ScreenOffTimeoutSeconds"] = "ScreenOffTimeout",
        ["Session.KeepPcAwake"] = "KeepPcAwake",
        ["Session.StartApp"] = "StartApp",

        ["Wireless.Enabled"] = "Wireless",
        ["Wireless.EnableTcpipWhenUsbAvailable"] = "WirelessTcpip",
        ["Touchpad.TwoFingerToAndroid"] = "TwoFinger",
        ["Touchpad.Sensitivity"] = "Sensitivity",

        ["Input.RightClick"] = "RightClick",
        ["Input.MiddleClick"] = "MiddleClick",
        ["Input.BackButton"] = "BackButton",
        ["Input.ForwardButton"] = "ForwardButton",
        ["Input.ShiftClicks"] = "ShiftClicks",
        ["Input.KeyRepeat"] = "KeyRepeat",
        ["Input.MouseHover"] = "MouseHover",
        ["Input.ClipboardAutosync"] = "ClipboardAutosync",
        ["Input.LegacyPaste"] = "LegacyPaste",
        ["Input.Gamepad"] = "Gamepad",
        ["Input.SwipeLength"] = "SwipeLength",
        ["Input.SwipeMilliseconds"] = "SwipeMilliseconds",

        ["Zoom.Enabled"] = "HostZoom",
        ["Zoom.MaxZoom"] = "MaximumZoom",
        ["Zoom.WheelStep"] = "WheelSpeed",
        ["Zoom.ResetOnRotate"] = "ResetOnRotate",
        ["Zoom.InvertWheel"] = "InvertWheel",
        ["Zoom.ZoomAtPointer"] = "ZoomAnchor",
        ["Zoom.ShowNavigator"] = "Navigator",
        ["Zoom.NavigatorCorner"] = "NavigatorCorner",
        ["Zoom.NavigatorWidth"] = "NavigatorWidth",
        ["Zoom.NavigatorPicture"] = "NavigatorPicture",
        ["Zoom.NavigatorOpacity"] = "NavigatorOpacity",
        ["Zoom.NavigatorFrameRate"] = "NavigatorFrameRate",
        ["Zoom.NavigatorAlways"] = "NavigatorAlways",

        ["Copies.Most"] = "CopiesMost",
        ["Copies.Gap"] = "CopiesGap",
        ["Copies.MaxSize"] = "CopiesMaxSize",
        ["Copies.Remember"] = "CopiesRemember",

        ["Ambient.Enabled"] = "AmbientEnabled",
        ["Ambient.Opacity"] = "AmbientOpacity",
        ["Ambient.Blur"] = "AmbientBlur",
        ["Ambient.Placement"] = "AmbientPlacement",
        ["Ambient.Scaling"] = "AmbientScaling",
        ["Ambient.Size"] = "AmbientSize",
        ["Ambient.OffsetX"] = "AmbientOffsetX",
        ["Ambient.OffsetY"] = "AmbientOffsetY",
        ["Ambient.FlipHorizontal"] = "AmbientFlip",
        ["Ambient.EdgeFade"] = "AmbientEdgeFade",
        ["Ambient.TintStrength"] = "AmbientTintStrength",
        ["Ambient.TintHue"] = "AmbientTintHue",
        ["Ambient.FrameRate"] = "AmbientFrameRate",

        ["Hud.Enabled"] = "HudEnabled",
        ["Hud.Position"] = "HudPosition",
        ["Hud.Buttons"] = "HudButtons",
        ["Hud.HideSeconds"] = "HudDelay",
        ["Hud.Scale"] = "HudScale",
        ["Hud.Opacity"] = "HudOpacity",
        ["Hud.ShowMessages"] = "HudMessages",

        ["PatternGuide.Enabled"] = "PatternEnabled",
        ["PatternGuide.AutoShowOnKeyguard"] = "PatternAuto",
        ["PatternGuide.AutoDiscoverGeometry"] = "PatternDiscover",

        ["App.RunInBackground"] = "RunInBackground",
        ["App.OpenOnConnect"] = "OpenOnConnect",
        ["App.ConfirmSensitiveWrites"] = "ConfirmWrites",
        ["App.ScreenshotDirectory"] = "ScreenshotLocation",
        ["App.AutoRepairUsb"] = "AutoRepairUsb",
        ["App.AlwaysOnTop"] = "AlwaysOnTop",
        ["App.SidebarSide"] = "SidebarSide",
        ["App.TopBarButtons"] = "QuickButtonChoices",
        ["App.ShowHints"] = "ShowHints",
        ["App.NotifyConnections"] = "NotifyConnections",
        ["App.ShowFrameRate"] = "ShowFrameRate",
        ["App.ScreenshotFormat"] = "ScreenshotFormat",
        ["App.CopyScreenshots"] = "CopyScreenshots",
    };

    public static readonly IReadOnlyDictionary<string, string> Elsewhere = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Version"] = "Managed by the app.",
        ["Hud.X"] = "Set by dragging the fullscreen controls.",
        ["Hud.Y"] = "Set by dragging the fullscreen controls.",
        ["Session.PreferredSerial"] = "Set by the phone picker.",
    };

    public static readonly IReadOnlySet<string> NotYet = new HashSet<string>(StringComparer.Ordinal)
    {
        "Mirror.AudioCodec", "Mirror.AudioBufferMs", "Mirror.RecordDirectory",
        "Session.KeepActive", "Session.WakeBeforeMirror", "Session.DismissKeyguard", "Session.PreferUsb",
        "Session.PollSeconds", "Session.RetrySeconds", "Wireless.Port", "Wireless.ManualHosts",
        "Touchpad.Enabled", "Zoom.WheelZoom", "Zoom.PinchZoom", "PatternGuide.AskPerDevice",
        "PatternGuide.CalibrationEnabled", "PatternGuide.ShowCursorTrail", "PatternGuide.Opacity",
        "Logging.Enabled", "Logging.MaxBytes", "Logging.KeepFiles",
    };
}
