using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rex.Core;

/// <summary>
/// Loads and saves <see cref="RexConfig"/>. Writes are atomic (temp file + move) and
/// keep the previous valid file as config.json.rex-backup so a bad change can be undone.
/// Old schema-1 files (flat keys such as TurnPhysicalScreenOff) are migrated on load.
/// </summary>
public static class ConfigFile
{
    public static string BackupPath(string path) => path + ".rex-backup";

    public static RexConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var fresh = new RexConfig();
            fresh.Normalize();
            return fresh;
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new InvalidOperationException("config.json does not contain a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"config.json is not valid JSON ({ex.Message}). Fix it or restore config.json.rex-backup.", ex);
        }

        var config = LegacyConfigMigration.IsLegacy(root)
            ? LegacyConfigMigration.Migrate(root)
            : Deserialize(root);

        config.Normalize();
        return config;
    }

    public static void Save(string path, RexConfig config)
    {
        config.Normalize();
        var json = JsonSerializer.Serialize(config, RexJsonContext.Default.RexConfig) + Environment.NewLine;
        AtomicFile.Write(path, json, keepBackupAt: BackupPath(path), validate: text =>
            JsonNode.Parse(text)?.AsObject() is not null);
    }

    public static bool RestoreBackup(string path)
    {
        using var transaction = CrossProcessFileLock.Acquire(path);

        var backup = BackupPath(path);
        if (!File.Exists(backup))
        {
            return false;
        }

        var candidate = File.ReadAllText(backup);
        _ = JsonNode.Parse(candidate)?.AsObject()
            ?? throw new InvalidOperationException("config.json.rex-backup is not a valid JSON object.");

        var current = File.Exists(path) ? File.ReadAllText(path) : null;
        AtomicFile.Write(path, candidate, keepBackupAt: null, validate: null);
        if (current is not null)
        {
            File.WriteAllText(backup, current);
        }

        return true;
    }

    private static RexConfig Deserialize(JsonObject root)
    {
        try
        {
            return root.Deserialize(RexJsonContext.Default.RexConfig) ?? new RexConfig();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"config.json has a value of the wrong type: {ex.Message}", ex);
        }
    }
}

/// <summary>Maps the schema-1 (flat PowerShell era) keys into the typed schema-2 model.</summary>
internal static class LegacyConfigMigration
{
    public static bool IsLegacy(JsonObject root) =>
        root["Version"] is null && (root["MirrorChrome"] is not null || root["TurnPhysicalScreenOff"] is not null || root["ScrcpySession"] is not null);

