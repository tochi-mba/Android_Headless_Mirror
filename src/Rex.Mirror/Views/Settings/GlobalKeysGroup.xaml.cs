using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using Rex.Core;
using Rex.Mirror.Native;
using Rex.Mirror.Services;
using Rex.Mirror.Views.Controls;

namespace Rex.Mirror.Views.Settings;

/// <summary>
/// Shortcuts from anywhere: the master switch, the show-or-hide key and what it does, and actions
/// with keys of their own. A key is checked against <see cref="GlobalKeyRules"/> the moment it is
/// pressed into its box, and refused there with the reason; a key the keyboard layout types a
/// character with, or that another app has claimed, is saved with a word of warning.
/// </summary>
public partial class GlobalKeysGroup : UserControl, ISettingsGroup
{
    private SettingsPanel? _panel;
    private bool _loading;
    private RexConfig _config = new();

    /// <summary>Actions added with "Add a shortcut…" that have no key yet, so are not saved yet.</summary>
    private readonly List<string> _pending = [];
    private string _rowsShown = string.Empty;
    private (string Chord, string Text, bool Refused)? _showHideNote;
    private readonly Dictionary<string, (string Text, bool Refused)> _actionNotes = [];

    public GlobalKeysGroup()
    {
        InitializeComponent();
        GlobalShowHide.Committed += (_, chord) => OnShowHideChosen(chord);
    }

    /// <summary>The character the keyboard layout types with a chord; replaced in tests.</summary>
    internal Func<KeyChord, string?> CharacterFor { get; set; } = KeyboardProbe.CharacterFor;

    /// <summary>Whether another app has claimed a chord; replaced in tests.</summary>
    internal Func<KeyChord, bool> TakenElsewhere { get; set; } = _ => false;

    Expander ISettingsGroup.Group => GroupGlobalKeys;

    public void Attach(SettingsPanel panel, MainWindow window, AppHost host)
    {
        _panel = panel;
        TakenElsewhere = chord => KeyboardProbe.TakenElsewhere(chord, new WindowInteropHelper(window).Handle);
    }

