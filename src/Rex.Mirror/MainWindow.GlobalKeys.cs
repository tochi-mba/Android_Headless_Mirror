using System.Windows;
using Rex.Core;
using Rex.Mirror.Native;
using Rex.Mirror.Views.Controls;

namespace Rex.Mirror;

/// <summary>
/// Keys from anywhere: the key that shows and hides the window, and actions with keys of their own.
/// They are matched in the keyboard hook, which sees every key whatever is in front; the hook only
/// looks the key up and queues the work, and everything else happens here on the dispatcher.
/// </summary>
public partial class MainWindow
{
    private Dictionary<KeyChord, string> _globalKeys = [];

    /// <summary>Which step last put the window in front ("set", "attached", "switched" or "refused").</summary>
    internal string LastForegroundStep { get; private set; } = string.Empty;

    internal bool KeyboardHookInstalled => _hooks.KeyboardInstalled;

    internal bool InFront => _source is not null && ForegroundHelper.InFront(_source.Handle);

    /// <summary>
    /// Rebuilds the table the hook looks keys up in. The hook is put in place as soon as there is a
    /// key to listen for, even while the window has never been shown (a start in the tray).
    /// </summary>
    private void ApplyGlobalKeys()
    {
        var settings = _host.Config.GlobalKeys;
        var keys = new Dictionary<KeyChord, string>();
        if (settings.Enabled)
        {
            foreach (var (chord, id) in settings.Keys())
            {
                keys.TryAdd(chord, id);
            }
        }

        _globalKeys = keys;
        InfoPanel.ShowKeys(_host.Config);
        if (keys.Count > 0)
        {
            _hooks.Install();
        }
    }

    /// <summary>Called inside the keyboard hook: a key for the shortcut box that is recording, queued to it.</summary>
    private bool OnRecordKey(int key, bool down)
    {
        if (ChordBox.Recording is not { } box || !ChordBox.Takes(key))
        {
            return false;
        }

        Dispatcher.BeginInvoke(() => box.FromHook(key, down));
        return true;
    }

    /// <summary>Called inside the keyboard hook: a lookup, and the work queued. Nothing else.</summary>
    private bool OnGlobalKey(int key, KeyMods mods)
    {
        if (ChordBox.AnyRecording || !_globalKeys.TryGetValue(new KeyChord(mods, key), out var id))
        {
            return false;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (id == GlobalKeyRules.ShowHide)
            {
                RunShowHide();
            }
            else
            {
                _ = RunGlobalActionAsync(id);
            }
        });
        return true;
    }

    private void RunShowHide()
    {
        if (_quitting)
        {
            return;
        }

        var settings = _host.Config.GlobalKeys;
        var now = new WindowNow(IsVisible, WindowState == WindowState.Minimized, InFront, _fullscreen);
        var step = GlobalKeyPolicy.ForShowHide(now, settings);
        if (GlobalKeyPolicy.LeavesFullscreenFirst(now, step))
        {
            ToggleFullscreen();
        }

        switch (step)
        {
            case GlobalStep.Show or GlobalStep.ShowFullscreen or GlobalStep.BringToFront:
                ShowFromTray();
                LastForegroundStep = _source is null ? "refused" : ForegroundHelper.Bring(_source.Handle);
                if (step == GlobalStep.ShowFullscreen && !_fullscreen)
                {
                    ToggleFullscreen();
                }

                if (settings.TypeIntoPhone && ActiveView.HasChild)
                {
                    ActiveView.FocusChild();
                }

                break;
            case GlobalStep.HideToTray:
                SavePlacement();
                HideToTray();
                break;
            case GlobalStep.Minimise:
                WindowState = WindowState.Minimized;
                break;
        }

        _host.Log.Info($"Show or hide key: {step} (visible={now.Visible}, minimised={now.Minimized}, in front={now.InFront}, fullscreen={now.Fullscreen}); foreground step {LastForegroundStep}.");
    }

    /// <summary>Plays an action from a key pressed anywhere, and says what it did when the window is out of sight.</summary>
    private async Task RunGlobalActionAsync(string id)
    {
        var (ok, text) = id == "screenshot" ? await SaveScreenshotAsync() : await RunActionCoreAsync(id);
        var label = MirrorActions.Find(id)?.Label ?? id;
        _host.Log.Info($"Key from anywhere: {id} ({(ok ? "done" : "failed")}: {text}).");
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            if (InFront && ActiveView.HasChild)
            {
                ActiveView.FocusChild();
            }

            return;
        }

        if (_host.Config.GlobalKeys.Announce)
        {
            Notify(label, text);
        }
    }
}
