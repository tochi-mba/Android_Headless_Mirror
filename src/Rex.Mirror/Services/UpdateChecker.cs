using System.IO;
using System.Net.Http;
using Rex.Core;

namespace Rex.Mirror.Services;

/// <summary>
/// Asks GitHub, at most once a day and only while <c>App.CheckForUpdates</c> is on, which version
/// is the newest. Nothing is sent but the request itself. With <see cref="FakeReleasesVariable"/>
/// set (the tests always set it) the answer is read from that file instead, so no test reaches the
/// network; a file that is not there is no answer.
/// </summary>
internal sealed class UpdateChecker(AppHost host)
{
    public const string FakeReleasesVariable = "REX_FAKE_RELEASES";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = UpdateCheck.Timeout };
        // GitHub's API refuses a request that does not say who it is from.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AndroidHeadlessMirror/" + CommandRouter.AppVersion);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>A newer version worth offering, or null: not due, no answer, nothing newer, or skipped.</summary>
    public async Task<Version?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (!UpdateCheck.Due(host.Config.App.CheckForUpdates, host.State.Ui.LastUpdateCheck, now))
        {
            return null;
        }

        host.State.SetUi(host.State.Ui with { LastUpdateCheck = now });
        string? answer;
        try
        {
            answer = Environment.GetEnvironmentVariable(FakeReleasesVariable) is { Length: > 0 } fake
                ? File.Exists(fake) ? await File.ReadAllTextAsync(fake, cancellationToken).ConfigureAwait(true) : null
                : await Http.GetStringAsync(UpdateCheck.LatestReleaseUrl, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // Offline, or GitHub is slow: said in the log only, and tried again tomorrow.
            host.Log.Info("Could not ask GitHub for the newest version: " + ex.Message);
            return null;
        }

        var latest = UpdateCheck.Latest(answer);
        host.Log.Info($"Asked GitHub for the newest version: {latest?.ToString() ?? "no answer"}.");
        return UpdateCheck.Offer(latest, CommandRouter.AppVersion, host.State.Ui.SkippedVersion) ? latest : null;
    }
}
