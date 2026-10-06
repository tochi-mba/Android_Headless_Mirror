using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Rex.Core;
using Rex.Mirror.Views;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The Controls tab as the person wants it: the sections in their order and the hidden ones out of
/// sight, the grids holding the tiles chosen in the order chosen, so many to a row, with or without
/// names; config.json kept to sections and actions the app knows; and the fullscreen controls in
/// the order they were picked when that is asked for.
/// </summary>
public sealed class ControlsTabTests
{
    [Fact]
    public void ConfigJsonKeepsKnownSectionsAndActionsEachOnceInOrder()
    {
        var layout = new ControlsSettings
        {
            Sections = ["VIEW", "phone", "nowhere", "phone", null!],
            PhoneTiles = ["Back", "home", "back", "teleport"],
            ViewTiles = null!,
            GestureTiles = [.. Enumerable.Repeat(MirrorActions.Ids, 2).SelectMany(ids => ids)],
            Columns = 9,
        };
        layout.Normalize();
        Assert.Equal(["view", "phone"], layout.Sections);
        Assert.Equal(["back", "home"], layout.PhoneTiles);
        Assert.Empty(layout.ViewTiles);
        Assert.Equal(ControlsSettings.MostTiles, layout.GestureTiles.Count);
        Assert.Equal(ControlsSettings.MostColumns, layout.Columns);

        layout.Columns = 0;
        layout.Normalize();
        Assert.Equal(ControlsSettings.FewestColumns, layout.Columns);
    }

    [Fact]
    public void TheSectionsListShowsTheShownOnesInOrderThenTheHiddenOnes()
    {
        var rows = new ControlsSettings { Sections = ["view", "apps"] }.SectionRows();
        Assert.Equal(("view", "View", true), rows[0]);
        Assert.Equal(("apps", "Apps", true), rows[1]);
        Assert.Equal(ControlsSettings.AllSections.Count, rows.Count);
        Assert.All(rows.Skip(2), row => Assert.False(row.Shown));
        Assert.Equal("screen", rows[2].Id);
    }

    [Theory]
    [InlineData("b", -1, "b,a,c")]
    [InlineData("b", 1, "a,c,b")]
    [InlineData("a", -1, "a,b,c")]
    [InlineData("c", 5, "a,b,c")]
    [InlineData("z", 1, "a,b,c")]
    public void AnItemMovesInsideItsList(string id, int by, string after) =>
        Assert.Equal(after.Split(','), ControlsSettings.Moved(["a", "b", "c"], id, by));

    [Fact]
    public void ACopyOfTheLayoutIsItsOwn()
    {
        var layout = new ControlsSettings();
        var copy = layout.Copy();
        copy.Sections.Clear();
        copy.PhoneTiles.Add("fps");
        Assert.Equal(ControlsSettings.AllSections.Count, layout.Sections.Count);
        Assert.DoesNotContain("fps", layout.PhoneTiles);
    }

    [Fact]
    public void TheFloatingControlsKeepThePickedOrderOnlyWhenAsked()
    {
        var hud = new HudSettings { Buttons = ["fullscreen", "home", "home", "nothing"] };
        hud.Normalize();
        Assert.Equal(["home", "fullscreen"], hud.Buttons);

        hud = new HudSettings { Buttons = ["fullscreen", "home", "home", "nothing"], KeepOrder = true };
        hud.Normalize();
        Assert.Equal(["fullscreen", "home"], hud.Buttons);
    }

    [Fact]
    public void TheTabFollowsItsLayout() => Wpf.Run(() =>
    {
        var panel = Wpf.Layout(new ControlsPanel(), 320);
        panel.ApplyLayout(new ControlsSettings
        {
            Sections = ["view", "phone"],
            PhoneTiles = ["mute", "home"],
            ViewTiles = ["screenshot"],
            GestureTiles = [],
            Columns = 2,
            TileLabels = false,
        });
        panel.UpdateLayout();

        // The shown sections come first, in their order, and every other one is out of sight.
        var hosts = panel.Sections.Children.OfType<StackPanel>().Where(s => s.Name.StartsWith("Host", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["HostView", "HostPhone"], hosts.Take(2).Select(h => h.Name));
        Assert.All(hosts.Skip(2), host => Assert.Equal(Visibility.Collapsed, host.Visibility));
        Assert.All(hosts.Take(2), host => Assert.Equal(Visibility.Visible, host.Visibility));

        var phone = panel.NavigationTiles.Items.Cast<ActionTileModel>().Select(t => t.Id).ToArray();
        Assert.Equal(["mute", "home"], phone);
        Assert.Equal(["screenshot"], panel.ViewTiles.Items.Cast<ActionTileModel>().Select(t => t.Id));
        Assert.Empty(panel.GestureTiles.Items);
        Assert.Same(panel.FindResource("TileIconOnly"), panel.NavigationTiles.ItemTemplate);
        var grid = Wpf.Visuals(panel.NavigationTiles).OfType<UniformGrid>().First();
        Assert.Equal(2, grid.Columns);

        // Back as it ships: every section, the shipped tiles, three to a row with their names.
        panel.ApplyLayout(new ControlsSettings());
        panel.UpdateLayout();
        Assert.Equal(ControlsPanel.PhoneTileIds, panel.NavigationTiles.Items.Cast<ActionTileModel>().Select(t => t.Id));
        Assert.Same(panel.FindResource("Tile"), panel.NavigationTiles.ItemTemplate);
        Assert.Equal(3, Wpf.Visuals(panel.NavigationTiles).OfType<UniformGrid>().First().Columns);
        return true;
    });

    [Fact]
    public void EveryActionThatCanBeATileHasAPictureOfItsOwn()
    {
        var theme = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Rex.Mirror", "Theme.xaml"));
        Assert.All(MirrorActions.All, action => Assert.NotNull(ActionIcons.TileFor(action.Id)));
        Assert.All(ActionIcons.TileOnlyIcons.Values, key => Assert.Contains($"<Geometry x:Key=\"{key}\">", theme, StringComparison.Ordinal));
        var pictures = MirrorActions.All.Select(a => ActionIcons.TileFor(a.Id)).ToArray();
        Assert.Equal(pictures.Length, pictures.Distinct().Count());
    }
}
