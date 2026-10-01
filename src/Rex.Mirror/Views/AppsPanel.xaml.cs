using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views;

/// <summary>
/// The Apps tab: every app the phone can open, found by typing, opened with a click. Favourites
/// come first in the person's order, then the apps opened last, then the person's own apps and,
/// when asked, the phone's. Rows are made once per list and reused while searching, so a phone
/// with hundreds of apps filters as fast as one with ten.
/// </summary>
public partial class AppsPanel : UserControl
{
    private readonly Dictionary<string, FrameworkElement> _rows = new(StringComparer.Ordinal);
    private readonly List<(ButtonBase Opener, FrameworkElement Row)> _order = [];
    private readonly ContextMenu _menu = new();
    private MainWindow? _window;
    private AppHost? _host;
    private string _serial = string.Empty;
    private string _rowsFor = string.Empty;
    private bool _showHidden;

    public AppsPanel() => InitializeComponent();

    /// <summary>The phone whose apps are shown, or empty when there is none.</summary>
    public string Serial => _serial;

    public void Attach(MainWindow window, AppHost host)
    {
        _window = window;
        _host = host;
    }

    /// <summary>Shows the phone's apps as remembered, and reads them when they never were. Does nothing while the tab is hidden.</summary>
    public void Refresh()
    {
        if (_host is null || _window is null || !IsVisible)
        {
            return;
        }

        _serial = _window.AppsSerial ?? string.Empty;
        Unavailable.Visibility = _serial.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Body.Visibility = _serial.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_serial.Length == 0)
        {
            return;
        }

        var session = _host.Session;
        var profile = _host.State.GetDevice(_serial);
        if (profile?.Apps is null && !session.ReadingApps && session.AppsError.Length == 0)
        {
            _ = session.ReadAppsAsync(_serial);
        }

        Build(profile);
    }

    private void Build(DeviceProfile? profile)
    {
        var session = _host!.Session;
        var settings = _host.Config.Apps;
        var apps = profile?.Apps ?? [];
        var query = AppsSearch.Text;
        SearchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        AppsReadAgain.IsEnabled = !session.ReadingApps;
        AppsReadAgain.Content = session.ReadingApps ? "Reading…" : "Read again";
        AppsInfo.Text = profile?.AppsReadUtc is { } read
            ? $"{apps.Count} apps · read {TimeWords.Ago(read, DateTimeOffset.UtcNow)}"
            : string.Empty;

        // Rows depend on the list and on how a row is drawn; anything else only changes which show.
        var rowsFor = $"{_serial}|{profile?.AppsReadUtc:O}|{settings.Layout}|{settings.ShowPackages}";
        if (rowsFor != _rowsFor)
        {
            _rows.Clear();
            _rowsFor = rowsFor;
        }

        var sections = AppOrder.Sections(apps, profile?.FavouriteApps ?? [], profile?.RecentApps ?? [], settings, query, _showHidden);
        Sections.Children.Clear();
        _order.Clear();
        foreach (var section in sections)
        {
            Sections.Children.Add(new TextBlock
            {
                Text = section.Title,
                Style = (Style)FindResource("Eyebrow"),
                Name = "AppsSection" + char.ToUpperInvariant(section.Id[0]) + section.Id[1..],
            });
            Panel list = settings.Layout == "grid"
                ? new UniformGrid { Columns = 3, Margin = new Thickness(-3, 0, -3, 0) }
                : new StackPanel();
            foreach (var entry in section.Apps)
            {
                var key = $"{section.Id}/{entry.App.Package}/{entry.Favourite}/{entry.Missing}/{entry.Hidden}";
                if (!_rows.TryGetValue(key, out var row))
                {
                    row = settings.Layout == "grid" ? GridTile(section.Id, entry) : ListRow(section.Id, entry, settings.ShowPackages);
                    _rows[key] = row;
                }

                (row.Parent as Panel)?.Children.Remove(row);
                list.Children.Add(row);
                if (Opener(row) is { } opener)
                {
                    _order.Add((opener, row));
                }
            }

            Sections.Children.Add(list);
        }

        ShowState(apps, sections, query, settings);
        var hiddenCount = settings.Hidden.Count(h => apps.Any(a => a.Package == h));
        AppsShowHiddenToggle.Visibility = hiddenCount > 0 && query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowHiddenText.Text = _showHidden ? "Hide them again" : $"Show hidden apps ({hiddenCount})";
    }

    /// <summary>Says what there is when there is nothing to list: reading, a failed read, or a search that found nothing.</summary>
    private void ShowState(IReadOnlyList<PhoneApp> apps, IReadOnlyList<AppSection> sections, string query, AppsSettings settings)
    {
        var session = _host!.Session;
        string text;
        if (session.ReadingApps && apps.Count == 0)
        {
            text = "Reading the phone's apps. The first time takes a few seconds.";
        }
        else if (session.AppsError.Length > 0)
        {
            text = (apps.Count == 0 ? "Could not read the phone's apps: " : "Could not read them again: ") + session.AppsError;
        }
        else if (query.Length > 0 && sections.Count == 0)
        {
            text = $"No app is called “{query.Trim()}”.";
        }
        else
        {
            text = string.Empty;
        }

        AppsState.Text = text;
        AppsState.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        AppsTryAgain.Visibility = session.AppsError.Length > 0 && !session.ReadingApps ? Visibility.Visible : Visibility.Collapsed;
        // A search finds the phone's own apps either way; this is for showing them in the lists.
        AppsSearchSystem.Visibility = query.Length > 0 && sections.Count == 0 && !settings.ShowSystem ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        if (_host is not null && _serial.Length > 0)
        {
            Build(_host.State.GetDevice(_serial));
        }
    }

    private void OnReadAgain(object sender, RoutedEventArgs e)
    {
        if (_host is not null && _serial.Length > 0)
        {
            _ = _host.Session.ReadAppsAsync(_serial);
        }
    }

    private void OnSearchSystem(object sender, RoutedEventArgs e) => _host?.UpdateConfig(c => c.Apps.ShowSystem = true);

    private void OnShowHidden(object sender, RoutedEventArgs e)
    {
        _showHidden = AppsShowHiddenToggle.IsChecked == true;
        Refresh();
    }

    /// <summary>Typing anywhere in the tab goes to the search box.</summary>
    private void OnTyping(object sender, TextCompositionEventArgs e)
    {
        if (e.OriginalSource is TextBox || string.IsNullOrWhiteSpace(e.Text) || Body.Visibility != Visibility.Visible)
        {
            return;
        }

        AppsSearch.Focus();
        AppsSearch.Text += e.Text;
        AppsSearch.CaretIndex = AppsSearch.Text.Length;
        e.Handled = true;
    }

    /// <summary>Esc clears the search; the arrow keys move between apps; Down from the search box goes to the first.</summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && AppsSearch.Text.Length > 0)
        {
            AppsSearch.Clear();
            AppsSearch.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Up or Key.Down) || _order.Count == 0)
        {
            return;
        }

        var focused = Keyboard.FocusedElement as DependencyObject;
        var at = _order.FindIndex(o => o.Row == focused || (focused is not null && o.Row.IsAncestorOf(focused)));
        if (e.OriginalSource == AppsSearch)
        {
            at = e.Key == Key.Down ? -1 : at;
        }
        else if (at < 0)
        {
            return;
        }

        var next = at + (e.Key == Key.Down ? 1 : -1);
        if (next < 0)
        {
            AppsSearch.Focus();
        }
        else if (next < _order.Count)
        {
            _order[next].Opener.Focus();
            _order[next].Opener.BringIntoView();
        }

        e.Handled = true;
    }
}
