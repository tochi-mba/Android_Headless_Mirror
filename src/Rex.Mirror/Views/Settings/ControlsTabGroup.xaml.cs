using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views.Settings;

/// <summary>
/// The Controls tab as the person wants it. Sections are shown or hidden and moved up and down;
/// each grid's tiles are moved, removed, and added from every action there is. The rows are built
/// again on each refresh, and the button that had the keyboard keeps it, so a section or a tile
/// can be moved several places with the keyboard alone.
/// </summary>
public partial class ControlsTabGroup : UserControl, ISettingsGroup
{
    private SettingsPanel? _panel;
    private AppHost? _host;
    private bool _loading;

    public ControlsTabGroup() => InitializeComponent();

    Expander ISettingsGroup.Group => GroupControlsTab;

    public void Attach(SettingsPanel panel, MainWindow window, AppHost host)
    {
        _panel = panel;
        _host = host;
    }

    public void Refresh(RexConfig config)
    {
        _loading = true;
        var focused = Keyboard.FocusedElement is FrameworkElement had && IsAncestorOf(had) ? AutomationProperties.GetAutomationId(had) : null;
        try
        {
            var layout = config.Controls;
            BuildSections(layout);
            BuildTiles(ControlsPhoneTilesList, "phone-tiles", layout.PhoneTiles, (c, list) => c.Controls.PhoneTiles = list);
            BuildTiles(ControlsViewTilesList, "view-tiles", layout.ViewTiles, (c, list) => c.Controls.ViewTiles = list);
            BuildTiles(ControlsGestureTilesList, "gesture-tiles", layout.GestureTiles, (c, list) => c.Controls.GestureTiles = list);
            ControlsColumns.Value = layout.Columns;
            ControlsColumnsValue.Text = $"{layout.Columns}";
            ControlsTileLabels.IsChecked = layout.TileLabels;
            ControlsPutBack.IsEnabled = !Same(layout, new ControlsSettings());
        }
        finally
        {
            _loading = false;
        }

        // The keyboard goes back to the button it was on, wherever the move took its row.
        if (focused is { Length: > 0 } && Find(this, focused) is { IsEnabled: true } again)
        {
            again.Focus();
        }
    }

    private static bool Same(ControlsSettings a, ControlsSettings b) =>
        a.Sections.SequenceEqual(b.Sections) && a.PhoneTiles.SequenceEqual(b.PhoneTiles) && a.ViewTiles.SequenceEqual(b.ViewTiles) &&
        a.GestureTiles.SequenceEqual(b.GestureTiles) && a.Columns == b.Columns && a.TileLabels == b.TileLabels;

    private void BuildSections(ControlsSettings layout)
    {
        ControlsSectionsList.Children.Clear();
        var shown = layout.Sections;
        foreach (var (id, name, isShown) in layout.SectionRows())
        {
            var show = new CheckBox { IsChecked = isShown, VerticalAlignment = VerticalAlignment.Center };
            Identify(show, "section-show-" + id, $"Show {name} in the Controls tab");
            show.Checked += (_, _) => Save(c => c.Controls.Sections = [.. c.Controls.Sections.Append(id).Distinct()]);
            show.Unchecked += (_, _) => Save(c => c.Controls.Sections = [.. c.Controls.Sections.Where(s => s != id)]);
            var at = shown.IndexOf(id);
            ControlsSectionsList.Children.Add(Row(name, show,
                Move("section-up-" + id, $"Move {name} up", "Up", isShown && at > 0, c => c.Controls.Sections = ControlsSettings.Moved(c.Controls.Sections, id, -1)),
                Move("section-down-" + id, $"Move {name} down", "Down", isShown && at < shown.Count - 1, c => c.Controls.Sections = ControlsSettings.Moved(c.Controls.Sections, id, 1))));
        }
    }

