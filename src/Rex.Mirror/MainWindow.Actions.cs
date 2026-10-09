using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Native;
using Rex.Mirror.Services;
using Rex.Mirror.Session;
using Rex.Mirror.Views;

namespace Rex.Mirror;

/// <summary>Running actions from every way in (buttons, keys, the pipe), and saving screenshots.</summary>
public partial class MainWindow
{
    public async Task<AndroidResult> ApplyAppActionAsync(string id)
    {
        switch (id)
        {
            case "browse":
                SetBrowse(!_browse);
                return AndroidResult.Success(_browse ? "Browse mode on" : "Browse mode off");
            case var gesture when MirrorActions.IsGesture(gesture):
                return await _keyboardTouch.RunAsync(gesture);
            case "zoom-in":
                Host.ZoomStep(1, step: _host.Config.Zoom.KeyStep);
                return AndroidResult.Success($"{Host.Zoom * 100:0}%");
            case "zoom-out":
                Host.ZoomStep(-1, step: _host.Config.Zoom.KeyStep);
                return AndroidResult.Success($"{Host.Zoom * 100:0}%");
            case "fit-window":
                return FitWindowToPhone();
            case "zoom-reset":
                Host.ResetZoom();
                return AndroidResult.Success("100%");
            case "fullscreen":
                ToggleFullscreen();
                return AndroidResult.Success(_fullscreen ? "Fullscreen" : "Windowed");
            case "screenshot":
                _ = SaveScreenshotAsync();
                return AndroidResult.Success("Saving…");
            case "second-screen":
                return await ToggleSecondScreenAsync();
            case "phone-switch":
                return SwitchPhone();
            case "phone-beside":
                return await ToggleBesideAsync();
            case "copy-add":
                return Copies.Add();
            case "copy-remove":
                return Copies.Remove();
            case "send-files":
                await SendFilesAsync();
                return AndroidResult.Success();
            case "send-copied-files":
                await SendCopiedFilesAsync();
                return AndroidResult.Success();
            case "sound-up" or "sound-down" or "sound-mute":
                return RunSound(id);
            default:
                return AndroidResult.Failure($"Unknown app action '{id}'.");
        }
    }

    public async Task RunActionAsync(string id)
    {
        await RunActionCoreAsync(id);
        // Back to whichever view was being used, the main one or a copy.
        if (ActiveView.HasChild)
        {
            ActiveView.FocusChild();
        }
    }

    /// <summary>Plays an action and shows what it did in the status bar; returns the same words.</summary>
    private async Task<(bool Ok, string Text)> RunActionCoreAsync(string id)
    {
        // While the phone beside is in use, what acts on a phone goes to it.
        if (await RunOnOtherPhoneAsync(id) is { } beside)
        {
            SetStatus(beside.Text, !beside.Ok);
            return beside;
        }

        // While the second screen has the keyboard, what acts on a display goes to its own.
        if (RunOnSecondScreen(id) is { } routed)
        {
            SetStatus(routed.Text, !routed.Ok);
            return routed;
        }

        var result = await _host.Session.RunActionAsync(id, ApplyAppActionAsync);
        NoteAction(id, result.Ok);
        var text = result.Ok ? (string.IsNullOrWhiteSpace(result.Text) ? MirrorActions.Find(id)?.Label ?? id : result.Text) : result.Text;
        SetStatus(text, !result.Ok);
        return (result.Ok, text);
    }

    private async Task<(bool Ok, string Text)> SaveScreenshotAsync(string? serial = null)
    {
        try { return await SaveScreenshotCoreAsync(serial); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _host.Log.Error("Could not save a screenshot", ex);
            SetStatus("Could not save screenshot: " + ex.Message, isError: true);
            return (false, "Could not save screenshot: " + ex.Message);
        }
    }

    private async Task<(bool Ok, string Text)> SaveScreenshotCoreAsync(string? serial)
    {
        var (ok, text) = await _host.Session.SaveScreenshotAsync(serial);
        if (ok)
        {
            AfterScreenshot(text);
        }

        var said = ok ? "Screenshot saved: " + Path.GetFileName(text) : text;
        SetStatus(said, !ok);
        if (ok)
        {
            StatusText.Inlines.Clear();
            StatusText.Inlines.Add("Screenshot saved: ");
            var link = new System.Windows.Documents.Hyperlink(
                new System.Windows.Documents.Run(Path.GetFileName(text)))
            {
                Foreground = (Brush)FindResource("Signal"),
                ToolTip = "Show in File Explorer: " + text,
            };
            link.Click += (_, _) => RevealScreenshot(text);
            StatusText.Inlines.Add(link);
        }

        return (ok, said);
    }

    private void RevealScreenshot(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                SetStatus("This screenshot has been moved or deleted.", isError: true);
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _host.Log.Error($"Could not reveal screenshot '{path}' in File Explorer", ex);
            SetStatus("Could not open File Explorer: " + ex.Message, isError: true);
        }
    }
}
