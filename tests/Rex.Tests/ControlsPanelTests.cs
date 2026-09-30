using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Session;
using Rex.Mirror.Views;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>The Controls tab's tiles: which there are, what they show and say, and how they behave while off.</summary>
public sealed class ControlsPanelTests
{
    [Fact]
    public void EachSectionHoldsTheTilesThatBelongToIt()
    {
        Assert.Equal(12, ControlsPanel.PhoneTileIds.Length);
        Assert.Contains("mute", ControlsPanel.PhoneTileIds);

        // The picture on this PC, not the phone: turning the phone lives under phone orientation,
        // and scrcpy's FPS counter prints to a console nobody can see from here.
        Assert.Equal(["rotate-left", "rotate-right", "pause", "reset-capture", "screenshot", "fullscreen"], ControlsPanel.ViewTileIds);
        Assert.DoesNotContain("fps", ControlsPanel.ViewTileIds);
        Assert.DoesNotContain("rotate-device", ControlsPanel.ViewTileIds);
        Assert.DoesNotContain("resume", ControlsPanel.ViewTileIds);

        // Six gestures, three by two; mute lives under PHONE, and browse and the layout are buttons.
        Assert.Equal(6, ControlsPanel.GestureTileIds.Length);
        Assert.All(ControlsPanel.GestureTileIds, id => Assert.True(MirrorActions.IsGesture(id), id));
        Assert.Empty(ControlsPanel.PhoneTileIds.Intersect(ControlsPanel.GestureTileIds));

        // The command line keeps every action, the FPS counter included.
        Assert.NotNull(MirrorActions.Find("fps"));
    }

    [Fact]
    public void TheViewTilesSayTheyTurnTheViewAndTheButtonThatTurnsThePhoneSaysSo()
    {
        Assert.Equal("Turn view left", MirrorActions.Find("rotate-left")!.Label);
        Assert.Equal("Turn view right", MirrorActions.Find("rotate-right")!.Label);
        Assert.Equal("Rotate the phone", MirrorActions.Find("rotate-device")!.Label);
        Wpf.Run(() => Assert.Equal("Rotate the phone 90°", new ControlsPanel().RotateDevice.Content));
    }

    [Fact]
    public void NoTwoActionsShowTheSameIconAndEveryIconExists()
    {
        var theme = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Rex.Mirror", "Theme.xaml"));
        var geometries = Regex.Matches(theme, "<Geometry x:Key=\"([A-Za-z]+)\">").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        Assert.All(ActionIcons.All, pair => Assert.Contains(pair.Value, geometries));
        Assert.All(ActionIcons.All, pair => Assert.NotNull(MirrorActions.Find(pair.Key)));
        var shared = ActionIcons.All.GroupBy(pair => pair.Value).Where(g => g.Count() > 1).Select(g => string.Join(" and ", g.Select(p => p.Key))).ToArray();
        Assert.Empty(shared);

        // Every tile in the panel has a picture of its own.
        Assert.All(ControlsPanel.PhoneTileIds.Concat(ControlsPanel.ViewTileIds).Concat(ControlsPanel.GestureTileIds).Append("resume"),
            id => Assert.NotNull(ActionIcons.For(id)));
    }

    [Theory]
    [InlineData("swipe-up", "Down")]
    [InlineData("swipe-down", "Up")]
    [InlineData("swipe-left", "Right")]
    [InlineData("swipe-right", "Left")]
    public void AFeedTilesArrowPointsTheWayItsKeyDoes(string action, string key)
    {
        Assert.Equal(key, Shortcuts.BrowseKey(action));
        Assert.Equal("IconArrow" + key, ActionIcons.For(action));
    }

