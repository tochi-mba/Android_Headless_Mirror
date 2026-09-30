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
            ShowStatus(string.Empty);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            ShowStatus("Could not read the phone's settings: " + ex.Message);
        }
        finally
        {
            _busy = false;
            LoadingText.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Shows a result line, or nothing at all: an empty line would still keep its margins.</summary>
    private void ShowStatus(string text)
    {
        Status.Text = text;
        Status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
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
        SearchCount.Text = _rows.Count == 0
            ? string.Empty
            : search.Length == 0
                ? $"{_rows.Count} settings this phone exposes"
                : $"{matches} of {_rows.Count} settings";
    }

    private void OnReload(object sender, RoutedEventArgs e) => Refresh(force: true);

    /// <summary>Writes one setting, then reloads so the row shows what the phone really took.</summary>
    private async Task ApplyAsync(PhoneSetting setting, string value)
    {
        if (_loading || Target() is not { } target || _window is null)
        {
            return;
        }

        if (!await ConfirmAsync(setting, $"Change {setting.Label}?", $"Set it to {setting.Describe(value)}.", "Change it"))
        {
            await LoadAsync(target.Adb, target.Serial);
            return;
        }

        var result = await target.Adb.ApplyPhoneSettingAsync(target.Serial, setting.Id, value);
        ShowStatus(result.Ok ? string.Empty : result.Text);
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

        if (!await ConfirmAsync(
                setting,
                $"Put {setting.Label} back to the phone's default?",
                setting.Source == PhoneSettingSource.SettingsProvider
                    ? "The value this app stored is deleted and Android decides again."
                    : "The override is removed and the phone draws at its own setting again.",
                "Use the default"))
        {
            return;
        }

        var result = await target.Adb.ResetPhoneSettingAsync(target.Serial, setting.Id);
        ShowStatus(result.Ok ? string.Empty : result.Text);
        _window.SetStatus(result.Ok ? $"{setting.Label} is back to the phone's default." : result.Text, !result.Ok);
        await LoadAsync(target.Adb, target.Serial);
    }

    /// <summary>
    /// Asks before a setting that can change how the phone behaves. The question is asked in the
    /// window, in the app's own voice, and names what is about to happen rather than offering Yes.
    /// </summary>
    private async Task<bool> ConfirmAsync(PhoneSetting setting, string title, string detail, string action)
    {
        if (_host is null || _window is null || !_host.Config.App.ConfirmSensitiveWrites ||
            setting.Risk is AndroidSettings.RiskNormal or AndroidSettings.RiskAdvanced)
        {
            return true;
        }

        _window.ShowTipOnce(Tips.FirstRiskyWrite);
        var body = string.IsNullOrWhiteSpace(setting.Description) ? detail : detail + " " + setting.Description;
        return await _window.ConfirmAsync(title, body, action, setting.Risk);
    }

    /// <summary>One row: label, description, the control the setting deserves, and a reset button.</summary>
    /// <summary>
    /// The keys that move a slider. Each one commits, since nothing else would: a keyboard user
    /// has no drag to end.
    /// </summary>
    internal static bool CommitsSlider(Key key) =>
        key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End;

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

            // A switch fits beside its label. Anything wider (a slider, a list, a text box) goes
            // under it at the panel's full width: side by side, the control took the whole row
            // and squeezed the label to nothing, which left a slider floating in empty space.
            FrameworkElement row;
            if (IsInline(value.Setting.Kind))
            {
                var editor = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                editor.Children.Add(_reset);
                editor.Children.Add(control);
                row = new HeaderedContentControl
                {
                    Style = (Style)panel.FindResource("SettingRow"),
                    Header = text,
                    Content = editor,
                };
            }
            else
            {
                _reset.Margin = new Thickness(8, 0, 0, 0);
                _reset.VerticalAlignment = VerticalAlignment.Top;
                var top = new Grid();
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                top.Children.Add(text);
                Grid.SetColumn(_reset, 1);
                top.Children.Add(_reset);

                control.Margin = new Thickness(0, 8, 0, 0);
                control.HorizontalAlignment = HorizontalAlignment.Stretch;
                var stack = new StackPanel();
                stack.Children.Add(top);
                stack.Children.Add(control);
                row = new Border
                {
                    BorderBrush = (Brush)panel.FindResource("Line"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(0, 10, 0, 10),
                    Margin = new Thickness(0, 0, 0, 2),
                    Background = Brushes.Transparent,
                    Child = stack,
                };
            }
            // The reset button stays out of the way until the pointer is on its row.
            row.MouseEnter += (_, _) => _reset.Opacity = 1;
            row.MouseLeave += (_, _) => _reset.Opacity = _reset.IsKeyboardFocusWithin ? 1 : 0;
            _reset.GotKeyboardFocus += (_, _) => _reset.Opacity = 1;
            _reset.LostKeyboardFocus += (_, _) => _reset.Opacity = row.IsMouseOver ? 1 : 0;
            Element = row;
        }

        /// <summary>Only switches sit beside their label; every other control gets a line of its own.</summary>
        internal static bool IsInline(PhoneSettingKind kind) => kind == PhoneSettingKind.Toggle;

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
            var combo = Identify(new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch }, value);
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
                var extra = new ComboBoxItem { Content = value.Setting.ChoiceLabel(value.Value), Tag = value.Value };
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
            // The slider takes the width it is given; the readout keeps enough room for the widest
            // value so the thumb does not jump as the number grows a digit.
            var panel = new Grid();
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var readout = new TextBlock
            {
                Style = (Style)_panel.FindResource("MutedText"),
                MinWidth = 56,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                FontFamily = (FontFamily)_panel.FindResource("Mono"),
            };
            Grid.SetColumn(readout, 1);
            var slider = Identify(new System.Windows.Controls.Slider
            {
                Minimum = value.Setting.Minimum,
                Maximum = value.Setting.Maximum,
                IsSnapToTickEnabled = value.Setting.Step >= 1,
                TickFrequency = value.Setting.Step,
                VerticalAlignment = VerticalAlignment.Center,
            }, value);
            slider.Value = double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var current)
                ? Math.Clamp(current, slider.Minimum, slider.Maximum)
                : slider.Minimum;
            readout.Text = value.Setting.Readout(slider.Value);
            slider.ValueChanged += (_, _) => readout.Text = value.Setting.Readout(slider.Value);
            // Writing on every pixel of a drag would flood ADB; commit when the drag ends.
            slider.PreviewMouseUp += async (_, _) => await _panel.ApplyAsync(value.Setting, value.Setting.SliderValue(slider.Value));
            slider.KeyUp += async (_, e) =>
            {
                if (CommitsSlider(e.Key))
                {
                    await _panel.ApplyAsync(value.Setting, value.Setting.SliderValue(slider.Value));
                }
            };

            panel.Children.Add(slider);
            panel.Children.Add(readout);
            return panel;
        }

        private FrameworkElement Text(PhoneSettingValue value)
        {
            var box = Identify(new TextBox { Text = value.Value, HorizontalAlignment = HorizontalAlignment.Stretch }, value);
            var hint = new TextBlock
            {
                Text = value.Setting.Hint,
                Style = (Style)_panel.FindResource("MutedText"),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Visibility = value.Value.Length == 0 && value.Setting.Hint.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
            };
            box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 && value.Setting.Hint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
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

            var field = new Grid();
            field.Children.Add(box);
            field.Children.Add(hint);
            var native = PhoneSetting.NativeLabel(value.Native);
            if (native.Length == 0)
            {
                return field;
            }

            var stack = new StackPanel();
            stack.Children.Add(field);
            stack.Children.Add(new TextBlock { Text = native, Style = (Style)_panel.FindResource("MutedText"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
            return stack;
        }

    }
}