    public static RexConfig Migrate(JsonObject root)
    {
        var config = new RexConfig();

        config.Session.TurnScreenOff = Bool(root, "TurnPhysicalScreenOff", config.Session.TurnScreenOff);
        config.Session.StayAwake = Bool(root, "StayAwakeWhenUsb", config.Session.StayAwake);
        config.Session.KeepActive = Bool(root, "KeepActiveDuringMirror", config.Session.KeepActive);
        config.Session.WakeBeforeMirror = Bool(root, "WakeBeforeMirror", config.Session.WakeBeforeMirror);
        config.Session.DismissKeyguard = Bool(root, "DismissKeyguardWhenPossible", config.Session.DismissKeyguard);
        config.Session.PowerOffOnClose = Bool(root, "PowerOffOnClose", config.Session.PowerOffOnClose);
        config.Session.RestartOnUnexpectedExit = Bool(root, "RestartOnUnexpectedExit", config.Session.RestartOnUnexpectedExit);
        config.Session.PreferUsb = Bool(root, "PreferUsb", config.Session.PreferUsb);
        config.Session.PreferredSerial = Str(root, "PreferredSerial", config.Session.PreferredSerial);
        config.Session.PollSeconds = Int(root, "PollSeconds", config.Session.PollSeconds);
        config.Session.RetrySeconds = Int(root, "RetrySeconds", config.Session.RetrySeconds);

        config.Mirror.MaxSize = Int(root, "MaxSize", config.Mirror.MaxSize);
        config.Mirror.MaxFps = Int(root, "MaxFps", config.Mirror.MaxFps);
        config.Mirror.VideoBitRate = Str(root, "VideoBitRate", config.Mirror.VideoBitRate);
        config.Mirror.ExtraArgs = Str(root, "ExtraScrcpyArgs", config.Mirror.ExtraArgs);

        if (root["ScrcpySession"] is JsonObject session)
        {
            config.Mirror.VideoCodec = Str(session, "VideoCodec", config.Mirror.VideoCodec);
            config.Mirror.Audio = Bool(session, "AudioEnabled", config.Mirror.Audio);
            config.Mirror.AudioCodec = Str(session, "AudioCodec", config.Mirror.AudioCodec);
            config.Mirror.AudioBufferMs = Int(session, "AudioBufferMs", config.Mirror.AudioBufferMs);
            config.Mirror.AudioDup = Bool(session, "AudioDup", config.Mirror.AudioDup);
            config.Mirror.RecordOnStart = Bool(session, "RecordOnStart", config.Mirror.RecordOnStart);
            config.Mirror.RecordDirectory = Str(session, "RecordDirectory", config.Mirror.RecordDirectory);
        }

        if (root["Wireless"] is JsonObject wireless)
        {
            config.Wireless.Enabled = Bool(wireless, "Enabled", config.Wireless.Enabled);
            config.Wireless.Port = Int(wireless, "Port", config.Wireless.Port);
            config.Wireless.EnableTcpipWhenUsbAvailable = Bool(wireless, "EnableTcpipWhenUsbAvailable", false);
            if (wireless["ManualHosts"] is JsonArray hosts)
            {
                config.Wireless.ManualHosts = hosts.Select(x => x?.ToString() ?? string.Empty).ToList();
            }
        }

        if (root["MirrorChrome"] is JsonObject chrome)
        {
            config.Touchpad.Enabled = Bool(chrome, "NativeTouchpadGestures", config.Touchpad.Enabled);
            config.Touchpad.TwoFingerToAndroid = Bool(chrome, "TouchpadPinchToAndroid", config.Touchpad.TwoFingerToAndroid);
            config.Zoom.Enabled = Bool(chrome, "HostZoomEnabled", config.Zoom.Enabled);
            config.Zoom.WheelZoom = Bool(chrome, "WheelToHostZoom", Bool(chrome, "CtrlWheelZoom", config.Zoom.WheelZoom));
            config.Zoom.PinchZoom = Bool(chrome, "TouchpadPinchToHostZoom", Bool(chrome, "CtrlTouchpadPinchToHostZoom", config.Zoom.PinchZoom));
            config.Zoom.MaxZoom = Dbl(chrome, "MaxZoom", config.Zoom.MaxZoom);
            config.Zoom.WheelStep = Dbl(chrome, "ZoomStep", config.Zoom.WheelStep);
            config.Zoom.ShowNavigator = Bool(chrome, "ShowZoomMinimap", config.Zoom.ShowNavigator);
        }

        if (root["PatternOverlay"] is JsonObject overlay)
        {
            config.PatternGuide.Enabled = Bool(overlay, "Enabled", config.PatternGuide.Enabled);
            config.PatternGuide.AskPerDevice = Bool(overlay, "PromptPerDevice", config.PatternGuide.AskPerDevice);
            config.PatternGuide.AutoShowOnKeyguard = Bool(overlay, "AutoShowOnKeyguard", config.PatternGuide.AutoShowOnKeyguard);
            config.PatternGuide.AutoDiscoverGeometry = Bool(overlay, "AutoDiscoverGeometry", config.PatternGuide.AutoDiscoverGeometry);
            config.PatternGuide.CalibrationEnabled = Bool(overlay, "CalibrationEnabled", config.PatternGuide.CalibrationEnabled);
            config.PatternGuide.ShowCursorTrail = Bool(overlay, "ShowCursorTrail", config.PatternGuide.ShowCursorTrail);
            config.PatternGuide.Opacity = Dbl(overlay, "Opacity", config.PatternGuide.Opacity);
        }

        if (root["ControlCenter"] is JsonObject center)
        {
            config.App.ConfirmSensitiveWrites = Bool(center, "ConfirmSensitiveDeviceWrites", config.App.ConfirmSensitiveWrites);
            config.App.ScreenshotDirectory = Str(center, "ScreenshotDirectory", config.App.ScreenshotDirectory);
        }

        if (root["Logging"] is JsonObject logging)
        {
            config.Logging.Enabled = Bool(logging, "Enabled", config.Logging.Enabled);
            config.Logging.MaxBytes = Long(logging, "MaxBytes", config.Logging.MaxBytes);
            config.Logging.KeepFiles = Int(logging, "KeepFiles", config.Logging.KeepFiles);
        }

        return config;
    }

    private static bool Bool(JsonObject obj, string key, bool fallback) =>
        obj[key] is JsonValue value && value.TryGetValue<bool>(out var parsed) ? parsed : fallback;

    private static int Int(JsonObject obj, string key, int fallback) =>
        obj[key] is JsonValue value && value.TryGetValue<int>(out var parsed) ? parsed : fallback;

    private static long Long(JsonObject obj, string key, long fallback) =>
        obj[key] is JsonValue value && value.TryGetValue<long>(out var parsed) ? parsed : fallback;

    private static double Dbl(JsonObject obj, string key, double fallback) =>
        obj[key] is JsonValue value && value.TryGetValue<double>(out var parsed) ? parsed : fallback;

    private static string Str(JsonObject obj, string key, string fallback) =>
        obj[key] is JsonValue value && value.TryGetValue<string>(out var parsed) ? parsed : fallback;
}

/// <summary>Write-to-temp-then-move so a crash mid-write never leaves a truncated file.</summary>
public static class AtomicFile
{
    public static void Write(string path, string content, string? keepBackupAt, Func<string, bool>? validate)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var fileName = Path.GetFileName(path);
        var temp = Path.Combine(
            directory ?? string.Empty,
            $".{fileName}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temp, content);

            if (validate is not null && !validate(File.ReadAllText(temp)))
            {
                throw new InvalidOperationException($"Refusing to save an invalid file to {Path.GetFileName(path)}.");
            }

            if (keepBackupAt is not null && File.Exists(path))
            {
                File.Copy(path, keepBackupAt, overwrite: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // Best-effort cleanup only. The destination write has already completed or failed.
            }
        }
    }
}
