using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rex.Core;

namespace Rex.Mirror.Views;

/// <summary>
/// The settings about driving the phone: what each mouse button does, keys, the clipboard, game
/// controllers, and the app the mirror opens with. They are scrcpy options, so like the rest of
/// the launch settings they apply the next time the mirror starts and offer a restart.
/// </summary>
public partial class SettingsPanel
{
    /// <summary>What a mouse button can be set to do, in the words the choices show.</summary>
    private static readonly (string Action, string Label)[] ButtonChoices =
    [
        ("back", "Back"),
        ("home", "Home"),
        ("recents", "Recent apps"),
        ("notifications", "Notifications"),
        ("click", "Click on the phone"),
        ("nothing", "Nothing"),
    ];

    private IEnumerable<ComboBox> ButtonCombos => [RightClick, MiddleClick, BackButton, ForwardButton];

    /// <summary>Each button's choices, made once; the same list four times over in the markup would drift apart.</summary>
    private void BuildButtonChoices()
    {
        foreach (var combo in ButtonCombos)
        {
            if (combo.Items.Count > 0)
            {
                continue;
            }

            foreach (var (action, label) in ButtonChoices)
            {
                combo.Items.Add(new ComboBoxItem { Content = label, Tag = action });
            }
        }
    }

    private void RefreshInput(RexConfig c)
    {
        BuildButtonChoices();
        SelectTag(RightClick, c.Input.RightClick);
        SelectTag(MiddleClick, c.Input.MiddleClick);
        SelectTag(BackButton, c.Input.BackButton);
        SelectTag(ForwardButton, c.Input.ForwardButton);
        ShiftClicks.IsChecked = c.Input.ShiftClicks;
        KeyRepeat.IsChecked = c.Input.KeyRepeat;
        MouseHover.IsChecked = c.Input.MouseHover;
        ClipboardAutosync.IsChecked = c.Input.ClipboardAutosync;
        LegacyPaste.IsChecked = c.Input.LegacyPaste;
        Gamepad.IsChecked = c.Input.Gamepad == "uhid";

        // Someone typing a package name keeps what they have typed until they commit it.
        if (!StartApp.IsKeyboardFocusWithin)
        {
            StartApp.Text = c.Session.StartApp;
        }
    }

    private void OnInputChanged(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Input.RightClick = SelectedTag(RightClick, "back");
        c.Input.MiddleClick = SelectedTag(MiddleClick, "home");
        c.Input.BackButton = SelectedTag(BackButton, "recents");
        c.Input.ForwardButton = SelectedTag(ForwardButton, "notifications");
        c.Input.ShiftClicks = ShiftClicks.IsChecked == true;
        c.Input.KeyRepeat = KeyRepeat.IsChecked == true;
        c.Input.MouseHover = MouseHover.IsChecked == true;
        c.Input.ClipboardAutosync = ClipboardAutosync.IsChecked == true;
        c.Input.LegacyPaste = LegacyPaste.IsChecked == true;
        c.Input.Gamepad = Gamepad.IsChecked == true ? "uhid" : "disabled";
    });

    /// <summary>Keyboard focus leaving the box, by any route (a click, Tab, the tab strip, the phone), commits it.</summary>
    private void OnStartApp(object sender, KeyboardFocusChangedEventArgs e) => CommitStartApp();

    /// <summary>
    /// Typing commits on Enter or on leaving the box, so a half-typed name is never saved. A value
    /// set from outside the keyboard (a screen reader, or UI Automation) arrives whole, and with no
    /// Enter to follow it, so it applies at once.
    /// </summary>
    private void OnStartAppText(object sender, TextChangedEventArgs e)
    {
        if (!StartApp.IsKeyboardFocusWithin)
        {
            CommitStartApp();
        }
    }

    private void OnStartAppKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitStartApp();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _host is not null)
        {
            StartApp.Text = _host.Config.Session.StartApp;
            StartAppError.Text = string.Empty;
            e.Handled = true;
        }
    }

    private void CommitStartApp()
    {
        if (_host is null || _loading)
        {
            return;
        }

        var text = StartApp.Text.Trim();
        if (text.Length > 0 && !SessionSettings.IsValidStartApp(text))
        {
            StartAppError.Text = "That cannot be an app. Use its package name, or ? and the start of its name.";
            return;
        }

        StartAppError.Text = string.Empty;
        if (text != _host.Config.Session.StartApp)
        {
            Save(c => c.Session.StartApp = text);
            Status.Text = text.Length == 0 ? "No app opens with the mirror now." : $"The mirror opens {text} from its next start.";
        }
    }
}
