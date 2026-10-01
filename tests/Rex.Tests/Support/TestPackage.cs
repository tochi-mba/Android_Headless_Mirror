using System.Text.Json;
using Rex.Core;

namespace Rex.Tests.Support;

/// <summary>
/// A throwaway package folder: config.json + REX.bat, optionally with the fake adb/scrcpy installed
/// under tools/scrcpy so normal tool discovery finds them. Everything the fakes log lands inside it.
/// </summary>
public sealed class TestPackage : IDisposable
{
    /// <param name="showTour">
    /// Whether the first-run tour should appear. It is off by default because it covers the window
    /// on purpose: a test that is not about the tour would otherwise be driving a scrim.
    /// </param>
    public TestPackage(bool withFakeTools = false, Action<RexConfig>? configure = null, bool showTour = false)
    {
        Root = Path.Combine(Path.GetTempPath(), "rex-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "REX.bat"), "@echo off\r\n");

        var config = new RexConfig();
        configure?.Invoke(config);
        ConfigFile.Save(Path.Combine(Root, "config.json"), config);
        File.Delete(Path.Combine(Root, "config.json.rex-backup"));

        Paths = AppPaths.FromRoot(Root);
        // The phone's sound as Windows' volume mixer would hold it, so tests never touch this PC's audio.
        File.WriteAllText(SoundFile, """{"available":true,"volume":1,"muted":false,"channels":2,"peak":0.4}""");
        if (!showTour)
        {
            new StateStore(Paths.State).SetUi(new UiState { TourSeenVersion = Rex.Mirror.Views.Tour.Version });
        }

        if (withFakeTools)
        {
            InstallFakeTools();
        }
    }

    public string Root { get; }
    public AppPaths Paths { get; }
    public string ToolsFolder => Path.Combine(Root, "tools", "scrcpy", "v9.9-fake");
    public string FakeAdbLog => Path.Combine(ToolsFolder, "fake-adb.log");
    public string FakeScrcpyLog => Path.Combine(ToolsFolder, "fake-scrcpy.log");
    public string DenyUhidMarker => Path.Combine(ToolsFolder, "deny-uhid");
    public string AnimateMarker => Path.Combine(ToolsFolder, "animate");

    /// <summary>While this exists, the fake phone shows a made-up home screen instead of its test card.</summary>
    public string HomeScreenMarker => Path.Combine(ToolsFolder, "home-screen");
    public string KeepWindowMarker => Path.Combine(ToolsFolder, "keep-window");

    /// <summary>With this present, every copy of the phone fails as if it had no video encoder left.</summary>
    public string FailCopiesMarker => Path.Combine(ToolsFolder, "fail-copies");
    public string FakeAdbScenario => Path.Combine(ToolsFolder, "fake-adb.json");

    /// <summary>With this present, the fake phone says it is too old to send its sound.</summary>
    public string NoAudioMarker => Path.Combine(ToolsFolder, "no-audio");

    /// <summary>The fake audio session of the phone's sound (<c>REX_FAKE_AUDIO</c>).</summary>
    public string SoundFile => Path.Combine(Root, "fake-audio.json");

    /// <summary>The USB devices the app is told Windows could not read (none until a test writes them).</summary>
    public string UsbProblemsFile => Path.Combine(Root, "usb-problems.json");

    /// <summary>Where the app records Task Scheduler and administrator-prompt calls instead of making them.</summary>
    public string UsbRepairLog => Path.Combine(Root, "usb-repair.log");

    /// <summary>Where the app writes the web addresses it would have opened in a browser.</summary>
    public string BrowserLog => Path.Combine(Root, "browser.log");

    public string[] OpenedPages() => ReadLiveLog(BrowserLog);

    public string[] UsbRepairCalls() => ReadLiveLog(UsbRepairLog);

    /// <summary>Tells the app Windows reports these devices; an empty list clears them.</summary>
    public void WriteUsbProblems(params object[] nodes) =>
        File.WriteAllText(UsbProblemsFile, JsonSerializer.Serialize(nodes));

    public string[] AdbCalls() => ReadLiveLog(FakeAdbLog);

    /// <summary>The fake audio session as the app last left it, or null while it is being replaced.</summary>
    public (double Volume, bool Muted, double Left, double Right)? Sound()
    {
        try
        {
            var file = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(SoundFile))!;
            return (file["volume"]?.GetValue<double>() ?? 1, file["muted"]?.GetValue<bool>() == true,
                file["left"]?.GetValue<double>() ?? 1, file["right"]?.GetValue<double>() ?? 1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Plays a change made in the Windows volume mixer: the app sees it at its next look.</summary>
    public void ChangeSoundOutside(double volume, bool muted)
    {
        var file = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(SoundFile))!.AsObject();
        file["outsideVolume"] = volume;
        file["outsideMuted"] = muted;
        AtomicFile.Write(SoundFile, file.ToJsonString(), keepBackupAt: null, validate: null);
    }
    public string[] ScrcpyLog() => ReadLiveLog(FakeScrcpyLog);

    private static string[] ReadLiveLog(string path)
    {
        if (!File.Exists(path)) return [];
        // The fake tools continue appending while the app polls. A read must not
        // deny their write handle (or fail just because a writer already has it).
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines.ToArray();
    }

    /// <summary>Rewrites the fake adb scenario (devices, properties, settings, keyguard).</summary>
    public void WriteScenario(object scenario) =>
        File.WriteAllText(FakeAdbScenario, JsonSerializer.Serialize(scenario, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>A colourful stand-in for a phone screen, served by both fakes as the capture and the video.</summary>
    public static void WritePreviewImage(string path)
    {
        var bounds = new System.Drawing.Rectangle(0, 0, 360, 800);
        using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(bounds, System.Drawing.Color.SteelBlue, System.Drawing.Color.DarkOrange, 60f))
        {
            graphics.FillRectangle(brush, bounds);
        }

        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private void InstallFakeTools()
    {
        Directory.CreateDirectory(ToolsFolder);
        foreach (var file in Directory.GetFiles(RepoPaths.FakeAdbOutput))
        {
            File.Copy(file, Path.Combine(ToolsFolder, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var file in Directory.GetFiles(RepoPaths.FakeScrcpyOutput))
        {
            File.Copy(file, Path.Combine(ToolsFolder, Path.GetFileName(file)), overwrite: true);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A fake process may still be shutting down; the temp folder is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}

/// <summary>Locations inside the repository, derived from where the test assembly runs.</summary>
public static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string Configuration { get; } =
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";

    public static string FakeAdbOutput => Output("tests", "Rex.FakeAdb");
    public static string FakeScrcpyOutput => Output("tests", "Rex.FakeScrcpy");
    public static string MirrorOutput => Output("src", "Rex.Mirror");
    public static string MirrorExecutable => Path.Combine(MirrorOutput, "RexMirror.exe");
    public static string CliExecutable => Path.Combine(Output("src", "Rex.Cli"), "rex.exe");
    public static string Screens => Path.Combine(Root, "artifacts", "screens");

    private static string Output(string area, string project) =>
        Path.Combine(Root, area, project, "bin", Configuration, "net10.0-windows");

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Rex.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find Rex.sln above the test assembly.");
    }
}

/// <summary>Records process invocations instead of running them.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public List<(string FileName, string[] Arguments)> Calls { get; } = [];
    public Func<string[], ProcessResult> Respond { get; set; } = _ => new ProcessResult(0, string.Empty, string.Empty);
    public byte[] Bytes { get; set; } = [];

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        Calls.Add((fileName, arguments.ToArray()));
        return Task.FromResult(Respond(arguments.ToArray()));
    }

    public Task<ProcessBytesResult> RunBytesAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        Calls.Add((fileName, arguments.ToArray()));
        return Task.FromResult(new ProcessBytesResult(0, Bytes, string.Empty));
    }

    /// <summary>Detached runs (adb start-server) are recorded apart from captured commands.</summary>
    public List<string[]> Detached { get; } = [];

    public Task<int> RunDetachedAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        Detached.Add(arguments.ToArray());
        return Task.FromResult(0);
    }
}
