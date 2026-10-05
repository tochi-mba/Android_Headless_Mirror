using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Rex.Core;

namespace Rex.Mirror.Views;

/// <summary>
/// What you changed: the Changed chip beside the search swaps the groups for a list of every
/// setting that differs from how the app ships, each with its value and Put back; and every group
/// ends with a button that puts that group back. Either way only what was changed moves.
/// </summary>
public partial class SettingsPanel
{
    private readonly Dictionary<string, FrameworkElement> _controlOf = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Expander> _groupOf = new(StringComparer.Ordinal);
    private readonly Dictionary<Expander, Button> _groupResets = [];

    /// <summary>Every setting the tab has a control for, in the order the tab shows them.</summary>
    private IReadOnlyList<string> _shownPaths = [];

    /// <summary>Finds each setting's control and group once, and gives every group its Put back button.</summary>
    private void IndexSettings()
    {
        var named = Descendants(this).OfType<FrameworkElement>().Where(e => e.Name.Length > 0).GroupBy(e => e.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var (path, name) in SettingsCatalogue.Controls)
        {
            if (!named.TryGetValue(name, out var control))
            {
                continue;
            }

            _controlOf[path] = control;
            DependencyObject? up = control;
            while (up is not null and not Expander)
            {
                up = LogicalTreeHelper.GetParent(up);
            }

            if (up is Expander group)
            {
                _groupOf[path] = group;
            }
        }

        var order = Groups().ToList();
        _shownPaths = SettingsCatalogue.Controls.Keys
            .Where(_groupOf.ContainsKey)
            .OrderBy(path => order.IndexOf(_groupOf[path]))
            .ToArray();

        foreach (var group in order)
        {
            if (group.Content is not Panel body)
            {
                continue;
            }

            var header = group.Header as string ?? group.Name;
            var reset = new Button
            {
                Content = "Put this group back to how it ships",
                Style = (Style)FindResource("GhostButton"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 12, 0, 0),
                Tag = "group-reset",
            };
            AutomationProperties.SetAutomationId(reset, "putback-group-" + group.Name);
            AutomationProperties.SetName(reset, $"Put {header} back to how it ships");
            reset.Click += async (_, _) => await PutGroupBackAsync(group, header);
            body.Children.Add(reset);
            _groupResets[group] = reset;
        }
    }

    /// <summary>The settings that differ now, the chip's count, the list when it shows, and which groups can be put back.</summary>
    private void RefreshChanges(RexConfig config)
    {
        var changed = SettingsChanges.Of(config, _shownPaths);
        SettingsChangedOnly.Content = changed.Count == 0 ? "Changed" : $"Changed · {changed.Count}";
        // The count is in what a screen reader says too, not only in what the chip shows.
        AutomationProperties.SetName(SettingsChangedOnly, changed.Count switch
        {
            0 => "Show only what you changed",
            1 => "Show only the 1 setting you changed",
            _ => $"Show only the {changed.Count} settings you changed",
        });
        SettingsChangedOnly.IsEnabled = changed.Count > 0 || SettingsChangedOnly.IsChecked == true;
        SettingsChangedOnly.ToolTip = changed.Count == 0
            ? "Every setting is as the app ships"
            : "Only the settings you changed, each with a way back";
        foreach (var (group, reset) in _groupResets)
        {
            var mine = changed.Count(path => _groupOf.TryGetValue(path, out var owner) && ReferenceEquals(owner, group));
            reset.IsEnabled = mine > 0;
            reset.ToolTip = mine switch
            {
                0 => "Every setting here is as the app ships",
                1 => "1 setting here goes back",
                _ => $"{mine} settings here go back",
            };
        }

        if (SettingsChangedOnly.IsChecked == true)
        {
            ShowChanged(config, changed);
        }
    }

    private void OnChangedOnly(object sender, RoutedEventArgs e)
    {
        var on = SettingsChangedOnly.IsChecked == true;
        ChangedList.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        foreach (var group in Groups())
        {
            group.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        }

        if (on)
        {
            SettingsFilter.Text = string.Empty;
        }

        if (_host is not null)
        {
            RefreshChanges(_host.Config);
        }
    }

