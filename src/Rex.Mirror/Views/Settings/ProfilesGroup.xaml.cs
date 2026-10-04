using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views.Settings;

/// <summary>
/// Profiles: what is in effect, a card for each saved profile with its menu, saving the current
/// settings as one, the presets, importing, and the rules that switch them by themselves.
/// </summary>
public partial class ProfilesGroup : UserControl, ISettingsGroup
{
    private SettingsPanel? _panel;
    private MainWindow? _window;
    private AppHost? _host;
    private bool _loading;
    private string? _renaming;
    private readonly HashSet<string> _showing = new(StringComparer.OrdinalIgnoreCase);

    public ProfilesGroup() => InitializeComponent();

    Expander ISettingsGroup.Group => GroupProfiles;

    private ProfileRunner Runner => _host!.Profiles;

    public void Attach(SettingsPanel panel, MainWindow window, AppHost host)
    {
        _panel = panel;
        _window = window;
        _host = host;
        ProfilesPreset.ItemsSource = ProfilePresets.All.Select(p => p.Name).ToArray();
        ProfilesPreset.SelectedIndex = 0;
        host.Profiles.Changed += () => Refresh(host.Config);
    }

    public void Refresh(RexConfig config)
    {
        if (_window is null)
        {
            return;
        }

        var entries = Runner.Store.List();
        _loading = true;
        try
        {
            var names = entries.Where(e => e.Profile is not null).Select(e => e.Name).ToArray();
            Choices(ProfilesFullscreen, names, config.Profiles.WhenFullscreen);
            Choices(ProfilesBattery, names, config.Profiles.WhenOnBattery);
            var battery = Runner.HasBattery;
            ProfilesBattery.IsEnabled = battery;
            ProfilesBattery.ToolTip = battery ? null : "This PC has no battery.";
            AutomationProperties.SetHelpText(ProfilesBattery, battery ? string.Empty : "This PC has no battery.");
            ProfilesPerPhone.IsChecked = config.Profiles.PerPhone;
            ProfilesPutBack.IsChecked = config.Profiles.PutBack;
            ProfilesAnnounce.IsChecked = config.Profiles.Announce;
            ProfilesKeys.IsChecked = config.Profiles.Keys;
            ProfilesInTray.IsChecked = config.Profiles.InTray;
            ProfilesAfter.SelectedItem = ProfilesAfter.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == config.Profiles.AfterApplying);
        }
        finally
        {
            _loading = false;
        }

