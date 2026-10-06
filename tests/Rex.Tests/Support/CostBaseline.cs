using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rex.Tests.Support;

/// <summary>
/// How far a measurement may rise above its baseline before it counts as a regression: a factor
/// of the baseline plus a fixed allowance, per kind of number, so a cheap state's noise and an
/// expensive state's drift are both tolerated without hiding a real jump.
/// </summary>
public sealed record CostTolerance(
    double CpuFactor = 2,
    double CpuPoints = 3,
    double MemoryFactor = 1.35,
    double MemoryMb = 40,
    double TimeFactor = 2,
    double TimeMs = 3000,
    double CountFactor = 1.5,
    double CountExtra = 100);

/// <summary>One number compared: its baseline, what was measured, the limit, and what that means.</summary>
public sealed record CostLine(string Metric, double? Baseline, double? Measured, double? Limit, CostVerdict Verdict);

public enum CostVerdict
{
    /// <summary>Within the limit.</summary>
    Ok,

    /// <summary>Above the limit: a regression.</summary>
    Over,

    /// <summary>Well under the baseline: the baseline could come down.</summary>
    Lower,

    /// <summary>Measured, with no baseline yet.</summary>
    New,

    /// <summary>In the baseline, not measured any more.</summary>
    Missing,
}

/// <summary>
/// The app's committed cost baseline (tests/perf-baseline.json) and the comparison against a run.
/// Metric names end in their kind: <c>.cpu</c> (percent of one core), <c>Mb</c> (megabytes),
/// <c>.ms</c> (milliseconds); anything else is a count, like handles or threads.
/// </summary>
public static class CostBaseline
{
    public enum Kind { Cpu, Memory, Time, Count }

    public static Kind KindOf(string metric) =>
        metric.EndsWith(".cpu", StringComparison.Ordinal) ? Kind.Cpu
        : metric.EndsWith("Mb", StringComparison.Ordinal) ? Kind.Memory
        : metric.EndsWith(".ms", StringComparison.Ordinal) ? Kind.Time
        : Kind.Count;

    public static double Limit(double baseline, Kind kind, CostTolerance t) => kind switch
    {
        Kind.Cpu => baseline * t.CpuFactor + t.CpuPoints,
        Kind.Memory => baseline * t.MemoryFactor + t.MemoryMb,
        Kind.Time => baseline * t.TimeFactor + t.TimeMs,
        _ => baseline * t.CountFactor + t.CountExtra,
    };

    /// <summary>Every metric of either side, in name order.</summary>
    public static IReadOnlyList<CostLine> Compare(IReadOnlyDictionary<string, double> baseline, IReadOnlyDictionary<string, double> measured, CostTolerance t) =>
        baseline.Keys.Union(measured.Keys).Order(StringComparer.Ordinal).Select(metric =>
        {
            var had = baseline.TryGetValue(metric, out var b);
            var has = measured.TryGetValue(metric, out var m);
            if (!had)
            {
                return new CostLine(metric, null, m, null, CostVerdict.New);
            }

            var limit = Limit(b, KindOf(metric), t);
            if (!has)
            {
                return new CostLine(metric, b, null, limit, CostVerdict.Missing);
            }

            // "Lower" is under 60 % of the baseline by more than the fixed allowance's worth of noise.
            var allowance = limit - b * Factor(KindOf(metric), t);
            var verdict = m > limit ? CostVerdict.Over
                : m < b * 0.6 && b - m > allowance ? CostVerdict.Lower
                : CostVerdict.Ok;
            return new CostLine(metric, b, m, limit, verdict);
        }).ToArray();

    private static double Factor(Kind kind, CostTolerance t) => kind switch
    {
        Kind.Cpu => t.CpuFactor,
        Kind.Memory => t.MemoryFactor,
        Kind.Time => t.TimeFactor,
        _ => t.CountFactor,
    };

    /// <summary>The comparison as a Markdown table, for the CI job's summary and the uploaded report.</summary>
    public static string Markdown(IReadOnlyList<CostLine> lines)
    {
        static string N(double? value) => value is { } v ? v.ToString("0.#", CultureInfo.InvariantCulture) : "-";
        var text = new StringBuilder()
            .AppendLine("| Metric | Baseline | Measured | Limit | |")
            .AppendLine("|---|---:|---:|---:|---|");
        foreach (var line in lines)
        {
            var verdict = line.Verdict switch
            {
                CostVerdict.Over => "**over the limit**",
                CostVerdict.Lower => "well under: the baseline could come down",
                CostVerdict.New => "new: no baseline yet",
                CostVerdict.Missing => "not measured any more",
                _ => "ok",
            };
            text.AppendLine(CultureInfo.InvariantCulture, $"| `{line.Metric}` | {N(line.Baseline)} | {N(line.Measured)} | {N(line.Limit)} | {verdict} |");
        }

        return text.ToString();
    }

    /// <summary>A baseline file: what it is, its tolerance, and its numbers, ready to be committed as it is.</summary>
    public static string Write(CostTolerance tolerance, IReadOnlyDictionary<string, double> metrics, string about)
    {
        var numbers = new JsonObject();
        foreach (var (metric, value) in metrics.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            numbers[metric] = Math.Round(value, 1);
        }

        var file = new JsonObject
        {
            ["about"] = about,
            ["tolerance"] = JsonSerializer.SerializeToNode(tolerance),
            ["metrics"] = numbers,
        };
        return file.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
    }

    /// <summary>Reads a baseline file; a missing tolerance is the default, and missing numbers are none.</summary>
    public static (CostTolerance Tolerance, Dictionary<string, double> Metrics) Read(string json)
    {
        var file = JsonNode.Parse(json)!.AsObject();
        var tolerance = file["tolerance"]?.Deserialize<CostTolerance>() ?? new CostTolerance();
        var metrics = (file["metrics"]?.AsObject() ?? [])
            .ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<double>(), StringComparer.Ordinal);
        return (tolerance, metrics);
    }
}