    private void ShowChanged(RexConfig config, IReadOnlyList<string> changed)
    {
        ChangedRows.Children.Clear();
        ChangedCount.Text = changed.Count switch
        {
            0 => "Every setting is as the app ships.",
            1 => "1 setting is not as the app ships.",
            _ => $"{changed.Count} settings are not as the app ships.",
        };
        ChangedPutAllBack.Visibility = changed.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        var values = ConfigPaths.Values(config);
        foreach (var path in changed)
        {
            var label = LabelFor(path);
            var where = _groupOf.TryGetValue(path, out var group) ? group.Header as string ?? string.Empty : string.Empty;
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = new Button
            {
                Content = label,
                Style = (Style)FindResource("GhostButton"),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                FontWeight = FontWeights.SemiBold,
                ToolTip = "Show it where it lives in " + where,
            };
            AutomationProperties.SetAutomationId(name, "changed-" + path);
            name.Click += (_, _) => ShowSetting(path);
            text.Children.Add(name);
            text.Children.Add(new TextBlock
            {
                Text = $"{where} · now {ValueWords(path, values.TryGetValue(path, out var value) ? value : null)}",
                Style = (Style)FindResource("MutedText"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var back = new Button { Content = "Put back", Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            AutomationProperties.SetAutomationId(back, "putback-" + path);
            AutomationProperties.SetName(back, $"Put {label} back to how it ships");
            back.Click += (_, _) => PutBack([path], $"{label} is back to how it ships");

            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(text);
            Grid.SetColumn(back, 1);
            row.Children.Add(back);
            ChangedRows.Children.Add(row);
        }
    }

    /// <summary>A setting's value in the words its control shows: the choice's name, On or Off, the number.</summary>
    private string ValueWords(string path, JsonNode? value)
    {
        if (_controlOf.TryGetValue(path, out var control))
        {
            switch (control)
            {
                case ComboBox { SelectedItem: ComboBoxItem { Content: { } choice } }:
                    return choice.ToString() ?? string.Empty;
                case ToggleButton toggle:
                    return toggle.IsChecked == true ? "On" : "Off";
            }
        }

        return value?.GetValueKind() switch
        {
            JsonValueKind.True => "On",
            JsonValueKind.False => "Off",
            JsonValueKind.String => value.GetValue<string>() is { Length: > 0 } text ? text : "empty",
            JsonValueKind.Array => value.AsArray().Count == 0 ? "none" : string.Join(", ", value.AsArray().Select(item => item?.ToString())),
            JsonValueKind.Number => value.GetValue<double>().ToString("0.##", CultureInfo.InvariantCulture),
            _ => "not set",
        };
    }

    /// <summary>Leaves the list for the setting itself, found by its path the way the search finds it.</summary>
    private void ShowSetting(string path)
    {
        SettingsChangedOnly.IsChecked = false;
        SettingsFilter.Text = path;
        if (_controlOf.TryGetValue(path, out var control))
        {
            Dispatcher.BeginInvoke(() =>
            {
                control.BringIntoView();
                control.Focus();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void PutBack(IReadOnlyList<string> paths, string words)
    {
        Save(c => c.CopyFrom(SettingsChanges.PutBack(c, paths)));
        _window?.SetStatus(words);
    }

    private async Task PutGroupBackAsync(Expander group, string header)
    {
        if (_host is null || _window is null)
        {
            return;
        }

        var paths = SettingsChanges.Of(_host.Config, _shownPaths).Where(p => _groupOf.TryGetValue(p, out var owner) && ReferenceEquals(owner, group)).ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        if (await _window.ConfirmAsync($"Put {header} back to how it ships?", Listing(paths), "Put it back"))
        {
            PutBack(paths, $"{header} is back to how it ships");
        }
    }

    private async void OnPutAllBack(object sender, RoutedEventArgs e)
    {
        if (_host is null || _window is null)
        {
            return;
        }

        var paths = SettingsChanges.Of(_host.Config, _shownPaths);
        if (paths.Count > 0 && await _window.ConfirmAsync("Put every changed setting back?", Listing(paths), "Put them all back"))
        {
            PutBack(paths, "Every setting is back to how the app ships");
        }
    }

    /// <summary>"Frame rate, Phone audio on this PC and 3 more go back." for the question before putting back.</summary>
    private string Listing(IReadOnlyList<string> paths)
    {
        var labels = paths.Take(3).Select(LabelFor).ToList();
        var more = paths.Count - labels.Count;
        var named = more > 0 ? string.Join(", ", labels) + $" and {more} more" : labels.Count == 1 ? labels[0] : string.Join(", ", labels.Take(labels.Count - 1)) + " and " + labels[^1];
        return $"{named} go back to how the app ships. Nothing else changes.";
    }
}
