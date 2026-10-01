using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Rex.Core;

namespace Rex.Mirror.Views;

/// <summary>The Apps tab's rows and tiles, and the menu every app has.</summary>
public partial class AppsPanel
{
    /// <summary>A row of the list: the tile, the name and package, and the star beside them.</summary>
    private FrameworkElement ListRow(string section, AppEntry entry, bool showPackage)
    {
        var app = entry.App;
        var row = new Grid { Margin = new Thickness(-6, 0, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = app.Name, TextTrimming = TextTrimming.CharacterEllipsis });
        var detail = entry.Missing ? "Not on this phone any more"
            : section == AppOrder.Matches && app.System ? (showPackage ? "System app · " + app.Package : "System app")
            : showPackage ? app.Package : null;
        if (detail is not null)
        {
            text.Children.Add(new TextBlock { Text = detail, Style = (Style)FindResource("MutedText"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        }

        var face = new Grid();
        face.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        face.ColumnDefinitions.Add(new ColumnDefinition());
        face.Children.Add(Tile(app, 30));
        Grid.SetColumn(text, 1);
        face.Children.Add(text);

        var open = Opener(section, entry, face);
        row.Children.Add(open);

        FrameworkElement side;
        if (entry.Missing)
        {
            var remove = new Button { Content = "Remove", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(10, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAutomationId(remove, $"app-remove-{app.Package}");
            AutomationProperties.SetName(remove, $"Remove {app.Package} from favourites");
            remove.Click += (_, _) => Favourite(app.Package, false);
            side = remove;
        }
        else
        {
            var star = new ToggleButton { Style = (Style)FindResource("StarToggle"), IsChecked = entry.Favourite };
            AutomationProperties.SetAutomationId(star, $"app-star-{section}-{app.Package}");
            var starName = entry.Favourite ? $"Remove {app.Name} from favourites" : $"Add {app.Name} to favourites";
            AutomationProperties.SetName(star, starName);
            star.ToolTip = starName;
            // Checked rather than Click: assistive technology toggles the star without clicking it.
            star.Checked += (_, _) => Favourite(app.Package, true);
            star.Unchecked += (_, _) => Favourite(app.Package, false);
            side = star;
        }

        Grid.SetColumn(side, 1);
        row.Children.Add(side);
        return row;
    }

    /// <summary>A tile of the grid: the letter tile over the name. Starring is in its menu.</summary>
    private FrameworkElement GridTile(string section, AppEntry entry)
    {
        var face = new StackPanel();
        var tile = Tile(entry.App, 36);
        tile.HorizontalAlignment = HorizontalAlignment.Center;
        face.Children.Add(tile);
        face.Children.Add(new TextBlock
        {
            Text = entry.App.Name,
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 5, 0, 0),
        });
        var open = Opener(section, entry, face);
        open.Margin = new Thickness(3);
        open.Padding = new Thickness(4, 8, 4, 8);
        open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        return open;
    }

    /// <summary>The button that opens the app; Shift opens it fresh, and its menu has everything else.</summary>
    private Button Opener(string section, AppEntry entry, object face)
    {
        var app = entry.App;
        var open = new Button { Style = (Style)FindResource("AppRow"), Content = face, ContextMenu = _menu, IsEnabled = !entry.Missing };
        AutomationProperties.SetAutomationId(open, $"app-{section}-{app.Package}");
        AutomationProperties.SetName(open, entry.Missing ? $"{app.Package}, not on this phone any more" : app.System ? app.Name + ", system app" : app.Name);
        AutomationProperties.SetHelpText(open, "Opens it on the phone. Shift opens it fresh; the menu key shows more.");
        open.ToolTip = entry.Missing ? "Not on this phone any more" : $"{app.Name} · {app.Package}\nShift+click opens it fresh";
        ToolTipService.SetShowOnDisabled(open, true);
        open.Click += async (_, _) => await _window!.OpenAppAsync(app, fresh: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || _host!.Config.Apps.OpenFresh);
        open.ContextMenuOpening += (_, _) => FillMenu(section, entry);
        return open;
    }

    private static ButtonBase? Opener(FrameworkElement row) =>
        row as ButtonBase ?? (row as Panel)?.Children.OfType<ButtonBase>().FirstOrDefault();

    /// <summary>The app's letter on its own tint: the same app always gets the same one.</summary>
    internal static Border Tile(PhoneApp app, double size) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 4),
        Background = (Brush)Application.Current.FindResource("AppTile" + LetterTile.Tint(app.Package)),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = LetterTile.Letter(app.Name),
            FontWeight = FontWeights.SemiBold,
            FontSize = size * 0.45,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    /// <summary>Fills the one menu every row shares with what can be done to this app.</summary>
    private void FillMenu(string section, AppEntry entry)
    {
        var app = entry.App;
        _menu.Items.Clear();
        void Add(string label, string id, Action act)
        {
            var item = new MenuItem { Header = label };
            AutomationProperties.SetAutomationId(item, "app-menu-" + id);
            item.Click += (_, _) => act();
            _menu.Items.Add(item);
        }

        Add("Open", "open", () => _ = _window!.OpenAppAsync(app, fresh: false));
        Add("Open fresh", "open-fresh", () => _ = _window!.OpenAppAsync(app, fresh: true));
        Add(entry.Favourite ? "Remove from favourites" : "Add to favourites", "favourite", () => Favourite(app.Package, !entry.Favourite));
        if (section == AppOrder.Favourites)
        {
            Add("Move up", "up", () => Move(app.Package, -1));
            Add("Move down", "down", () => Move(app.Package, 1));
        }

        _menu.Items.Add(new Separator());
        Add("App info on the phone", "info", () => _ = _window!.AppChoreAsync("info", app));
        Add("Close the app", "close", () => _ = _window!.AppChoreAsync("close", app));
        Add(entry.Hidden ? "Show it in the list again" : "Hide from this list", "hide", () => Hide(app.Package, !entry.Hidden));
        Add("Copy the package name", "copy", () => _window!.CopyText(app.Package, "Copied " + app.Package));
        if (!app.System)
        {
            _menu.Items.Add(new Separator());
            Add("Clear its data…", "clear", () => _ = _window!.AppChoreAsync("clear", app));
            Add("Uninstall…", "uninstall", () => _ = _window!.AppChoreAsync("uninstall", app));
        }
    }

    private void Favourite(string package, bool on)
    {
        _host!.State.SetFavourite(_serial, package, on);
        _window!.AppsEdited();
    }

    private void Move(string package, int delta)
    {
        _host!.State.MoveFavourite(_serial, package, delta);
        _window!.AppsEdited();
    }

    private void Hide(string package, bool hide) => _host!.UpdateConfig(c =>
    {
        c.Apps.Hidden.Remove(package);
        if (hide)
        {
            c.Apps.Hidden.Add(package);
        }
    });
}
