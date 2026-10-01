using Rex.Tests.Support;

namespace Rex.Tests;

public sealed partial class AppEndToEndTests
{
    [Fact(Timeout = 120_000)]
    public async Task SendTo_QueuesFilesFromASecondInstanceUntilAPhoneConnects()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: config => config.Transfer.ConfirmOverMb = 0);
        package.WriteScenario(new { Devices = Array.Empty<object>() });
        var file = Path.Combine(package.Root, "sent from explorer.txt");
        File.WriteAllText(file, "hello");
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("waiting", StartupTimeout);

        using var second = app.StartAnother("--send", file);
        Assert.True(await Task.Run(() => second.WaitForExit(15_000), TestContext.Current.CancellationToken));
        Assert.Equal(0, second.ExitCode);
        Assert.DoesNotContain(package.AdbCalls(), call => call.Contains("push", StringComparison.Ordinal));

        File.Delete(package.FakeAdbScenario);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitForStatusAsync(status => status["files"]!["done"]!.GetValue<int>() == 1,
            StartupTimeout, "the file handed over by the second instance");
        Assert.Contains(package.AdbCalls(), call => call.Contains("push", StringComparison.Ordinal) && call.Contains("sent from explorer.txt", StringComparison.Ordinal));
        await app.QuitAsync();
    }
}