    [Fact]
    public void TilesSayWhatTheyDoInPlainWordsAndEndWithTheirKeys()
    {
        Wpf.Run(() =>
        {
            var panel = new ControlsPanel();
            var tiles = Tiles(panel.NavigationTiles).Concat(Tiles(panel.ViewTiles)).Concat(Tiles(panel.GestureTiles)).ToArray();
            Assert.All(tiles, tile =>
            {
                Assert.DoesNotContain("KEYCODE", tile.Tip, StringComparison.Ordinal);
                Assert.Equal(Shortcuts.Tip(MirrorActions.Find(tile.Id)!.Detail, tile.Id), tile.Tip);
                Assert.Equal("tile-" + tile.Id, tile.AutomationId);
            });

            Assert.Equal("Raise the volume", tiles.Single(t => t.Id == "volume-up").Tip);
            Assert.Equal("Go to the home screen · Ctrl+Alt+H", tiles.Single(t => t.Id == "home").Tip);
            Assert.EndsWith("· Ctrl+Alt+Down · Down in browse mode", tiles.Single(t => t.Id == "swipe-up").Tip, StringComparison.Ordinal);
            Assert.EndsWith("· Right in browse mode", tiles.Single(t => t.Id == "swipe-left").Tip, StringComparison.Ordinal);
            Assert.EndsWith("· M in browse mode", tiles.Single(t => t.Id == "mute").Tip, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ALongLabelTrimsInsteadOfMakingEveryRowTaller()
    {
        Wpf.Run(() =>
        {
            var panel = Wpf.Layout(new ControlsPanel(), 240);
            var tiles = Wpf.Visuals(panel.NavigationTiles).OfType<Button>().ToArray();
            Assert.Equal(12, tiles.Length);
            Assert.Single(tiles.Select(t => Math.Round(t.ActualHeight)).Distinct());
            Assert.True(tiles[0].ActualHeight < 66, $"A tile is {tiles[0].ActualHeight} DIP tall, so a label wrapped.");

            var labels = tiles.SelectMany(t => Wpf.Visuals(t).OfType<TextBlock>()).ToArray();
            Assert.All(labels, label =>
            {
                Assert.Equal(TextWrapping.NoWrap, label.TextWrapping);
                Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
            });

            // The outer tiles line up with the rest of the panel, not 3 DIP inside it.
            Assert.Equal(0, tiles[0].TranslatePoint(new Point(), panel).X, 1);
            Assert.Equal(240, tiles[2].TranslatePoint(new Point(tiles[2].ActualWidth, 0), panel).X, 1);
        });
    }

    [Fact]
    public void SegmentsAndClipboardButtonsAreTheSameWidth()
    {
        Wpf.Run(() =>
        {
            var panel = Wpf.Layout(new ControlsPanel(), 300);
            Assert.Single(new[] { panel.RotationPortrait, panel.RotationLandscape, panel.RotationAuto }.Select(s => Math.Round(s.ActualWidth)).Distinct());
            Assert.Single(new[] { panel.ClipboardCopy, panel.ClipboardCut, panel.ClipboardPaste, panel.ClipboardType }.Select(b => Math.Round(b.ActualWidth)).Distinct());
            Assert.Equal(0, panel.RotationPortrait.TranslatePoint(new Point(), panel).X, 1);
            Assert.Equal(300, panel.ClipboardType.TranslatePoint(new Point(panel.ClipboardType.ActualWidth, 0), panel).X, 1);
        });
    }

    [Fact]
    public void ATileThatIsOffSaysWhy()
    {
        Wpf.Run(() =>
        {
            var panel = Wpf.Layout(new ControlsPanel(), 300);
            var home = Wpf.Visuals(panel.NavigationTiles).OfType<Button>().First();
            Assert.Equal("Go to the home screen · Ctrl+Alt+H", home.ToolTip);
            Assert.Equal("Home", AutomationProperties.GetName(home));
            Assert.True(ToolTipService.GetShowOnDisabled(home));

            panel.NavigationTiles.IsEnabled = false;
            Wpf.Layout(panel, 300);
            Assert.Equal(ControlsPanel.NeedsPhone, home.ToolTip);
            Assert.Equal(ControlsPanel.NeedsPhone, AutomationProperties.GetHelpText(home));
        });
    }

    [Fact]
    public void ThePauseTileTurnsIntoResumeWhereItStands()
    {
        Assert.Equal("pause", ControlsPanel.PauseTileAction(paused: false));
        Assert.Equal("resume", ControlsPanel.PauseTileAction(paused: true));

        Wpf.Run(() =>
        {
            var tile = new ActionTileModel(MirrorActions.Find("pause")!, Geometry.Empty, "tile-pause");
            var changed = 0;
            tile.PropertyChanged += (_, _) => changed++;
            tile.Show(MirrorActions.Find("resume")!, Geometry.Empty);
            Assert.Equal(1, changed);
            Assert.Equal("resume", tile.Id);
            Assert.Equal("Resume", tile.Label);
            Assert.Equal("tile-pause", tile.AutomationId);

            // Showing what it already shows changes nothing, so a refresh costs no redraw.
            tile.Show(MirrorActions.Find("resume")!, Geometry.Empty);
            Assert.Equal(1, changed);
        });
    }

    [Fact]
    public void TheSessionButtonSaysWhatPressingItWillDo()
    {
        Assert.Equal(("Stop mirror", "Close the mirror and keep waiting in the tray", true), ControlsPanel.SessionButtonState(true, SessionPhase.Mirroring));
        Assert.Equal(("Start mirror", "Open the mirror again", true), ControlsPanel.SessionButtonState(false, SessionPhase.Stopped));
        Assert.False(ControlsPanel.SessionButtonState(false, SessionPhase.Waiting).Enabled);
        Assert.False(ControlsPanel.SessionButtonState(false, SessionPhase.Starting).Enabled);
        Assert.DoesNotContain("Close", ControlsPanel.SessionButtonState(false, SessionPhase.Stopped).Tip, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowseKeysInTheRegistryAreTheOnesBrowseModeAnswers()
    {
        var keys = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Down"] = 0x28, ["Up"] = 0x26, ["Right"] = 0x27, ["Left"] = 0x25, ["Enter"] = 0x0D, ["L"] = 'L', ["M"] = 'M', ["Backspace"] = 0x08,
        };
        Assert.All(Shortcuts.BrowseKeys, shortcut =>
        {
            Assert.NotNull(shortcut.Action);
            Assert.Equal(shortcut.Action, KeyboardBrowse.ActionFor(keys[shortcut.Gesture]));
        });
    }

    private static ActionTileModel[] Tiles(ItemsControl tiles) => tiles.ItemsSource.Cast<ActionTileModel>().ToArray();
}
