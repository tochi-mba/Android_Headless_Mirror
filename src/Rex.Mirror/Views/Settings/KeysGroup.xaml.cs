using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Rex.Core;
using Rex.Mirror.Services;
using Rex.Mirror.Views.Controls;

namespace Rex.Mirror.Views.Settings;

/// <summary>
/// Every key the window answers to, and every plain key of browse mode, as the person wants them:
/// one row per action, with its key in a shortcut box and Put back while it differs from how the
/// app ships. A key another action has, or one that would fire while typing into the phone, is
/// refused in words under its box and nothing is saved. The rows are built once and refreshed.
/// </summary>
public partial class KeysGroup : UserControl, ISettingsGroup
{
    private sealed record Row(string Id, ChordBox Box, Button PutBack, TextBlock Why);

    /// <summary>Put back is a small button with a picture, so the name keeps its room in a narrow panel.</summary>
    private const double PutBackWidth = 36;

    private readonly List<Row> _window = [];
    private readonly List<Row> _browse = [];
    private SettingsPanel? _panel;
    private AppHost? _host;
    private bool _loading;

    public KeysGroup()
    {
        InitializeComponent();
        foreach (var (id, label, _) in KeyMap.WindowIds)
        {
            _window.Add(AddRow(WindowKeysList, "key-" + id, id, label));
        }

        foreach (var (action, label, _) in KeyMap.BrowseIds)
        {
            _browse.Add(AddRow(BrowseKeysList, "browse-key-" + action, action, label));
        }
    }

    Expander ISettingsGroup.Group => GroupKeys;

    public void Attach(SettingsPanel panel, MainWindow window, AppHost host)
    {
        _panel = panel;
        _host = host;
    }

    public void Refresh(RexConfig config)
    {
        _loading = true;
        try
        {
            var map = new KeyMap(config.Keys);
            foreach (var key in map.Window)
            {
                Show(_window.First(r => r.Id == key.Id), key.Now, key.Shipped);
            }

            foreach (var key in map.Browse)
            {
                Show(_browse.First(r => r.Id == key.Action), key.Now, key.Shipped);
            }

            KeysPutAllBack.IsEnabled = config.Keys.Window.Count > 0 || config.Keys.Browse.Count > 0;
        }
        finally
        {
            _loading = false;
        }
    }

    private static void Show(Row row, KeyChord? now, KeyChord? shipped)
    {
        // A box someone is typing a key into keeps what they are typing.
        if (!row.Box.IsKeyboardFocusWithin)
        {
            row.Box.Chord = now?.ToString() ?? string.Empty;
        }

        row.PutBack.Visibility = now == shipped ? Visibility.Collapsed : Visibility.Visible;
        row.PutBack.ToolTip = shipped is { } back ? $"Put back {back}" : "Put back: no key";
    }

    private Row AddRow(Panel list, string automationId, string id, string label)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // Room for Put back on every row, shown or not, so the boxes line up down the list.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PutBackWidth) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // The whole name, over two lines where the panel is narrow: cut short, most of them read alike.
        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        // Wide enough for the longest key the app ships with, Ctrl+Alt+Backspace, and no wider.
        var box = new ChordBox { Width = 136, Padding = new Thickness(8, 6, 8, 6), Tag = id, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(box, automationId);
        AutomationProperties.SetName(box, label + ", key");
        box.Committed += OnCommitted;
        var putBack = new Button
        {
            Content = new System.Windows.Shapes.Path
            {
                Data = (System.Windows.Media.Geometry)FindResource("IconRefresh"),
                Stroke = (System.Windows.Media.Brush)FindResource("Signal"),
                StrokeThickness = 1.6,
                Width = 16,
                Height = 16,
                Stretch = System.Windows.Media.Stretch.Uniform,
            },
            Tag = id,
            Style = (Style)FindResource("GhostButton"),
            Padding = new Thickness(4),
            Margin = new Thickness(4, 0, 0, 0),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(putBack, "put-back-" + automationId);
        AutomationProperties.SetName(putBack, $"Put {label}'s key back");
        putBack.Click += OnPutBack;
        var why = new TextBlock
        {
            Foreground = (System.Windows.Media.Brush)FindResource("Live"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetAutomationId(why, "why-" + automationId);
        AutomationProperties.SetLiveSetting(why, AutomationLiveSetting.Polite);

        Grid.SetColumn(box, 1);
        Grid.SetColumn(putBack, 2);
        Grid.SetRow(why, 1);
        Grid.SetColumnSpan(why, 3);
        grid.Children.Add(name);
        grid.Children.Add(box);
        grid.Children.Add(putBack);
        grid.Children.Add(why);
        list.Children.Add(grid);
        return new Row(id, box, putBack, why);
    }

    private void OnCommitted(ChordBox box, string key)
    {
        if (_loading || _host is null || box.Tag is not string id)
        {
            return;
        }

        var browse = _browse.Any(r => ReferenceEquals(r.Box, box));
        var row = (browse ? _browse : _window).First(r => ReferenceEquals(r.Box, box));
        var map = new KeyMap(_host.Config.Keys);
        var why = browse ? map.WhyNotBrowse(id, key) : map.WhyNotWindow(id, key) ?? KeyMap.WhyNotFromAnywhere(_host.Config.GlobalKeys, key);
        row.Why.Text = why ?? string.Empty;
        row.Why.Visibility = why is null ? Visibility.Collapsed : Visibility.Visible;
        if (why is not null)
        {
            // The box goes back to the key the action has; the words under it say why.
            box.Chord = (browse ? map.BrowseChordFor(id) : map.ChordFor(id))?.ToString() ?? string.Empty;
            return;
        }

        var shipped = new KeyMap(new KeysSettings());
        _panel?.Save(c => KeyMap.Set(browse ? c.Keys.Browse : c.Keys.Window, id, key, browse ? shipped.BrowseChordFor(id) : shipped.ChordFor(id)));
    }

    private void OnPutBack(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } button)
        {
            return;
        }

        var browse = _browse.Any(r => ReferenceEquals(r.PutBack, button));
        var row = (browse ? _browse : _window).First(r => ReferenceEquals(r.PutBack, button));
        row.Why.Visibility = Visibility.Collapsed;
        _panel?.Save(c => (browse ? c.Keys.Browse : c.Keys.Window).RemoveAll(b => b.Action == id));
    }

    private void OnPutAllBack(object sender, RoutedEventArgs e)
    {
        foreach (var row in _window.Concat(_browse))
        {
            row.Why.Visibility = Visibility.Collapsed;
        }

        _panel?.Save(c =>
        {
            c.Keys.Window.Clear();
            c.Keys.Browse.Clear();
        });
    }
}
