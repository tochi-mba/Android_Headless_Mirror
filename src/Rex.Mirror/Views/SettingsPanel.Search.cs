using System.Windows;
using System.Windows.Controls;
using Rex.Mirror.Views.Settings;

namespace Rex.Mirror.Views;

public partial class SettingsPanel
{
    private HashSet<string>? _openBeforeFilter;

    /// <summary>Narrows the panel by label, hint, option, button text, or configuration path.</summary>
    private void OnFilter(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SettingsFilter.Text.Trim();
        var searching = query.Length > 0;
        if (searching && _openBeforeFilter is null)
        {
            _openBeforeFilter = Groups().Where(group => group.IsExpanded).Select(group => group.Name).ToHashSet(StringComparer.Ordinal);
        }
        else if (!searching && _openBeforeFilter is { } restore)
        {
            _loading = true;
            try
            {
                foreach (var group in Groups()) group.IsExpanded = restore.Contains(group.Name);
            }
            finally
            {
                _loading = false;
                _openBeforeFilter = null;
            }
        }

        FilterHint.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        var matches = 0;
        foreach (var group in Groups())
        {
            var whole = searching && Mentions(group.Header, query);
            var shown = group.Content is Panel body ? FilterRows(body, whole ? string.Empty : query) : 0;
            var hit = !searching || whole || shown > 0;
            group.Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
            if (searching && hit)
            {
                group.IsExpanded = true;
                matches += shown;
            }
        }

        FilterEmpty.Visibility = searching && matches == 0 ? Visibility.Visible : Visibility.Collapsed;
        FilterCount.Visibility = searching && matches > 0 ? Visibility.Visible : Visibility.Collapsed;
        FilterCount.Text = matches == 1 ? "1 setting matches" : $"{matches} settings match";
        if (!searching && _host is not null && !_loading) Refresh();
    }

    private static int FilterRows(Panel body, string query)
    {
        var searching = query.Length > 0;
        var shown = 0;
        var children = body.Children.OfType<UIElement>().ToArray();
        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];
            if (child is Grid label && i + 1 < children.Length && children[i + 1] is Slider slider)
            {
                var pair = !searching || Mentions(label, query) || Mentions(slider, query);
                label.Visibility = pair ? Visibility.Visible : Visibility.Collapsed;
                slider.Visibility = label.Visibility;
                shown += pair ? 1 : 0;
                i++;
                continue;
            }

            if (child is StackPanel { Tag: not "row" } inner)
            {
                var count = FilterRows(inner, query);
                inner.Visibility = !searching || count > 0 ? Visibility.Visible : Visibility.Collapsed;
                shown += count;
                continue;
            }

            if (child is TextBlock)
            {
                child.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
                continue;
            }

            var hit = !searching || Mentions(child, query);
            child.Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
            shown += hit ? 1 : 0;
        }

        return shown;
    }

    internal IEnumerable<Expander> Groups() =>
        [GroupOf(ProfilesSettingsGroup), GroupDisplay, GroupAudio, GroupOf(SoundSettingsGroup), GroupSession, GroupZoom, GroupInput, GroupOf(CopiesSettingsGroup), GroupOf(SecondScreenSettingsGroup), GroupOf(SecondPhoneSettingsGroup), GroupOf(AppsSettingsGroup), GroupOf(TransferSettingsGroup), GroupOf(HudSettingsGroup), GroupLockScreen, GroupCaptures, GroupStartup, GroupOf(GlobalKeysSettingsGroup), GroupAdvanced];

    private static Expander GroupOf(ISettingsGroup group) => group.Group;

    /// <summary>Opens a group and scrolls it into view, as a link from elsewhere in the window does.</summary>
    internal void Reveal(string groupName)
    {
        if (Groups().FirstOrDefault(group => group.Name == groupName) is not { } group)
        {
            return;
        }

        group.IsExpanded = true;
        group.BringIntoView();
    }

    private void OnGroupExpansionChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null || SettingsFilter.Text.Trim().Length > 0) return;
        var open = Groups().Where(group => group.IsExpanded).Select(group => group.Name).ToList();
        _host.State.SetUi(_host.State.Ui with { SettingsOpen = open });
    }

    private static bool Mentions(object? subject, string query)
    {
        bool Says(string? text) => text is not null &&
            (text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             Normalized(text).Contains(Normalized(query), StringComparison.OrdinalIgnoreCase));

        if (subject is string text) return Says(text);
        if (subject is not DependencyObject root) return false;
        foreach (var node in Descendants(root).Prepend(root))
        {
            var said = node switch
            {
                TextBlock value => Says(value.Text),
                HeaderedContentControl row => Says(row.Header as string),
                ContentControl control => Says(control.Content as string),
                _ => false,
            };
            if (said || node is FrameworkElement element &&
                (Says(System.Windows.Automation.AutomationProperties.GetName(element)) || Says(SettingRows.GetConfigPath(element)))) return true;
        }

        return false;
        static string Normalized(string text) => new(text.Where(char.IsLetterOrDigit).ToArray());
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
}
