using System.Diagnostics;
using Rex.Tests.Support;

namespace Rex.Tests;

public sealed class InstallerUpgradeTests
{
    [Fact]
    public async Task Preparation_StopsBundledAdbButPreservesOtherInstallations_AndCanRepeat()
    {
        var root = Path.Combine(Path.GetTempPath(), "rex-upgrade-" + Guid.NewGuid().ToString("N"));
        var install = Path.Combine(root, "App with spaces");
        var other = install + "-other";
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(other);
        Process StartAdb(string folder)
        {
            foreach (var file in Directory.GetFiles(RepoPaths.FakeScrcpyOutput))
                File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
            File.Copy(Path.Combine(folder, "scrcpy.exe"), Path.Combine(folder, "adb.exe"));
            return Process.Start(new ProcessStartInfo(Path.Combine(folder, "adb.exe"), "--rex-test-child")
            { UseShellExecute = false, CreateNoWindow = true })!;
        }
        using var bundled = StartAdb(install);
        using var unrelated = StartAdb(other);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var start = new ProcessStartInfo("powershell.exe")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                    Path.Combine(RepoPaths.Root, "installer", "prepare-upgrade.ps1"), "-InstallDirectory", install })
                    start.ArgumentList.Add(arg);
                using var preparation = Process.Start(start)!;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(40));
                await preparation.WaitForExitAsync(timeout.Token);
                Assert.True(preparation.ExitCode == 0, await preparation.StandardError.ReadToEndAsync(timeout.Token));
                Assert.True(bundled.HasExited);
                Assert.False(unrelated.HasExited);
            }
            // The executable really is unlocked, not merely reported as stopped.
            File.Delete(Path.Combine(install, "adb.exe"));
        }
        finally
        {
            foreach (var process in new[] { bundled, unrelated })
            {
                if (!process.HasExited) process.Kill();
                process.WaitForExit();
            }
            Directory.Delete(root, true);
        }
    }
}
