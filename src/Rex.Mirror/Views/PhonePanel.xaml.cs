using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views;

/// <summary>
/// The phone's own settings, grouped the way a phone groups them and applied over ADB the moment
/// a control changes. Every row comes from <see cref="PhoneSettings"/>; a phone only shows the
/// settings it actually has, so a missing OEM key never leaves a dead control behind.
/// </summary>
public partial class PhonePanel : UserControl
{
    private readonly List<SettingRow> _rows = [];
    private readonly Dictionary<string, Expander> _groups = new(StringComparer.Ordinal);
    private readonly HashSet<string> _openGroups = new(StringComparer.Ordinal) { PhoneSettings.GroupDisplay };
    private MainWindow? _window;
    private AppHost? _host;
    private bool _loading;
    private bool _busy;
    private string _loadedSerial = string.Empty;

    public PhonePanel()
    {
        InitializeComponent();
    }

    public void Attach(MainWindow window, AppHost host)
    {
        _window = window;
        _host = host;
        Advanced.Attach(window, host);
    }

    private (AdbClient Adb, string Serial)? Target()
    {
        var session = _host?.Session;
        var device = session?.ActiveDevice ?? session?.Devices.FirstOrDefault(d => d.IsReady);
        return session?.Adb is null || device is null ? null : (session.Adb, device.Serial);
    }

    public void Refresh(bool force = false)
    {
        var target = Target();
        Unavailable.Visibility = target is null ? Visibility.Visible : Visibility.Collapsed;
        Body.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
        if (target is null)
        {
            _loadedSerial = string.Empty;
            return;
        }

        if (!force && target.Value.Serial == _loadedSerial)
        {
            return;
        }

        _ = LoadAsync(target.Value.Adb, target.Value.Serial);
    }

