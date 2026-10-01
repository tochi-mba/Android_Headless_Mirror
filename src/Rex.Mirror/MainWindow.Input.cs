using System.Windows;
using System.Windows.Controls;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Native;
using Rex.Mirror.Views.Controls;

namespace Rex.Mirror;

/// <summary>
/// Everything the window does with keys, the wheel and raw pointer messages before the phone sees
/// them: the app's own shortcuts, browse mode, Alt + wheel zoom and the two-finger bridge. The
/// hooks that call in here run on the UI thread and only classify and enqueue.
/// </summary>
public partial class MainWindow
{
    /// <summary>Turns browse mode on or off: plain keys swipe and tap the phone while it is on.</summary>
    public void SetBrowse(bool on)
    {
        if (_browse == on)
        {
            return;
        }

        _browse = on;
        if (on)
        {
            ShowTipSoon(Tips.FirstBrowse);
        }

        // The session refresh rewrites the status line from the session, so it goes first.
        OnSessionChanged();
        SetStatus(on ? "Browse mode on: plain keys drive the phone. Esc leaves." : "Browse mode off: keys type into the phone again.");
        if (Host.HasChild)
        {
            Host.FocusChild();
        }
    }

    /// <summary>
    /// Whether keyboard focus sits in one of the app's own controls, where a plain key belongs to
    /// that control rather than to browse mode. Focus in the mirror itself reads as nothing.
    /// </summary>
    private static bool FocusInsideControl() =>
        System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
            or PasswordBox or ComboBox or System.Windows.Controls.Primitives.ButtonBase
            or System.Windows.Controls.Primitives.RangeBase or System.Windows.Controls.Primitives.Selector;

    private bool OnPanelWheel(int delta, int screenX, int screenY)
    {
        if (!SidebarScroll.IsVisible) return false;
        if (!_sidebarWheelBounds.Contains(screenX, screenY))
            return false;
        // scrcpy can retain native keyboard focus even when the pointer is over WPF.
        // Consume this event before Windows delivers it to the focused child HWND.
        Dispatcher.BeginInvoke(() =>
        {
            var distance = SystemParameters.WheelScrollLines < 0
                ? SidebarScroll.ViewportHeight : SystemParameters.WheelScrollLines * 16;
            SidebarScroll.ScrollToVerticalOffset(SidebarScroll.VerticalOffset - delta / 120.0 * distance);
        });
        return true;
    }

    private bool OnAltWheel(int delta, int screenX, int screenY)
    {
        if (!_host.Config.Zoom.Enabled || !_host.Config.Zoom.WheelZoom || !Host.HasChild)
        {
            return false;
        }

        // Over a copy, the zoom is anchored at the same spot of the main view: every copy follows
        // the main view, so all of them zoom in on the same place.
        if (OnMainView(screenX, screenY) is not { } onMain)
        {
            return false;
        }

        var viewport = Host.ViewportScreenRect;
        var zoom = _host.Config.Zoom;
        var direction = (delta > 0) != zoom.InvertWheel ? 1 : -1;

        // On the pointer, or on the middle of what is showing.
        var anchorX = zoom.ZoomAtPointer ? onMain.X - viewport.Left : viewport.Width / 2;
        var anchorY = zoom.ZoomAtPointer ? onMain.Y - viewport.Top : viewport.Height / 2;
        var step = _host.Config.Zoom.WheelStep;
        Dispatcher.BeginInvoke(() => Host.ZoomStep(direction, anchorX, anchorY, step));
        return true;
    }

    private bool OnHotkey(int virtualKey, KeyMods mods)
    {
        // The key being recorded in a shortcut box is that box's alone.
        if (ChordBox.AnyRecording)
        {
            return false;
        }

        var ctrl = mods.HasFlag(KeyMods.Ctrl);
        var alt = mods.HasFlag(KeyMods.Alt);
        var shift = mods.HasFlag(KeyMods.Shift);
        var guide = _guide;
        if (guide is { IsCalibrating: true } && !alt && guide.CanHandleCalibrationKey(virtualKey))
        {
            var plainCalibrationKey = !ctrl;
            var fineArrowKey = ctrl && virtualKey is NativeMethods.VK_LEFT or NativeMethods.VK_UP or NativeMethods.VK_RIGHT or NativeMethods.VK_DOWN;
            if (plainCalibrationKey || fineArrowKey)
            {
                Dispatcher.BeginInvoke(() => guide.HandleCalibrationKey(virtualKey, ctrl, shift));
                return true;
            }
        }

        if (_browse && !ctrl && !alt)
        {
            if (virtualKey == NativeMethods.VK_ESCAPE)
            {
                Dispatcher.BeginInvoke(() => SetBrowse(false));
                return true;
            }

            if (KeyboardBrowse.ActionFor(virtualKey) is { } browseAction &&
                KeyboardBrowse.Applies(_browse, ctrl, alt, FocusInsideControl()))
            {
                Dispatcher.BeginInvoke(() => _ = RunActionAsync(browseAction));
                return true;
            }
        }

        return WindowKeys.ActionFor(virtualKey, mods) is { } id && RunWindowKey(id);
    }

    /// <summary>Queues what a key of the window's own means; false when it means nothing right now.</summary>
    private bool RunWindowKey(string id)
    {
        Action? work = id switch
        {
            "fullscreen" => ToggleFullscreen,
            "fullscreen-exit" => _fullscreen ? ToggleFullscreen : null,
            "tour" => StartTour,
            "sidebar" => () => SetSidebarVisible(!_sidebarWanted),
            "pattern-guide" => () => _guide?.Toggle(),
            "pattern-calibrate" => () => _guide?.StartCalibration(),
            _ when id.StartsWith("tab-", StringComparison.Ordinal) => () =>
            {
                if (!_sidebarWanted)
                {
                    SetSidebarVisible(true);
                }

                SelectTab(id["tab-".Length..]);
            },
            _ => () => _ = RunActionAsync(id),
        };
        if (work is null)
        {
            return false;
        }

        Dispatcher.BeginInvoke(work);
        return true;
    }

    private bool OnOverlayPointer(int msg, IntPtr wParam) => _touchpad.HandlePointerMessage(msg, wParam);

    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is NativeMethods.WM_POINTERDOWN or NativeMethods.WM_POINTERUPDATE or NativeMethods.WM_POINTERUP or NativeMethods.WM_POINTERCAPTURECHANGED)
        {
            if (_touchpad.HandlePointerMessage(msg, wParam))
            {
                handled = true;
            }
        }

        return IntPtr.Zero;
    }
}
