using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>The cost baseline's comparison, without running the app: what counts as over, and the files it reads and writes.</summary>
public sealed class CostBaselineTests
{
    private static readonly CostTolerance Tolerance = new();

    [Theory]
    [InlineData("hidden.cpu", CostBaseline.Kind.Cpu)]
    [InlineData("hidden.privateMb", CostBaseline.Kind.Memory)]
    [InlineData("start.ms", CostBaseline.Kind.Time)]
    [InlineData("hidden.handles", CostBaseline.Kind.Count)]
    public void AMetricsNameSaysWhatKindOfNumberItIs(string metric, CostBaseline.Kind kind) =>
        Assert.Equal(kind, CostBaseline.KindOf(metric));

    [Fact]
    public void EachKindHasItsOwnRoom()
    {
        Assert.Equal(10 * 2 + 3, CostBaseline.Limit(10, CostBaseline.Kind.Cpu, Tolerance));
        Assert.Equal(100 * 1.35 + 40, CostBaseline.Limit(100, CostBaseline.Kind.Memory, Tolerance), 6);
        Assert.Equal(2000 * 2 + 3000, CostBaseline.Limit(2000, CostBaseline.Kind.Time, Tolerance));
        Assert.Equal(400 * 1.5 + 100, CostBaseline.Limit(400, CostBaseline.Kind.Count, Tolerance));
    }

    [Fact]
    public void ARunIsComparedNumberByNumber()
    {
        var baseline = new Dictionary<string, double>
        {
            ["a.cpu"] = 2, ["b.cpu"] = 2, ["c.privateMb"] = 200, ["gone.threads"] = 30, ["d.cpu"] = 20,
        };
        var measured = new Dictionary<string, double>
        {
            ["a.cpu"] = 7, ["b.cpu"] = 7.5, ["c.privateMb"] = 100, ["new.ms"] = 900, ["d.cpu"] = 19,
        };

        var lines = CostBaseline.Compare(baseline, measured, Tolerance).ToDictionary(l => l.Metric);

        Assert.Equal(["a.cpu", "b.cpu", "c.privateMb", "d.cpu", "gone.threads", "new.ms"], lines.Keys);
        Assert.Equal(CostVerdict.Ok, lines["a.cpu"].Verdict);          // 7 <= 2 x 2 + 3
        Assert.Equal(CostVerdict.Over, lines["b.cpu"].Verdict);        // 7.5 > 7
        Assert.Equal(CostVerdict.Lower, lines["c.privateMb"].Verdict); // half, by more than 40 MB
        Assert.Equal(CostVerdict.Ok, lines["d.cpu"].Verdict);
        Assert.Equal(CostVerdict.Missing, lines["gone.threads"].Verdict);
        Assert.Equal(CostVerdict.New, lines["new.ms"].Verdict);
        Assert.Null(lines["new.ms"].Limit);
        Assert.Null(lines["gone.threads"].Measured);
    }

    [Fact]
    public void ASmallNumberHalvingIsStillNoise()
    {
        var lines = CostBaseline.Compare(new Dictionary<string, double> { ["a.cpu"] = 2 }, new Dictionary<string, double> { ["a.cpu"] = 0.5 }, Tolerance);
        Assert.Equal(CostVerdict.Ok, Assert.Single(lines).Verdict);
    }

    [Fact]
    public void TheReportSaysWhatEachLineMeans()
    {
        var lines = CostBaseline.Compare(
            new Dictionary<string, double> { ["a.cpu"] = 2, ["b.cpu"] = 2, ["c.privateMb"] = 200, ["gone.threads"] = 30 },
            new Dictionary<string, double> { ["a.cpu"] = 1.24, ["b.cpu"] = 9, ["c.privateMb"] = 100, ["new.ms"] = 900 },
            Tolerance);
        var report = CostBaseline.Markdown(lines);

        Assert.StartsWith("| Metric | Baseline | Measured | Limit | |", report, StringComparison.Ordinal);
        Assert.Contains("| `a.cpu` | 2 | 1.2 | 7 | ok |", report, StringComparison.Ordinal);
        Assert.Contains("| `b.cpu` | 2 | 9 | 7 | **over the limit** |", report, StringComparison.Ordinal);
        Assert.Contains("well under: the baseline could come down", report, StringComparison.Ordinal);
        Assert.Contains("| `gone.threads` | 30 | - | 145 | not measured any more |", report, StringComparison.Ordinal);
        Assert.Contains("| `new.ms` | - | 900 | - | new: no baseline yet |", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ABaselineFileReadsBackAsItWasWritten()
    {
        var tolerance = new CostTolerance(CpuFactor: 3, MemoryMb: 64);
        var text = CostBaseline.Write(tolerance, new Dictionary<string, double> { ["b.cpu"] = 1.234, ["a.privateMb"] = 150.06 }, "About it: it's baseline x factor + allowance.");
        var (read, metrics) = CostBaseline.Read(text);

        Assert.Equal(tolerance, read);
        Assert.Equal(new Dictionary<string, double> { ["a.privateMb"] = 150.1, ["b.cpu"] = 1.2 }, metrics);
        Assert.True(text.IndexOf("\"a.privateMb\"", StringComparison.Ordinal) < text.IndexOf("\"b.cpu\"", StringComparison.Ordinal));
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.Contains("it's baseline x factor + allowance", text, StringComparison.Ordinal);

        var (defaults, none) = CostBaseline.Read("{}");
        Assert.Equal(new CostTolerance(), defaults);
        Assert.Empty(none);
    }

    [Fact]
    public void TheCommittedBaselineIsReadable()
    {
        var (_, metrics) = CostBaseline.Read(File.ReadAllText(CostCheck.BaselineFile));
        // Every number it holds is one the check still measures.
        var measured = CostCheck.States.SelectMany(s => new[] { ".cpu", ".workingSetMb", ".privateMb", ".handles", ".threads" }.Select(m => s + m)).Append("start.ms");
        Assert.All(metrics.Keys, metric => Assert.Contains(metric, measured));
    }
}
