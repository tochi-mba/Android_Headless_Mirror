using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rex.Core;

public sealed record ToolPaths(string Scrcpy, string Adb, string Version)
{
    public bool IsComplete => File.Exists(Scrcpy) && File.Exists(Adb);
}

/// <summary>Finds the bundled scrcpy/adb (tools/scrcpy/&lt;version&gt;/) or an explicit override.</summary>
public static class ToolLocator
{
    public const string AdbOverride = "REX_ADB_PATH";
    public const string ScrcpyOverride = "REX_SCRCPY_PATH";

    public static ToolPaths? Find(AppPaths paths)
    {
        var adbOverride = Environment.GetEnvironmentVariable(AdbOverride);
        var scrcpyOverride = Environment.GetEnvironmentVariable(ScrcpyOverride);
        if (!string.IsNullOrWhiteSpace(adbOverride) && !string.IsNullOrWhiteSpace(scrcpyOverride)
            && File.Exists(adbOverride) && File.Exists(scrcpyOverride))
        {
            return new ToolPaths(scrcpyOverride, adbOverride, "override");
        }

        if (!Directory.Exists(paths.ScrcpyTools))
        {
            return null;
        }

        var candidates = Directory.GetDirectories(paths.ScrcpyTools)
            .Where(dir => !Path.GetFileName(dir).StartsWith(".install-", StringComparison.OrdinalIgnoreCase))
            .Select(FromDirectory)
            .Where(x => x.IsComplete)
            .OrderByDescending(x => VersionKey(x.Version))
            .ToArray();

        return candidates.FirstOrDefault();
    }

    private static ToolPaths FromDirectory(string directory)
    {
        var version = Path.GetFileName(directory);
        var marker = Path.Combine(directory, ".rex-version");
        try
        {
            if (File.Exists(marker))
            {
                var marked = File.ReadAllText(marker).Trim();
                if (!string.IsNullOrWhiteSpace(marked))
                {
                    version = marked;
                }
            }
        }
        catch (IOException)
        {
            // A version marker is display metadata only; tool discovery must still work without it.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }

        return new ToolPaths(Path.Combine(directory, "scrcpy.exe"), Path.Combine(directory, "adb.exe"), version);
    }

    private static Version VersionKey(string folder)
    {
        var match = Regex.Match(folder, @"(\d+)\.(\d+)(?:\.(\d+))?");
        return match.Success
            ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0)
            : new Version(0, 0);
    }
}

public sealed record InstallProgress(string Stage, double Fraction);

/// <summary>
/// Downloads the official Windows x64 scrcpy release from GitHub, verifies its SHA-256 against
/// the release's SHA256SUMS.txt, and installs it into an immutable content-addressed folder under
/// tools/scrcpy/. A verified copy is staged first and only becomes discoverable after the move completes.
/// </summary>
public sealed class ScrcpyInstaller
{
    private const string ReleaseApi = "https://api.github.com/repos/Genymobile/scrcpy/releases/latest";

    private readonly HttpClient _http;

    public ScrcpyInstaller(HttpClient? http = null)
    {
        _http = http ?? CreateClient();
    }

