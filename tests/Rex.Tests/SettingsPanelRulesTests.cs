using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Rex.Core;
using Rex.Mirror.Views;
using Rex.Mirror.Views.Settings;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The Settings tab's rules without a window: which settings can be changed given their parents,
/// the fullscreen controls' order, and that every control in the markup is named and laid out
/// the same way.
/// </summary>
public sealed class SettingsPanelRulesTests
{
    [Fact]
    public void ASettingUnderAParentThatIsOffCannotBeChanged()
    {
        var on = SettingsDependencies.Of(new RexConfig());
        Assert.True(on.Sensitivity && on.Zoom && on.Navigator && on.PatternOptions && on.Audio && on.AudioDup && on.Ambient && on.Hud);
        Assert.False(on.WirelessTcpip);

        var config = new RexConfig();
        config.Touchpad.TwoFingerToAndroid = false;
        config.Zoom.Enabled = false;
        config.PatternGuide.Enabled = false;
        config.Wireless.Enabled = true;
        config.Mirror.Audio = false;
        config.Ambient.Enabled = false;
        config.Hud.Enabled = false;
        var off = SettingsDependencies.Of(config);
        Assert.False(off.Sensitivity);
        Assert.False(off.Zoom);
        Assert.False(off.Navigator);
        Assert.False(off.PatternOptions);
        Assert.True(off.WirelessTcpip);
        Assert.False(off.Audio);
        Assert.False(off.AudioDup);
        Assert.False(off.Ambient);
        Assert.False(off.Hud);

        // The navigator needs both zoom and itself; keeping the audio on the phone needs a source that can.
        var navigator = new RexConfig();
        navigator.Zoom.ShowNavigator = false;
        Assert.True(SettingsDependencies.Of(navigator).Zoom);
        Assert.False(SettingsDependencies.Of(navigator).Navigator);
        var mic = new RexConfig();
        mic.Mirror.AudioSource = "mic";
        Assert.True(SettingsDependencies.Of(mic).Audio);
        Assert.False(SettingsDependencies.Of(mic).AudioDup);
    }

    [Fact]
    public void TheFullscreenControlsKeepTheCataloguesOrder()
    {
        Assert.Equal(MirrorActions.Ids.Where(HudSettings.DefaultButtons.Contains), HudSettings.DefaultButtons);

        var hud = new HudSettings { Buttons = ["fullscreen", "home", "nonsense", "home", "back"] };
        hud.Normalize();
        Assert.Equal(new[] { "home", "back", "fullscreen" }, hud.Buttons);
    }

