using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rex.Core;

namespace Rex.Mirror.Views.Controls;

/// <summary>
/// A box that records a keyboard shortcut. Focus it and press the keys: it shows the chord while
/// it is held and keeps it when the key comes up. Backspace or Delete turn the shortcut off, Esc
/// puts back what was there, and Tab moves on. While a box has the keyboard, the window's keyboard
/// hook hands it every other key first (<see cref="FromHook"/>), before Windows gives a key another
/// app has claimed to that app, and no shortcut of the app acts, so the key being recorded never
/// does something else.
/// </summary>
public sealed class ChordBox : TextBox
{
    private readonly ChordRecorder _recorder = new();
    private KeyMods _hookMods;
    private string _chord = string.Empty;
    private string _before = string.Empty;

    public ChordBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        InputMethod.SetIsInputMethodEnabled(this, false);
        SetResourceReference(StyleProperty, typeof(TextBox));
        Show();
    }

    /// <summary>The shortcut box that has the keyboard, if one does.</summary>
    public static ChordBox? Recording { get; private set; }

    /// <summary>True while a shortcut box has the keyboard.</summary>
    public static bool AnyRecording => Recording is not null;

    /// <summary>A chord was chosen: its text, or empty when the shortcut was turned off.</summary>
    public event Action<ChordBox, string>? Committed;

    /// <summary>The chosen chord as written, or empty for off.</summary>
    public string Chord
    {
        get => _chord;
        set
        {
            _chord = value ?? string.Empty;
            Show();
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Recording = this;
        _hookMods = KeyMods.None;
        _before = _chord;
        _recorder.Start();
        Show();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (Recording == this)
        {
            Recording = null;
        }

        Show();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e) => e.Handled = Down(VirtualKey(e), Mods());

    protected override void OnPreviewKeyUp(KeyEventArgs e) => e.Handled = Up(VirtualKey(e), Mods());

    /// <summary>
    /// A key the keyboard hook took for this box. The hook keeps it from Windows altogether, so the
    /// modifiers are counted here rather than read from the keyboard's state.
    /// </summary>
    public void FromHook(int key, bool down)
    {
        var modifier = ModifierOf(key);
        _hookMods = down ? _hookMods | modifier : _hookMods & ~modifier;
        _ = down ? Down(key, _hookMods) : Up(key, _hookMods);
    }

    /// <summary>Whether the box takes this key while it records: every key but Tab on its own.</summary>
    public static bool Takes(int key) => key != KeyChord.Tab;

    private bool Down(int key, KeyMods mods)
    {
        if (!_recorder.Press(key, mods))
        {
            return false;
        }

        switch (_recorder.State)
        {
            case ChordState.Cleared:
                Commit(string.Empty);
                break;
            case ChordState.Cancelled:
                _chord = _before;
                _recorder.Start();
                break;
        }

        Show();
        return true;
    }

    private bool Up(int key, KeyMods mods)
    {
        _recorder.Release(key, mods);
        var done = _recorder.State == ChordState.Done;
        if (done)
        {
            Commit(_recorder.Chord!.Value.ToString());
        }

        Show();
        return done;
    }

    private static KeyMods ModifierOf(int key) => key switch
    {
        0x11 or 0xA2 or 0xA3 => KeyMods.Ctrl,
        0x12 or 0xA4 or 0xA5 => KeyMods.Alt,
        0x10 or 0xA0 or 0xA1 => KeyMods.Shift,
        0x5B or 0x5C => KeyMods.Win,
        _ => KeyMods.None,
    };

    private void Commit(string chord)
    {
        _chord = chord;
        _recorder.Start();
        Committed?.Invoke(this, chord);
        _before = _chord;
    }

    /// <summary>What the box shows: the chord being pressed, the one chosen, or what to do.</summary>
    private void Show()
    {
        var focused = IsKeyboardFocused;
        var (text, placeholder) = _recorder.State switch
        {
            ChordState.Holding when focused => (_recorder.Chord!.Value.ToString(), false),
            ChordState.Waiting when focused && _recorder.Held != KeyMods.None => (Words(_recorder.Held) + "+", true),
            _ when _chord.Length > 0 => (_chord, false),
            _ => (focused ? "Press the keys" : "Off", true),
        };
        Text = text;
        SetResourceReference(ForegroundProperty, placeholder ? "Muted" : "Text");
        System.Windows.Automation.AutomationProperties.SetHelpText(this, _chord.Length > 0 ? _chord : "Off");
    }

    private static string Words(KeyMods mods) =>
        string.Join('+', new[] { (KeyMods.Ctrl, "Ctrl"), (KeyMods.Alt, "Alt"), (KeyMods.Shift, "Shift"), (KeyMods.Win, "Win") }
            .Where(m => mods.HasFlag(m.Item1)).Select(m => m.Item2));

    private static int VirtualKey(KeyEventArgs e) => KeyInterop.VirtualKeyFromKey(e.Key switch
    {
        Key.System => e.SystemKey,
        Key.ImeProcessed => e.ImeProcessedKey,
        _ => e.Key,
    });

    private static KeyMods Mods()
    {
        var held = Keyboard.Modifiers;
        return (held.HasFlag(ModifierKeys.Control) ? KeyMods.Ctrl : 0) | (held.HasFlag(ModifierKeys.Alt) ? KeyMods.Alt : 0) |
            (held.HasFlag(ModifierKeys.Shift) ? KeyMods.Shift : 0) | (held.HasFlag(ModifierKeys.Windows) ? KeyMods.Win : 0);
    }
}