        ShowNow(entries, config);
        BuildCards(entries, config);
        ShowPreset();
    }

    private void ShowNow(IReadOnlyList<ProfileEntry> entries, RexConfig config)
    {
        var automatic = Runner.Automatic;
        var manual = Runner.Manual;
        var current = automatic ?? manual;
        var profile = current.Length == 0 ? null : entries.FirstOrDefault(e => e.Name.Equals(current, StringComparison.OrdinalIgnoreCase))?.Profile ?? ProfilePresets.Find(current);
        if (profile is null)
        {
            ProfilesNow.Text = "Now: your own settings";
            return;
        }

        var changed = profile.ChangedSince(config);
        ProfilesNow.Text = $"Now: {profile.Name}" + (automatic is not null ? " (switched on by itself)" : string.Empty) +
            (changed == 0 ? string.Empty : changed == 1 ? " · 1 setting changed since" : $" · {changed} settings changed since");
    }

    private void BuildCards(IReadOnlyList<ProfileEntry> entries, RexConfig config)
    {
        ProfilesList.Children.Clear();
        ProfilesEmpty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < entries.Count; i++)
        {
            ProfilesList.Children.Add(Card(entries[i], i + 1, config));
        }
    }

    private FrameworkElement Card(ProfileEntry entry, int number, RexConfig config)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = entry.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = entry.Name });
        var detail = entry.Profile is { } profile
            ? (profile.Settings.Count == 1 ? "1 setting" : $"{profile.Settings.Count} settings") + (profile.Groups.Count > 0 ? " · " + string.Join(", ", profile.Groups.Take(3)) + (profile.Groups.Count > 3 ? "…" : string.Empty) : string.Empty)
              + (number <= Shortcuts.ProfileKeys && config.Profiles.Keys ? " · " + Shortcuts.Gesture(Shortcuts.ProfilePrefix + number) : string.Empty)
            : "Could not be read: " + entry.Problem;
        text.Children.Add(new TextBlock { Text = detail, Style = (Style)FindResource("MutedText"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });

        var apply = new Button { Content = "Apply", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4), IsEnabled = entry.Profile is not null, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(apply, "profile-apply-" + number);
        AutomationProperties.SetName(apply, "Apply " + entry.Name);
        apply.Click += (_, _) => Say(Runner.Apply(entry.Name));

        var more = new Button { Content = "…", Style = (Style)FindResource("GhostButton"), Width = 36, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(more, "profile-menu-" + number);
        AutomationProperties.SetName(more, "More for " + entry.Name);
        more.Click += (_, _) => OpenMenu(more, entry, number);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(apply, 1);
        grid.Children.Add(apply);
        Grid.SetColumn(more, 2);
        grid.Children.Add(more);
        if (entry.Profile is { } shown && _showing.Contains(entry.Name))
        {
            var what = new TextBlock
            {
                Text = Changes(shown),
                Style = (Style)FindResource("MutedText"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            };
            AutomationProperties.SetAutomationId(what, "profile-changes-" + number);
            Grid.SetRow(what, 1);
            Grid.SetColumnSpan(what, 3);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(what);
        }

        return new Border
        {
            Child = grid,
            Background = (System.Windows.Media.Brush)FindResource("Raised"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 6, 6),
            Margin = new Thickness(0, 0, 0, 6),
        };
    }

    private void OpenMenu(Button anchor, ProfileEntry entry, int number)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        void Add(string label, string id, Action act, bool enabled = true)
        {
            var item = new MenuItem { Header = label, IsEnabled = enabled };
            AutomationProperties.SetAutomationId(item, "profile-menu-item-" + id);
            item.Click += (_, _) => act();
            menu.Items.Add(item);
        }

        var readable = entry.Profile is not null;
        Add("Update with the current settings", "update", () => { Runner.Update(entry.Name); Say($"{entry.Name} now has the current settings"); }, readable);
        Add("Rename…", "rename", () => StartRename(entry.Name));
        Add("Duplicate", "duplicate", () => Say("Saved a copy as " + Runner.Store.Duplicate(entry.Name, DateTimeOffset.UtcNow)), readable);
        Add("Move up", "up", () => { Runner.Store.Move(entry.Name, -1); Refresh(_host!.Config); }, number > 1);
        Add("Move down", "down", () => { Runner.Store.Move(entry.Name, 1); Refresh(_host!.Config); });
        Add(_showing.Contains(entry.Name) ? "Hide what it changes" : "Show what it changes", "show", () =>
        {
            if (!_showing.Remove(entry.Name))
            {
                _showing.Add(entry.Name);
            }

            Refresh(_host!.Config);
        }, readable);
        menu.Items.Add(new Separator());
        if (_window!.AppsSerial is { } serial)
        {
            var phone = _host!.State.GetDevice(serial);
            var own = phone?.Profile.Equals(entry.Name, StringComparison.OrdinalIgnoreCase) == true;
            var phoneName = string.IsNullOrWhiteSpace(phone?.Name) ? "this phone" : phone!.Name;
            Add(own ? $"Stop using it for {phoneName}" : $"Use it for {phoneName}", "phone", () => Runner.UseForPhone(serial, own ? string.Empty : entry.Name), readable);
        }

        Add("Use it in fullscreen", "fullscreen", () => _panel!.Save(c => c.Profiles.WhenFullscreen = entry.Name), readable);
        Add("Use it on battery", "battery", () => _panel!.Save(c => c.Profiles.WhenOnBattery = entry.Name), readable && Runner.HasBattery);
        Add("Export…", "export", () => Export(entry.Name), readable);
        menu.Items.Add(new Separator());
        Add("Delete…", "delete", () => _ = DeleteAsync(entry.Name));
        menu.IsOpen = true;
    }

    private async Task DeleteAsync(string name)
    {
        if (await _window!.ConfirmAsync($"Delete {name}?", "The profile is removed. Your settings stay as they are.", "Delete", risk: "Cannot be undone"))
        {
            Runner.Delete(name);
            Say($"{name} deleted");
        }
    }

    private void Export(string name)
    {
        var dialog = new SaveFileDialog { Title = "Export a profile", FileName = name + ".json", Filter = "Profile (*.json)|*.json" };
        if (dialog.ShowDialog(_window) == true)
        {
            Runner.Store.Export(name, dialog.FileName);
            Say($"{name} exported");
        }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Import a profile", Filter = "Profile (*.json)|*.json" };
        if (dialog.ShowDialog(_window) != true)
        {
            return;
        }

        try
        {
            Say("Imported as " + Runner.Store.Import(dialog.FileName, DateTimeOffset.UtcNow));
            Refresh(_host!.Config);
        }
        catch (Exception ex) when (ex is FormatException or IOException or ArgumentException)
        {
            Say("Could not import it: " + ex.Message, error: true);
        }
    }

    /// <summary>A profile's settings as the Settings tab names them: "Frame rate: 120 · Phone sound: Off".</summary>
    private string Changes(Profile profile) =>
        profile.Settings.Count == 0
            ? "It changes nothing yet."
            : string.Join(" · ", profile.Settings.Select(p => $"{_panel!.LabelFor(p.Key)}: {Words(p.Value)}"));

    private static string Words(System.Text.Json.Nodes.JsonNode value) => value.GetValueKind() switch
    {
        System.Text.Json.JsonValueKind.True => "On",
        System.Text.Json.JsonValueKind.False => "Off",
        System.Text.Json.JsonValueKind.String => value.GetValue<string>() is { Length: > 0 } s ? s : "None",
        System.Text.Json.JsonValueKind.Array => value.AsArray().Count == 0 ? "None" : string.Join(", ", value.AsArray().Select(v => v?.ToString())),
        _ => value.ToJsonString(),
    };

    // ----- Saving and renaming -----

    private void OnShowSave(object sender, RoutedEventArgs e)
    {
        _renaming = null;
        ProfileName.Text = string.Empty;
        ProfileGroupsLabel.Visibility = Visibility.Visible;
        ProfileGroups.Visibility = Visibility.Visible;
        ProfilesOnlyChanged.IsChecked = true;
        ProfilesSaveConfirm.Content = "Save";
        BuildGroupChips(onlyChanged: true);
        ProfilesSaveForm.Visibility = Visibility.Visible;
        ProfileName.Focus();
        ShowNameProblem();
    }

    private void StartRename(string name)
    {
        _renaming = name;
        ProfileName.Text = name;
        ProfileGroupsLabel.Visibility = Visibility.Collapsed;
        ProfileGroups.Visibility = Visibility.Collapsed;
        ProfilesSaveConfirm.Content = "Rename";
        ProfilesSaveForm.Visibility = Visibility.Visible;
        ProfileName.Focus();
        ProfileName.SelectAll();
    }

    /// <summary>A chip per group of settings; the ones whose settings differ from how the app ships start ticked.</summary>
    private void BuildGroupChips(bool onlyChanged)
    {
        var changed = ConfigPaths.Differences(_host!.Config, new RexConfig()).Select(Profile.GroupOf).ToHashSet(StringComparer.Ordinal);
        var groups = ConfigPaths.Values(new RexConfig()).Keys.Where(Profile.Carries).Select(Profile.GroupOf).Distinct(StringComparer.Ordinal);
        ProfileGroups.Children.Clear();
        foreach (var group in groups)
        {
            var chip = new ToggleButton { Content = group, Style = (Style)FindResource("Chip"), Tag = group, IsChecked = !onlyChanged || changed.Contains(group) };
            AutomationProperties.SetAutomationId(chip, "profile-group-" + group);
            ProfileGroups.Children.Add(chip);
        }
    }

    private void OnOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (ProfilesSaveForm is not null && ProfilesSaveForm.Visibility == Visibility.Visible && _renaming is null)
        {
            BuildGroupChips(ProfilesOnlyChanged.IsChecked == true);
        }
    }

    private void OnNameTyped(object sender, TextChangedEventArgs e) => ShowNameProblem();

    private void OnNameKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnSave(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            OnCancelSave(sender, e);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Checks the name as it is typed: an allowed name, and for a rename one no other profile has.
    /// Saving under a name in use replaces that profile, which is said but allowed.
    /// </summary>
    private string? ShowNameProblem()
    {
        if (_window is null)
        {
            return null;
        }

        var name = ProfileName.Text.Trim();
        var taken = name.Length > 0 && !name.Equals(_renaming, StringComparison.OrdinalIgnoreCase) && Runner.Store.Load(name) is not null;
        var problem = name.Length == 0 ? null : ProfileNames.WhyNot(name)
            ?? (taken && _renaming is not null ? $"There is already a profile called “{name}”." : null);
        var note = problem ?? (taken ? $"Saving replaces the profile called “{name}”." : null);
        ProfileNameProblem.Text = note ?? string.Empty;
        ProfileNameProblem.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
        ProfilesSaveConfirm.IsEnabled = name.Length > 0 && problem is null;
        return problem;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (ShowNameProblem() is not null || ProfileName.Text.Trim().Length == 0)
        {
            return;
        }

        var name = ProfileName.Text.Trim();
        try
        {
            if (_renaming is { } from)
            {
                Runner.Rename(from, name);
                Say($"{from} is now {name}");
            }
            else
            {
                var groups = ProfileGroups.Children.OfType<ToggleButton>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToArray();
                var profile = Runner.Save(name, groups, ProfilesOnlyChanged.IsChecked == true);
                Say(profile.Settings.Count == 1 ? $"Saved {name} with 1 setting" : $"Saved {name} with {profile.Settings.Count} settings");
            }

            ProfilesSaveForm.Visibility = Visibility.Collapsed;
        }
        catch (ArgumentException ex)
        {
            ProfileNameProblem.Text = ex.Message;
            ProfileNameProblem.Visibility = Visibility.Visible;
        }
    }

    private void OnCancelSave(object sender, RoutedEventArgs e)
    {
        _renaming = null;
        ProfilesSaveForm.Visibility = Visibility.Collapsed;
    }

    // ----- Presets -----

    private void OnPresetChosen(object sender, SelectionChangedEventArgs e) => ShowPreset();

    private Profile? Preset => ProfilesPreset.SelectedItem is string name ? ProfilePresets.Find(name) : null;

    private void ShowPreset()
    {
        if (Preset is not { } preset)
        {
            ProfilesPresetChanges.Text = string.Empty;
            return;
        }

        ProfilesPresetChanges.Text = Changes(preset);
    }

    private void OnPresetApply(object sender, RoutedEventArgs e)
    {
        if (Preset is { } preset)
        {
            Say(Runner.Apply(preset.Name));
        }
    }

    private void OnPresetSave(object sender, RoutedEventArgs e)
    {
        if (Preset is not { } preset)
        {
            return;
        }

        var name = Runner.Store.Load(preset.Name) is null ? preset.Name : preset.Name + " (mine)";
        Runner.Store.Save(preset with { Name = name, Saved = DateTimeOffset.UtcNow });
        Say($"Saved {name}; change it with Update after adjusting the settings");
        Refresh(_host!.Config);
    }

    // ----- Rules -----

    private void OnOption(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _panel?.Save(c =>
        {
            c.Profiles.WhenFullscreen = ProfilesFullscreen.SelectedItem is ComboBoxItem { Tag: string fullscreen } ? fullscreen : string.Empty;
            c.Profiles.WhenOnBattery = ProfilesBattery.SelectedItem is ComboBoxItem { Tag: string battery } ? battery : string.Empty;
            c.Profiles.PerPhone = ProfilesPerPhone.IsChecked == true;
            c.Profiles.PutBack = ProfilesPutBack.IsChecked == true;
            c.Profiles.Announce = ProfilesAnnounce.IsChecked == true;
            c.Profiles.Keys = ProfilesKeys.IsChecked == true;
            c.Profiles.InTray = ProfilesInTray.IsChecked == true;
            c.Profiles.AfterApplying = ProfilesAfter.SelectedItem is ComboBoxItem { Tag: string after } ? after : "offer";
        });
        _host?.Profiles.Evaluate();
    }

    /// <summary>None, the saved profiles and the presets, the given one chosen.</summary>
    private static void Choices(ComboBox combo, IReadOnlyList<string> names, string chosen)
    {
        var items = ProfilePresets.RuleChoices(names).Select(c => new ComboBoxItem { Content = c.Label, Tag = c.Value }).ToList();
        combo.ItemsSource = items;
        combo.SelectedItem = items.FirstOrDefault(i => ((string)i.Tag).Equals(chosen, StringComparison.OrdinalIgnoreCase)) ?? items[0];
    }

    private void Say(string words, bool error = false)
    {
        _window?.SetStatus(words, error);
        Refresh(_host!.Config);
    }
}
