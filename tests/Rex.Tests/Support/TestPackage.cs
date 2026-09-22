using System.Text.Json;
using Rex.Core;

namespace Rex.Tests.Support;

/// <summary>
/// A throwaway package folder: config.json + REX.bat, optionally with the fake adb/scrcpy installed
/// under tools/scrcpy so normal tool discovery finds them. Everything the fakes log lands inside it.
/// </summary>
public sealed class TestPackage : IDisposable
{
    public TestPackage(bool withFakeTools = false, Action<RexConfig>? configure = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "rex-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "REX.bat"), "@echo off\r\n");

        var config = new RexConfig();
        configure?.Invoke(config);
        ConfigFile.Save(Path.Combine(Root, "config.json"), config);
        File.Delete(Path.Combine(Root, "config.json.rex-backup"));

        Paths = AppPaths.FromRoot(Root);
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
    public string FakeAdbScenario => Path.Combine(ToolsFolder, "fake-adb.json");

    public string[] AdbCalls() => File.Exists(FakeAdbLog) ? File.ReadAllLines(FakeAdbLog) : [];
    public string[] ScrcpyLog() => File.Exists(FakeScrcpyLog) ? File.ReadAllLines(FakeScrcpyLog) : [];

    /// <summary>Rewrites the fake adb scenario (devices, properties, settings, keyguard).</summary>
    public void WriteScenario(object scenario) =>
        File.WriteAllText(FakeAdbScenario, JsonSerializer.Serialize(scenario, new JsonSerializerOptions { WriteIndented = true }));

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
}
