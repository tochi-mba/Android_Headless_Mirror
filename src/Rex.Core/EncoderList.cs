using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rex.Core;

/// <summary>One of the phone's video encoders, as scrcpy lists it.</summary>
/// <param name="Codec">The codec it encodes: h264, h265 or av1.</param>
/// <param name="Name">Its name, as --video-encoder takes it.</param>
/// <param name="Kind">"hw" for the phone's hardware, "sw" for software, "hybrid", or empty when scrcpy did not say.</param>
public sealed record VideoEncoder(string Codec, string Name, string Kind)
{
    /// <summary>How the list shows it: its name and, where scrcpy says, whether it is hardware.</summary>
    public string Label => Kind switch
    {
        "hw" => Name + " (hardware)",
        "sw" => Name + " (software)",
        "hybrid" => Name + " (hybrid)",
        _ => Name,
    };
}

/// <summary>What reading a phone's encoders gave: the encoders, or why there are none.</summary>
public sealed record EncodersRead(bool Ok, IReadOnlyList<VideoEncoder> Encoders, string Error)
{
    public static EncodersRead Failed(string error) => new(false, [], error);
}

/// <summary>
/// Reads a phone's video encoders with scrcpy itself (<c>scrcpy --list-encoders</c>), with
/// <c>--no-cleanup</c> like the app list, so it can never put back what a running mirror changed.
/// </summary>
public static partial class EncoderList
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static IReadOnlyList<string> Arguments(string serial) => ["--serial=" + serial, "--list-encoders", "--no-cleanup"];

    [GeneratedRegex(@"--video-codec=(?<codec>[a-z0-9]+)\s+--video-encoder=(?<name>\S+)(?<rest>.*)$")]
    private static partial Regex EncoderLine();

    [GeneratedRegex(@"^\s*\((?<kind>hw|sw|hybrid)\)")]
    private static partial Regex KindMark();

    /// <summary>The video encoders in scrcpy's listing, each once, in the order the phone gave them.</summary>
    public static IReadOnlyList<VideoEncoder> Parse(IEnumerable<string> lines)
    {
        var encoders = new List<VideoEncoder>();
        foreach (var line in lines)
        {
            var match = EncoderLine().Match(line);
            if (!match.Success || !MirrorSettings.IsValidEncoderName(match.Groups["name"].Value))
            {
                continue;
            }

            var kind = KindMark().Match(match.Groups["rest"].Value) is { Success: true } mark ? mark.Groups["kind"].Value : string.Empty;
            var encoder = new VideoEncoder(match.Groups["codec"].Value, match.Groups["name"].Value, kind);
            if (!encoders.Any(e => e.Codec == encoder.Codec && e.Name == encoder.Name))
            {
                encoders.Add(encoder);
            }
        }

        return encoders;
    }

    /// <summary>The encoders that can make <paramref name="codec"/>.</summary>
    public static IReadOnlyList<VideoEncoder> For(IEnumerable<VideoEncoder> encoders, string codec) =>
        encoders.Where(e => string.Equals(e.Codec, codec, StringComparison.OrdinalIgnoreCase)).ToArray();

    /// <summary>
    /// The encoder to keep once the codec changes: the same one when the phone is known to make
    /// the new codec with it, else none (an encoder for another codec stops scrcpy from starting).
    /// </summary>
    public static string KeepFor(string encoder, string codec, IEnumerable<VideoEncoder>? known) =>
        encoder.Length > 0 && known is not null && For(known, codec).Any(e => e.Name == encoder) ? encoder : string.Empty;

    /// <summary>The phone's encoders with the codec and encoder chosen now, as the pipe and the command line give them.</summary>
    public static JsonObject ToJson(string serial, MirrorSettings mirror, IEnumerable<VideoEncoder> encoders) => new()
    {
        ["serial"] = serial,
        ["codec"] = mirror.VideoCodec,
        ["chosen"] = mirror.VideoEncoder,
        ["encoders"] = new JsonArray([.. encoders.Select(e => (JsonNode)new JsonObject
        {
            ["codec"] = e.Codec,
            ["name"] = e.Name,
            ["kind"] = e.Kind,
        })]),
    };

    public static async Task<EncodersRead> ReadAsync(IProcessRunner runner, string scrcpyPath, string serial, CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync(scrcpyPath, Arguments(serial), Timeout, cancellationToken).ConfigureAwait(false);
        var encoders = Parse((result.StdOut + "\n" + result.StdErr).Split('\n'));
        if (encoders.Count > 0)
        {
            return new EncodersRead(true, encoders, string.Empty);
        }

        return EncodersRead.Failed(
            result.TimedOut ? "The phone took too long to list its encoders." :
            result.Ok ? "The phone listed no video encoders." :
            AppLister.Why(result));
    }
}
