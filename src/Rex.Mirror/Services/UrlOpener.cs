using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Rex.Core;

namespace Rex.Mirror.Services;

/// <summary>
/// Opens one of the app's own web pages in the default browser. Only https addresses are opened.
/// With <see cref="FakeBrowserVariable"/> set (the tests always set it) the address is written to
/// that file instead, so no test ever opens a browser.
/// </summary>
internal static class UrlOpener
{
    public const string FakeBrowserVariable = "REX_FAKE_BROWSER_LOG";

    public static bool Open(string url, RexLog log)
    {
        if (!url.StartsWith("https://", StringComparison.Ordinal))
        {
            throw new ArgumentException("Only https addresses are opened.", nameof(url));
        }

        try
        {
            if (Environment.GetEnvironmentVariable(FakeBrowserVariable) is { Length: > 0 } fake)
            {
                File.AppendAllText(fake, url + Environment.NewLine);
                return true;
            }

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            log.Warn("Could not open " + url, ex);
            return false;
        }
    }
}
