using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Rex.Core;
using Rex.Mirror.Views;
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
