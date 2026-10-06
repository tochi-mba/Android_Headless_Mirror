namespace Rex.Tests.Support;

/// <summary>The app's own cost while it runs: its process only, never the fake phone's tools.</summary>
public sealed partial class AppProcess
{
    /// <summary>
    /// CPU as a share of one core, the middle of several slices so one hiccup is not the answer,
    /// and the memory, handles and threads at the end, each named <c>&lt;state&gt;.&lt;metric&gt;</c>.
    /// </summary>
    public async Task<Dictionary<string, double>> MeasureCostAsync(string state, int slices = 5, int sliceMs = 2000)
    {
        var shares = new List<double>();
        for (var i = 0; i < slices; i++)
        {
            _process.Refresh();
            var cpu = _process.TotalProcessorTime;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // The slice's length is itself what is being measured over.
            await Task.Delay(sliceMs, TestContext.Current.CancellationToken);
            _process.Refresh();
            shares.Add((_process.TotalProcessorTime - cpu).TotalMilliseconds / clock.Elapsed.TotalMilliseconds * 100);
        }

        _process.Refresh();
        const double Mb = 1024 * 1024;
        return new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [state + ".cpu"] = shares.Order().ElementAt(shares.Count / 2),
            [state + ".workingSetMb"] = _process.WorkingSet64 / Mb,
            [state + ".privateMb"] = _process.PrivateMemorySize64 / Mb,
            [state + ".handles"] = _process.HandleCount,
            [state + ".threads"] = _process.Threads.Count,
        };
    }

    /// <summary>How long ago the app's process started, in milliseconds.</summary>
    public double MillisecondsSinceStart() => Math.Round((DateTime.Now - _process.StartTime).TotalMilliseconds);
}