    [Fact]
    public void EveryControlInTheSettingsTabIsNamedAndOnlySwitchesSitBesideTheirLabels()
    {
        Wpf.Run(() =>
        {
            var panel = Wpf.Layout(new SettingsPanel(), 300);
            var rows = LogicalDescendants(panel).OfType<HeaderedContentControl>().Where(r => r.GetType() == typeof(HeaderedContentControl)).ToArray();
            Assert.NotEmpty(rows);
            foreach (var row in rows)
            {
                var label = SettingsPanel.LabelOf(row);
                Assert.False(string.IsNullOrWhiteSpace(label), "A row without a label");
                var style = row.Style == (System.Windows.Style)panel.FindResource("SettingRow") ? "inline" : "stacked";
                Assert.Equal(row.Content is CheckBox ? "inline" : "stacked", style);
            }

            foreach (var control in LogicalDescendants(panel).OfType<Control>())
            {
                if (control is CheckBox or ComboBox or Slider or TextBox or ToggleButton)
                {
                    var named = !string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)) || AutomationProperties.GetLabeledBy(control) is not null;
                    Assert.True(named, $"{control.GetType().Name} '{control.Name}' has no accessible name.");
                }
            }

            return true;
        });
    }

    [Fact]
    public void EveryValueInConfigHasAControlOrARecordedReason()
    {
        using var package = new TestPackage();
        var paths = new ConfigStore(package.Paths.Config).Flatten().Select(leaf => leaf.Path).ToHashSet(StringComparer.Ordinal);
        // Flatten intentionally omits null leaves; these two are still schema values and are set by dragging.
        paths.Add("Hud.X");
        paths.Add("Hud.Y");
        // Every value has a control, or is set where it belongs, with the reason: none is left to config.json alone.
        var catalogued = SettingsCatalogue.Controls.Keys
            .Concat(SettingsCatalogue.Elsewhere.Keys)
            .ToArray();

        Assert.Equal(catalogued.Length, catalogued.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(paths.Order(), catalogued.Order());

        Wpf.Run(() =>
        {
            var panel = Wpf.Layout(new SettingsPanel(), 300);
            var controls = LogicalDescendants(panel).OfType<FrameworkElement>()
                .Where(element => element.Name.Length > 0)
                .ToDictionary(element => element.Name, StringComparer.Ordinal);
            Assert.All(SettingsCatalogue.Controls, entry =>
            {
                Assert.True(controls.TryGetValue(entry.Value, out var control), $"No settings control named '{entry.Value}'.");
                Assert.Equal(entry.Key, SettingRows.GetConfigPath(control));
            });
            return true;
        });
    }

    [Fact]
    public void GroupsInTheirOwnFilesAreStillSearchedNamedAndPolished()
    {
        Wpf.Run(() =>
        {
            var panel = Wpf.Layout(new SettingsPanel(), 300);
            Assert.Equal(
                ["GroupProfiles", "GroupDisplay", "GroupAudio", "GroupSound", "GroupSession", "GroupZoom", "GroupInput", "GroupCopies", "GroupScreen", "GroupSecondPhone", "GroupApps", "GroupFiles", "GroupHud", "GroupLockScreen", "GroupCaptures", "GroupWindow", "GroupStartup", "GroupGlobalKeys", "GroupConnection", "GroupAdvanced"],
                panel.Groups().Select(group => group.Name));
            var filter = Assert.IsType<TextBox>(panel.FindName("SettingsFilter"));
            var count = Assert.IsType<TextBlock>(panel.FindName("FilterCount"));
            var copies = panel.Groups().Single(group => group.Name == "GroupCopies");
            var hud = panel.Groups().Single(group => group.Name == "GroupHud");

            // By the words on the row, and by the config path stamped on its control.
            filter.Text = "Show the status line under them";
            Assert.Equal("1 setting matches", count.Text);
            Assert.Equal(Visibility.Visible, hud.Visibility);
            Assert.True(hud.IsExpanded);
            Assert.Equal(Visibility.Collapsed, copies.Visibility);

            filter.Text = "Copies.Gap";
            Assert.Equal("1 setting matches", count.Text);
            Assert.Equal(Visibility.Visible, copies.Visibility);
            Assert.Equal(Visibility.Collapsed, hud.Visibility);

            // Cleared, every group is back and closed as it was before the search.
            filter.Text = string.Empty;
            Assert.All(panel.Groups(), group => Assert.Equal(Visibility.Visible, group.Visibility));
            Assert.False(copies.IsExpanded);
            Assert.False(hud.IsExpanded);

            // Named and polished by the same rules as the rest of the tab.
            foreach (var row in LogicalDescendants(copies).Concat(LogicalDescendants(hud)).OfType<HeaderedContentControl>().Where(r => r.GetType() == typeof(HeaderedContentControl)))
            {
                Assert.False(string.IsNullOrWhiteSpace(SettingsPanel.LabelOf(row)), "A row without a label");
                var style = row.Style == (System.Windows.Style)panel.FindResource("SettingRow") ? "inline" : "stacked";
                Assert.Equal(row.Content is CheckBox ? "inline" : "stacked", style);
            }

            return true;
        });
    }

    [Fact]
    public void GroupsInTheirOwnFilesCanBeConstructedBeforeTheyAreAttached()
    {
        Wpf.Run(() =>
        {
            var copies = Wpf.Layout(new CopiesGroup(), 300);
            var hud = Wpf.Layout(new HudGroup(), 300);
            Assert.NotNull(LogicalDescendants(copies).OfType<Slider>().Single(slider => slider.Name == "CopiesMost"));
            Assert.NotNull(LogicalDescendants(hud).OfType<Slider>().Single(slider => slider.Name == "HudScale"));
            return true;
        });
    }

    [Fact]
    public void ASettingIsFoundByItsConfigPath()
    {
        Wpf.Run(() =>
        {
            var panel = Wpf.Layout(new SettingsPanel(), 300);
            var filter = Assert.IsType<TextBox>(panel.FindName("SettingsFilter"));
            var count = Assert.IsType<TextBlock>(panel.FindName("FilterCount"));
            var frameRate = Assert.IsType<ComboBox>(panel.FindName("MaxFps"));

            filter.Text = "Mirror.MaxFps";
            Assert.Equal("1 setting matches", count.Text);
            Assert.Equal(Visibility.Visible, frameRate.Visibility);

            // Without its group the name is the mirror's frame rate and the copies' own.
            filter.Text = "maxfps";
            Assert.Equal("2 settings match", count.Text);
            Assert.Equal(Visibility.Visible, frameRate.Visibility);
            var copies = Assert.IsType<Rex.Mirror.Views.Settings.CopiesGroup>(panel.FindName("CopiesSettingsGroup"));
            Assert.Equal(Visibility.Visible, Assert.IsType<ComboBox>(copies.FindName("CopiesMaxFps")).Visibility);

            filter.Text = "Config.Path.That.Does.Not.Exist";
            Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(panel.FindName("FilterEmpty")).Visibility);
            return true;
        });
    }

    [Fact]
    public void TheAuditStitchesScreenfulsAtTheirOffsets()
    {
        // Three screenfuls of a 100-pixel scrolling area, scrolled 0, 100 and 150: the picture is
        // the strip above the area, then the area's content from 0 to 250.
        var band = new System.Drawing.Rectangle(10, 70, 80, 100);
        var pages = new[] { 0.0, 100, 150 }
            .Select(offset => (offset, new System.Drawing.Bitmap(120, 200)))
            .ToArray();
        try
        {
            using var stitched = UiAuditCapture.Stitch(pages, band, scale: 1);
            Assert.Equal(80, stitched.Width);
            Assert.Equal(60 + 150 + 100, stitched.Height);

            using var scaled = UiAuditCapture.Stitch(pages, band, scale: 1.5);
            Assert.Equal(60 + 225 + 100, scaled.Height);
        }
        finally
        {
            foreach (var (_, shot) in pages)
            {
                shot.Dispose();
            }
        }
    }

    private static IEnumerable<System.Windows.DependencyObject> LogicalDescendants(System.Windows.DependencyObject root)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        {
            yield return child;
            foreach (var deeper in LogicalDescendants(child))
            {
                yield return deeper;
            }
        }
    }
}