    private void BuildTiles(Panel list, string key, IReadOnlyList<string> tiles, Action<RexConfig, List<string>> set)
    {
        list.Children.Clear();
        for (var i = 0; i < tiles.Count; i++)
        {
            var id = tiles[i];
            var label = MirrorActions.Find(id)?.Label ?? id;
            list.Children.Add(Row(label, null,
                Move($"{key}-up-{id}", $"Move {label} up", "Up", i > 0, c => set(c, ControlsSettings.Moved(Current(c, key), id, -1))),
                Move($"{key}-down-{id}", $"Move {label} down", "Down", i < tiles.Count - 1, c => set(c, ControlsSettings.Moved(Current(c, key), id, 1))),
                Move($"{key}-remove-{id}", $"Take {label} out", "Remove", true, c => set(c, [.. Current(c, key).Where(t => t != id)]))));
        }

        if (tiles.Count == 0)
        {
            list.Children.Add(new TextBlock { Text = "None: the grid is hidden.", Style = (Style)FindResource("MutedText"), FontSize = 11 });
        }

        // Any action can be a tile, once.
        var choice = new ComboBox { Margin = new Thickness(0, 6, 0, 0) };
        Identify(choice, key + "-add-choice", "A tile to add");
        foreach (var action in MirrorActions.All.Where(a => !tiles.Contains(a.Id)))
        {
            choice.Items.Add(new ComboBoxItem { Content = action.Label, Tag = action.Id });
        }

        var add = new Button { Content = "Add", Margin = new Thickness(6, 6, 0, 0), IsEnabled = tiles.Count < ControlsSettings.MostTiles };
        Identify(add, key + "-add", "Add the tile");
        add.ToolTip = tiles.Count < ControlsSettings.MostTiles ? null : $"A grid holds at most {ControlsSettings.MostTiles} tiles.";
        add.Click += (_, _) =>
        {
            if (choice.SelectedItem is ComboBoxItem { Tag: string chosen })
            {
                Save(c => set(c, [.. Current(c, key), chosen]));
            }
        };
        var adding = new Grid();
        adding.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        adding.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(add, 1);
        adding.Children.Add(choice);
        adding.Children.Add(add);
        list.Children.Add(adding);
    }

    private static List<string> Current(RexConfig c, string key) => key switch
    {
        "phone-tiles" => c.Controls.PhoneTiles,
        "view-tiles" => c.Controls.ViewTiles,
        _ => c.Controls.GestureTiles,
    };

    private static Grid Row(string label, UIElement? lead, params Button[] buttons)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (lead is not null)
        {
            row.Children.Add(lead);
        }

        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(lead is null ? 0 : 6, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        foreach (var button in buttons)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(button, row.ColumnDefinitions.Count - 1);
            row.Children.Add(button);
        }

        return row;
    }

    private Button Move(string id, string name, string content, bool enabled, Action<RexConfig> change)
    {
        var button = new Button { Content = content, Style = (Style)FindResource("GhostButton"), Margin = new Thickness(4, 0, 0, 0), IsEnabled = enabled };
        Identify(button, id, name);
        button.Click += (_, _) => Save(change);
        return button;
    }

    private static void Identify(FrameworkElement element, string id, string name)
    {
        AutomationProperties.SetAutomationId(element, id);
        AutomationProperties.SetName(element, name);
    }

    private static FrameworkElement? Find(DependencyObject root, string id)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is FrameworkElement element && AutomationProperties.GetAutomationId(element) == id)
            {
                return element;
            }

            if (Find(child, id) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    private void Save(Action<RexConfig> change)
    {
        if (_loading) return;
        _panel?.Save(change);
    }

    private void OnColumns(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_host is null) return;
        var columns = (int)Math.Round(ControlsColumns.Value);
        ControlsColumnsValue.Text = $"{columns}";
        if (_loading) return;
        _host.PreviewConfig(c => c.Controls.Columns = columns);
    }

    private void OnLabels(object sender, RoutedEventArgs e) => Save(c => c.Controls.TileLabels = ControlsTileLabels.IsChecked == true);

    private void OnPutBack(object sender, RoutedEventArgs e) => Save(c => c.Controls = new ControlsSettings());
}