    public static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Android-Headless-Mirror", "2"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public async Task<ToolPaths> InstallLatestAsync(AppPaths paths, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new InstallProgress("Checking the latest scrcpy release", 0.05));
        var release = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);

        var temp = Path.Combine(Path.GetTempPath(), "rex-scrcpy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var zipPath = Path.Combine(temp, release.AssetName);
            progress?.Report(new InstallProgress($"Downloading {release.AssetName}", 0.1));
            await DownloadAsync(release.AssetUrl, zipPath, progress, 0.1, 0.8, cancellationToken).ConfigureAwait(false);

            progress?.Report(new InstallProgress("Verifying SHA-256 checksum", 0.82));
            var sums = await _http.GetStringAsync(release.ChecksumUrl, cancellationToken).ConfigureAwait(false);
            var expected = ParseChecksum(sums, release.AssetName)
                ?? throw new InvalidOperationException($"SHA256SUMS.txt has no entry for {release.AssetName}.");

            var actual = await ComputeSha256Async(zipPath, cancellationToken).ConfigureAwait(false);
            if (!expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Checksum mismatch for {release.AssetName}: expected {expected}, got {actual}. Nothing was installed.");
            }

            progress?.Report(new InstallProgress("Extracting", 0.9));
            var extract = Path.Combine(temp, "extract");
            ZipFile.ExtractToDirectory(zipPath, extract);

            var scrcpy = Directory.GetFiles(extract, "scrcpy.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidOperationException("The archive did not contain scrcpy.exe.");
            var source = Path.GetDirectoryName(scrcpy)!;
            if (!File.Exists(Path.Combine(source, "adb.exe")))
            {
                throw new InvalidOperationException("The archive did not contain adb.exe next to scrcpy.exe.");
            }

            var tools = InstallVerifiedDirectory(paths, source, release.Tag, actual);

            progress?.Report(new InstallProgress("Installed scrcpy " + release.Tag, 1.0));
            return tools;
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException)
            {
                // Temp cleanup is best effort.
            }
            catch (UnauthorizedAccessException)
            {
                // Temp cleanup is best effort.
            }
        }
    }

    internal sealed record ReleaseInfo(string Tag, string AssetName, string AssetUrl, string ChecksumUrl);

    internal async Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(ReleaseApi, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return ParseRelease(json.RootElement);
    }

    internal static ReleaseInfo ParseRelease(JsonElement root)
    {
        var tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidOperationException("Release has no tag.");
        string? assetName = null, assetUrl = null, checksumUrl = null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            var url = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
            if (Regex.IsMatch(name, @"^scrcpy-win64-v[0-9].*\.zip$"))
            {
                assetName = name;
                assetUrl = url;
            }
            else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
            {
                checksumUrl = url;
            }
        }

        if (assetName is null || assetUrl is null)
        {
            throw new InvalidOperationException("The latest scrcpy release has no Windows x64 archive.");
        }

        if (checksumUrl is null)
        {
            throw new InvalidOperationException("The latest scrcpy release has no SHA256SUMS.txt, so it cannot be verified.");
        }

        return new ReleaseInfo(tag, assetName, assetUrl, checksumUrl);
    }

    internal static string? ParseChecksum(string sums, string assetName)
    {
        foreach (var line in sums.Split('\n'))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[^1].TrimStart('*').Equals(assetName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0].ToLowerInvariant();
            }
        }

        return null;
    }

    internal static ToolPaths InstallVerifiedDirectory(AppPaths paths, string source, string tag, string sha256)
    {
        var sourceScrcpy = Path.Combine(source, "scrcpy.exe");
        var sourceAdb = Path.Combine(source, "adb.exe");
        if (!File.Exists(sourceScrcpy) || !File.Exists(sourceAdb))
        {
            throw new InvalidOperationException("The verified scrcpy folder must contain scrcpy.exe and adb.exe.");
        }

        if (!Regex.IsMatch(sha256, "^[0-9a-fA-F]{64}$"))
        {
            throw new ArgumentException("A full SHA-256 digest is required.", nameof(sha256));
        }

        Directory.CreateDirectory(paths.ScrcpyTools);
        var safeTag = Regex.Replace(tag, @"[^A-Za-z0-9._-]", "_").Trim('.', ' ');
        if (safeTag.Length == 0)
        {
            safeTag = "scrcpy";
        }

        var canonicalName = $"{safeTag}-{sha256[..12].ToLowerInvariant()}";
        var canonical = Path.Combine(paths.ScrcpyTools, canonicalName);
        var existing = new ToolPaths(Path.Combine(canonical, "scrcpy.exe"), Path.Combine(canonical, "adb.exe"), tag);
        if (existing.IsComplete)
        {
            return existing;
        }

        // Never delete an existing install here. adb.exe may still be running and Windows will
        // deny deletion of its directory. A partial canonical folder gets a unique sibling instead.
        var target = Directory.Exists(canonical)
            ? canonical + "-" + Guid.NewGuid().ToString("N")[..8]
            : canonical;
        var staging = Path.Combine(paths.ScrcpyTools, ".install-" + Guid.NewGuid().ToString("N"));

        try
        {
            CopyDirectory(source, staging);
            File.WriteAllText(Path.Combine(staging, ".rex-version"), tag);

            var staged = new ToolPaths(Path.Combine(staging, "scrcpy.exe"), Path.Combine(staging, "adb.exe"), tag);
            if (!staged.IsComplete)
            {
                throw new InvalidOperationException("The staged scrcpy installation is incomplete.");
            }

            Directory.Move(staging, target);
            return new ToolPaths(Path.Combine(target, "scrcpy.exe"), Path.Combine(target, "adb.exe"), tag);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort. A later run ignores .install-* folders.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }

    private async Task DownloadAsync(string url, string path, IProgress<InstallProgress>? progress, double from, double to, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = File.Create(path);

        var buffer = new byte[81920];
        long read = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            read += count;
            if (total > 0)
            {
                progress?.Report(new InstallProgress("Downloading scrcpy", from + (to - from) * read / total));
            }
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }
}