    public void Refresh(RexConfig config)
    {
        _config = config;
        var keys = config.GlobalKeys;
        _loading = true;
        try
        {
            GlobalKeysEnabled.IsChecked = keys.Enabled;
            SelectTag(GlobalWhenBehind, keys.WhenBehind);
            SelectTag(GlobalWhenInFront, keys.WhenInFront);
            SelectTag(GlobalHideTo, keys.HideTo);
            GlobalShowFullscreen.IsChecked = keys.ShowFullscreen;
            GlobalTypeIntoPhone.IsChecked = keys.TypeIntoPhone;
            GlobalAnnounce.IsChecked = keys.Announce;
            if (!GlobalShowHide.IsKeyboardFocused)
            {
                GlobalShowHide.Chord = keys.ShowHide;
            }

            GlobalShowHideOff.IsEnabled = keys.ShowHide.Length > 0;
            _pending.RemoveAll(id => keys.Actions.Any(a => a.Action == id));
            ShowRows(keys);
            ShowNotes(keys);

            // The rows stay in sight while the switch is off, but say why they cannot be changed.
            var on = SettingsDependencies.Of(config).GlobalKeys;
            GlobalOptions.IsEnabled = on;
            const string Why = "Turn on \"Shortcuts work while the window is hidden or behind others\" first.";
            foreach (var control in new FrameworkElement[] { GlobalShowHide, GlobalShowHideOff, GlobalWhenBehind, GlobalWhenInFront, GlobalHideTo, GlobalShowFullscreen, GlobalTypeIntoPhone, GlobalAnnounce, GlobalNewAction, GlobalAddAction })
            {
                AutomationProperties.SetHelpText(control, on ? string.Empty : Why);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnOption(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Save(c =>
        {
            c.GlobalKeys.Enabled = GlobalKeysEnabled.IsChecked == true;
            c.GlobalKeys.WhenBehind = SelectedTag(GlobalWhenBehind, "front");
            c.GlobalKeys.WhenInFront = SelectedTag(GlobalWhenInFront, "hide");
            c.GlobalKeys.HideTo = SelectedTag(GlobalHideTo, "tray");
            c.GlobalKeys.ShowFullscreen = GlobalShowFullscreen.IsChecked == true;
            c.GlobalKeys.TypeIntoPhone = GlobalTypeIntoPhone.IsChecked == true;
            c.GlobalKeys.Announce = GlobalAnnounce.IsChecked == true;
        });
    }

    private void OnShowHideOff(object sender, RoutedEventArgs e) => OnShowHideChosen(string.Empty);

    /// <summary>A key pressed into the show-or-hide box: kept when the rules allow it, refused with the reason otherwise.</summary>
    internal void OnShowHideChosen(string chord)
    {
        var others = _config.GlobalKeys.Actions.Select(a => KeyChord.Parse(a.Key));
        if (chord.Length > 0 && GlobalKeyRules.WhyNot(chord, GlobalKeyRules.ShowHide, others) is { } why)
        {
            _showHideNote = (chord, why, true);
            GlobalShowHide.Chord = _config.GlobalKeys.ShowHide;
            ShowNotes(_config.GlobalKeys);
            return;
        }

        _showHideNote = chord.Length > 0 ? (chord, Warnings(KeyChord.Parse(chord)), false) : null;
        Save(c => c.GlobalKeys.ShowHide = chord);
    }

    private void OnAddAction(object sender, RoutedEventArgs e)
    {
        if ((GlobalNewAction.SelectedItem as ComboBoxItem)?.Tag is not string id)
        {
            return;
        }

        _pending.Add(id);
        ShowRows(_config.GlobalKeys);
        ShowNotes(_config.GlobalKeys);
        RowBox(id)?.Focus();
    }

    /// <summary>A key pressed into an action's box, or the box emptied (which takes the action's key away).</summary>
    internal void OnActionChosen(string id, string chord)
    {
        var others = _config.GlobalKeys.Keys().Where(k => k.Id != id).Select(k => k.Chord);
        if (chord.Length > 0 && GlobalKeyRules.WhyNot(chord, id, others) is { } why)
        {
            _actionNotes[id] = (why, true);
            RowBox(id)!.Chord = _config.GlobalKeys.Actions.FirstOrDefault(a => a.Action == id)?.Key ?? string.Empty;
            ShowNotes(_config.GlobalKeys);
            return;
        }

        if (chord.Length > 0)
        {
            _actionNotes[id] = (Warnings(KeyChord.Parse(chord)), false);
        }
        else
        {
            _actionNotes.Remove(id);
            _pending.Remove(id);
        }

        Save(c =>
        {
            var actions = c.GlobalKeys.Actions.Where(a => a.Action != id).ToList();
            if (chord.Length > 0)
            {
                var at = c.GlobalKeys.Actions.FindIndex(a => a.Action == id);
                actions.Insert(at < 0 ? actions.Count : at, new GlobalKeyAction { Key = chord, Action = id });
            }

            c.GlobalKeys.Actions = actions;
        });
    }

    private void Save(Action<RexConfig> mutate)
    {
        if (_panel is null)
        {
            // Not attached (a test of the group alone): the change lands on the settings it shows.
            mutate(_config);
            _config.Normalize();
            Refresh(_config);
            return;
        }

        _panel.Save(mutate);
    }

    /// <summary>What else the chord means on this PC, or that it simply works.</summary>
    private string Warnings(KeyChord chord)
    {
        var notes = new List<string>(2);
        if (CharacterFor(chord) is { } typed)
        {
            notes.Add($"On your keyboard this also types '{typed}'.");
        }

        if (TakenElsewhere(chord))
        {
            notes.Add("Another app uses this key too. This app takes it while it runs.");
        }

        return notes.Count == 0 ? "Works from anywhere." : string.Join(' ', notes);
    }

    private void ShowNotes(GlobalKeysSettings keys)
    {
        // A refusal is said until the next key is chosen; otherwise what the key does now.
        var (text, refused) = _showHideNote is { Refused: true } refusal ? (refusal.Text, true)
            : keys.ShowHide.Length == 0 ? ("Off: the key reaches the app in front.", false)
            : _showHideNote is { } note && note.Chord == keys.ShowHide ? (note.Text, false)
            : ("Works from anywhere.", false);
        Note(GlobalShowHideNote, text, refused);

        foreach (var row in GlobalActions.Children.OfType<Grid>())
        {
            var id = (string)row.Tag;
            var noteBlock = (TextBlock)row.Children[3];
            var (said, bad) = _actionNotes.TryGetValue(id, out var n) ? n
                : keys.Actions.Any(a => a.Action == id) ? ("Works from anywhere.", false) : ("Press the keys for it.", false);
            Note(noteBlock, said, bad);
        }

        var full = keys.Actions.Count + _pending.Count >= GlobalKeysSettings.MostActions;
        GlobalAddAction.IsEnabled = !full;
        GlobalActionsNote.Text = full ? $"That is the most there can be: {GlobalKeysSettings.MostActions}. Remove one to add another." : string.Empty;
        GlobalActionsNote.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void Note(TextBlock block, string text, bool refused)
    {
        block.Text = text;
        block.SetResourceReference(TextBlock.ForegroundProperty, refused ? "Live" : "Muted");
    }

    /// <summary>One row per action with a key, and per action still waiting for one; rebuilt only when that set changes.</summary>
    private void ShowRows(GlobalKeysSettings keys)
    {
        var rows = keys.Actions.Select(a => (a.Action, a.Key)).Concat(_pending.Select(id => (Action: id, Key: string.Empty))).ToArray();
        var shown = string.Join('|', rows.Select(r => r.Action + "=" + r.Key));
        if (shown != _rowsShown)
        {
            _rowsShown = shown;
            foreach (var old in GlobalActions.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<FrameworkElement>()).Where(c => c.Name.Length > 0).ToArray())
            {
                UnregisterName(old.Name);
            }

            GlobalActions.Children.Clear();
            foreach (var (action, key) in rows)
            {
                GlobalActions.Children.Add(Row(action, key));
            }
        }

        // An action already with a key, or waiting for one, is not offered again.
        var used = rows.Select(r => r.Action).ToHashSet(StringComparer.Ordinal);
        var selected = (GlobalNewAction.SelectedItem as ComboBoxItem)?.Tag as string;
        GlobalNewAction.Items.Clear();
        foreach (var action in MirrorActions.All.Where(a => !used.Contains(a.Id)))
        {
            GlobalNewAction.Items.Add(new ComboBoxItem { Content = action.Label, Tag = action.Id });
        }

        GlobalNewAction.SelectedItem = GlobalNewAction.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected)
            ?? GlobalNewAction.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private Grid Row(string id, string key)
    {
        var label = MirrorActions.Find(id)!.Label;
        var row = new Grid { Tag = id, Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition());
        row.RowDefinitions.Add(new RowDefinition());

        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var box = new ChordBox { Name = RowName("GlobalActionKey_", id), Chord = key, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(box, label + ", keyboard shortcut from anywhere");
        box.Committed += (_, chord) => OnActionChosen(id, chord);
        var remove = new Button
        {
            Name = RowName("GlobalActionRemove_", id),
            Content = "Remove",
            Style = (Style)FindResource("GhostButton"),
            Margin = new Thickness(8, 0, 0, 0),
        };
        AutomationProperties.SetName(remove, "Remove the shortcut for " + label);
        remove.Click += (_, _) => OnActionChosen(id, string.Empty);
        var note = new TextBlock { Name = RowName("GlobalActionNote_", id), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        Grid.SetColumn(box, 1);
        Grid.SetColumn(remove, 2);
        Grid.SetRow(note, 1);
        Grid.SetColumnSpan(note, 3);
        row.Children.Add(name);
        row.Children.Add(box);
        row.Children.Add(remove);
        row.Children.Add(note);
        RegisterName(box.Name, box);
        RegisterName(remove.Name, remove);
        RegisterName(note.Name, note);
        return row;
    }

    private ChordBox? RowBox(string id) => FindName(RowName("GlobalActionKey_", id)) as ChordBox;

    /// <summary>An element name for an action's control; names allow no hyphens.</summary>
    internal static string RowName(string prefix, string id) => prefix + id.Replace('-', '_');

    private static string SelectedTag(ComboBox combo, string fallback) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    private static void SelectTag(ComboBox combo, string tag) =>
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal));
}