    private async Task LoadAsync(AdbClient adb, string serial)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        LoadingText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            var values = await adb.ReadPhoneSettingsAsync(serial);
            _loadedSerial = serial;
            Build(values);
            Status.Text = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status.Text = "Could not read the phone's settings: " + ex.Message;
        }
        finally
        {
            _busy = false;
            LoadingText.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Rebuilds the visible rows for what this phone reported.</summary>
    private void Build(IReadOnlyList<PhoneSettingValue> values)
    {
        _loading = true;
        try
        {
            Groups.Children.Clear();
            _groups.Clear();
            _rows.Clear();

            foreach (var group in PhoneSettings.Groups)
            {
                var inGroup = values.Where(v => v.Setting.Group == group).ToArray();
                if (inGroup.Length == 0)
                {
                    continue;
                }

                var content = new StackPanel();
                foreach (var value in inGroup)
                {
                    var row = new SettingRow(value, this);
                    _rows.Add(row);
                    content.Children.Add(row.Element);
                }

                var expander = new Expander
                {
                    Header = $"{group}  ({inGroup.Length})",
                    IsExpanded = _openGroups.Contains(group),
                    Margin = new Thickness(0, 6, 0, 0),
                    Content = content,
                };
                // Applying a setting rebuilds these rows; remember what the user had open.
                var name = group;
                expander.Expanded += (_, _) => _openGroups.Add(name);
                expander.Collapsed += (_, _) => _openGroups.Remove(name);
                // x:Name is not available for generated controls; give automation a stable id.
                System.Windows.Automation.AutomationProperties.SetAutomationId(expander, "PhoneGroup " + group);
                _groups[group] = expander;
                Groups.Children.Add(expander);
            }

            ApplyFilter();
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SettingsSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var search = SettingsSearch.Text.Trim();
        var matches = 0;
        foreach (var group in _groups)
        {
            var visible = 0;
            foreach (var row in _rows.Where(r => r.Value.Setting.Group == group.Key))
            {
                var show = row.Matches(search);
                row.Element.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                visible += show ? 1 : 0;
            }

            group.Value.Visibility = visible > 0 ? Visibility.Visible : Visibility.Collapsed;
            group.Value.Header = $"{group.Key}  ({visible})";
            if (search.Length > 0 && visible > 0)
            {
                group.Value.IsExpanded = true;
            }

            matches += visible;
        }

        EmptyText.Visibility = matches == 0 && _rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnReload(object sender, RoutedEventArgs e) => Refresh(force: true);

    /// <summary>Writes one setting, then reloads so the row shows what the phone really took.</summary>
    private async Task ApplyAsync(PhoneSetting setting, string value)
    {
        if (_loading || Target() is not { } target || _window is null)
        {
            return;
        }

        if (!Confirm(setting, $"Change {setting.Label}?"))
        {
            await LoadAsync(target.Adb, target.Serial);
            return;
        }

        var result = await target.Adb.ApplyPhoneSettingAsync(target.Serial, setting.Id, value);
        Status.Text = result.Ok ? string.Empty : result.Text;
        _window.SetStatus(result.Ok ? $"{setting.Label}: {setting.Describe(value)}" : result.Text, !result.Ok);
        await LoadAsync(target.Adb, target.Serial);
    }

    /// <summary>Deletes the key so Android falls back to its own default.</summary>
    private async Task ResetAsync(PhoneSetting setting)
    {
        if (Target() is not { } target || _window is null)
        {
            return;
        }

        if (!Confirm(setting, $"Reset {setting.Label} to the phone's default?\n\nThe stored value is deleted."))
        {
            return;
        }

        var result = await target.Adb.ResetPhoneSettingAsync(target.Serial, setting.Id);
        Status.Text = result.Ok ? string.Empty : result.Text;
        _window.SetStatus(result.Ok ? $"{setting.Label} is back to the phone's default." : result.Text, !result.Ok);
        await LoadAsync(target.Adb, target.Serial);
    }

    private bool Confirm(PhoneSetting setting, string question)
    {
        if (_host is null || !_host.Config.App.ConfirmSensitiveWrites || setting.Risk is AndroidSettings.RiskNormal or AndroidSettings.RiskAdvanced)
        {
            return true;
        }

        return MessageBox.Show(
            $"{question}\n\nRisk: {setting.Risk}. {setting.Description}",
            "Android Headless Mirror",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    /// <summary>One row: label, description, the control the setting deserves, and a reset button.</summary>
    private sealed class SettingRow
    {
        private readonly PhonePanel _panel;
        private readonly Button _reset;

        public SettingRow(PhoneSettingValue value, PhonePanel panel)
        {
            Value = value;
            _panel = panel;

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = value.Setting.Label, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock
            {
                Text = value.Setting.Description,
                Style = (Style)panel.FindResource("MutedText"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });

            _reset = new Button
            {
                Content = "Default",
                Style = (Style)panel.FindResource("GhostButton"),
                MinHeight = 26,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0,
                Focusable = true,
                ToolTip = $"Delete {value.Setting.Namespace}/{value.Setting.Key} so Android uses its own default",
                Visibility = value.Setting.CanReset && !value.IsDefault ? Visibility.Visible : Visibility.Hidden,
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(_reset, "Reset " + value.Setting.Id);
            _reset.Click += async (_, _) => await panel.ResetAsync(value.Setting);

            var control = Build(value);
            control.VerticalAlignment = VerticalAlignment.Center;
            control.ToolTip = $"{value.Setting.Namespace}/{value.Setting.Key}";

            var editor = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            editor.Children.Add(_reset);
            editor.Children.Add(control);

            var row = new HeaderedContentControl
            {
                Style = (Style)panel.FindResource("SettingRow"),
                Header = text,
                Content = editor,
            };
            // The reset button stays out of the way until the pointer is on its row.
            row.MouseEnter += (_, _) => _reset.Opacity = 1;
            row.MouseLeave += (_, _) => _reset.Opacity = _reset.IsKeyboardFocusWithin ? 1 : 0;
            _reset.GotKeyboardFocus += (_, _) => _reset.Opacity = 1;
            _reset.LostKeyboardFocus += (_, _) => _reset.Opacity = row.IsMouseOver ? 1 : 0;
            Element = row;
        }

        public PhoneSettingValue Value { get; }

        public FrameworkElement Element { get; }

        public bool Matches(string search) =>
            search.Length == 0 ||
            Value.Setting.Label.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            Value.Setting.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            Value.Setting.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            Value.Setting.Key.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            Value.Setting.Group.Contains(search, StringComparison.OrdinalIgnoreCase);

        private FrameworkElement Build(PhoneSettingValue value) => value.Setting.Kind switch
        {
            PhoneSettingKind.Toggle => Toggle(value),
            PhoneSettingKind.Choice => Choice(value),
            PhoneSettingKind.Slider => Slider(value),
            _ => Text(value),
        };

        private FrameworkElement Toggle(PhoneSettingValue value)
        {
            var box = Identify(new CheckBox { IsChecked = value.Value == value.Setting.OnValue }, value);
            // Checked and Unchecked also fire for assistive technology, which Click does not.
            RoutedEventHandler apply = async (_, _) => await _panel.ApplyAsync(value.Setting,
                box.IsChecked == true ? value.Setting.OnValue : value.Setting.OffValue);
            box.Checked += apply;
            box.Unchecked += apply;
            return box;
        }

        private static T Identify<T>(T control, PhoneSettingValue value) where T : FrameworkElement
        {
            System.Windows.Automation.AutomationProperties.SetAutomationId(control, value.Setting.Id);
            System.Windows.Automation.AutomationProperties.SetName(control, value.Setting.Label);
            return control;
        }

        private FrameworkElement Choice(PhoneSettingValue value)
        {
            var combo = Identify(new ComboBox { MinWidth = 150 }, value);
            foreach (var choice in value.Setting.Choices)
            {
                combo.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Value });
            }

            if (!value.IsDefault && combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == value.Value) is { } match)
            {
                combo.SelectedItem = match;
            }
            else if (!value.IsDefault)
            {
                // The phone holds a value outside the catalogue; show it rather than silently retagging.
                var extra = new ComboBoxItem { Content = value.Value, Tag = value.Value };
                combo.Items.Add(extra);
                combo.SelectedItem = extra;
            }

            combo.SelectionChanged += async (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: string tag } && tag != value.Value)
                {
                    await _panel.ApplyAsync(value.Setting, tag);
                }
            };
            return combo;
        }

        private FrameworkElement Slider(PhoneSettingValue value)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Width = 210 };
            var readout = new TextBlock
            {
                Style = (Style)_panel.FindResource("MutedText"),
                MinWidth = 52,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            var slider = Identify(new System.Windows.Controls.Slider
            {
                Minimum = value.Setting.Minimum,
                Maximum = value.Setting.Maximum,
                Width = 140,
                IsSnapToTickEnabled = value.Setting.Step >= 1,
                TickFrequency = value.Setting.Step,
                VerticalAlignment = VerticalAlignment.Center,
            }, value);
            slider.Value = double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var current)
                ? Math.Clamp(current, slider.Minimum, slider.Maximum)
                : slider.Minimum;
            readout.Text = Format(slider.Value, value.Setting);
            slider.ValueChanged += (_, _) => readout.Text = Format(slider.Value, value.Setting);
            // Writing on every pixel of a drag would flood ADB; commit when the drag ends.
            slider.PreviewMouseUp += async (_, _) => await _panel.ApplyAsync(value.Setting, Text(slider.Value, value.Setting));
            slider.KeyUp += async (_, e) =>
            {
                if (e.Key is Key.Left or Key.Right or Key.Home or Key.End)
                {
                    await _panel.ApplyAsync(value.Setting, Text(slider.Value, value.Setting));
                }
            };

            panel.Children.Add(slider);
            panel.Children.Add(readout);
            return panel;

            static string Text(double number, PhoneSetting setting) => setting.Step >= 1
                ? ((long)Math.Round(number)).ToString(CultureInfo.InvariantCulture)
                : number.ToString("0.##", CultureInfo.InvariantCulture);

            static string Format(double number, PhoneSetting setting) =>
                Text(number, setting) + (setting.Unit.Length > 0 ? " " + setting.Unit : string.Empty);
        }

        private FrameworkElement Text(PhoneSettingValue value)
        {
            var box = Identify(new TextBox { Text = value.Value, MinWidth = 150 }, value);
            box.KeyUp += async (_, e) =>
            {
                if (e.Key == Key.Enter && box.Text.Trim() != value.Value)
                {
                    await _panel.ApplyAsync(value.Setting, box.Text.Trim());
                }
            };
            box.LostFocus += async (_, _) =>
            {
                if (box.Text.Trim() != value.Value)
                {
                    await _panel.ApplyAsync(value.Setting, box.Text.Trim());
                }
            };
            return box;
        }
    }
}
