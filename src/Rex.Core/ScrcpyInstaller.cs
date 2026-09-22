using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rex.Core;

public sealed record ToolPaths(string Scrcpy, string Adb, string Version)
{
    public bool IsComplete => File.Exists(Scrcpy) && File.Exists(Adb);

    public static ToolPaths In(string directory, string version) =>
        new(Path.Combine(directory, "scrcpy.exe"), Path.Combine(directory, "adb.exe"), version);
}

/// <summary>
/// Finds scrcpy/adb: an explicit override, else the newest complete version folder among the
/// copies the app installed itself (root/tools/scrcpy) and the copy the installer ships next to
/// the executables.
/// </summary>
public static class ToolLocator
{
    public const string AdbOverride = "REX_ADB_PATH";
    public const string ScrcpyOverride = "REX_SCRCPY_PATH";
    public const string StagingPrefix = ".install-";

    public static ToolPaths? Find(AppPaths paths)
    {
        var adbOverride = Environment.GetEnvironmentVariable(AdbOverride);
        var scrcpyOverride = Environment.GetEnvironmentVariable(ScrcpyOverride);
        if (!string.IsNullOrWhiteSpace(adbOverride) && !string.IsNullOrWhiteSpace(scrcpyOverride)
            && File.Exists(adbOverride) && File.Exists(scrcpyOverride))
        {
            return new ToolPaths(scrcpyOverride, adbOverride, "override");
        }

        return Find([paths.ScrcpyTools, AppPaths.BundledScrcpyTools]);
    }

    /// <summary>Newest complete scrcpy folder directly under any of the given folders.</summary>
    public static ToolPaths? Find(IEnumerable<string> folders) =>
        folders.Where(Directory.Exists)
            .SelectMany(Directory.GetDirectories)
            .Where(dir => !Path.GetFileName(dir).StartsWith(StagingPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(dir => ToolPaths.In(dir, VersionOf(Path.GetFileName(dir))))
            .Where(x => x.IsComplete)
            .OrderByDescending(x => VersionKey(x.Version))
            .FirstOrDefault();

    /// <summary>"scrcpy-win64-v4.1" and "v4.1-1a2b3c4d" both read as "v4.1".</summary>
    public static string VersionOf(string folderName)
    {
        var match = Regex.Match(folderName, @"v?\d+(?:\.\d+)+");
        return match.Success ? match.Value : folderName;
    }

    private static Version VersionKey(string version)
    {
        var match = Regex.Match(version, @"(\d+)\.(\d+)(?:\.(\d+))?");
        return match.Success
            ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0)
            : new Version(0, 0);
    }
}

public sealed record InstallProgress(string Stage, double Fraction);

/// <summary>
/// Downloads the official Windows x64 scrcpy release from GitHub, verifies its SHA-256 against
/// the release's SHA256SUMS.txt, and installs it as tools/scrcpy/&lt;tag&gt;/. The verified copy is
/// staged in a folder discovery ignores and only becomes visible once the move completes.
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

            var tools = InstallVerifiedDirectory(paths, source, release.Tag);

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
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApi);
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            // CI runners share public IP addresses and hit the anonymous API rate limit.
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
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

    internal static ToolPaths InstallVerifiedDirectory(AppPaths paths, string source, string tag)
    {
        if (!ToolPaths.In(source, tag).IsComplete)
        {
            throw new InvalidOperationException("The verified scrcpy folder must contain scrcpy.exe and adb.exe.");
        }

        Directory.CreateDirectory(paths.ScrcpyTools);
        var target = Path.Combine(paths.ScrcpyTools, Regex.Replace(tag, @"[^A-Za-z0-9._-]", "_"));
        var existing = ToolPaths.In(target, tag);
        if (existing.IsComplete)
        {
            return existing;
        }

        // A leftover partial folder is never deleted: Windows refuses while its adb.exe is still
        // running. Install beside it instead; discovery reads the version from the name either way.
        if (Directory.Exists(target))
        {
            target += "-" + Guid.NewGuid().ToString("N")[..8];
        }

        var staging = Path.Combine(paths.ScrcpyTools, ToolLocator.StagingPrefix + Guid.NewGuid().ToString("N"));
        CopyDirectory(source, staging);
        Directory.Move(staging, target);
        return ToolPaths.In(target, tag);
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
