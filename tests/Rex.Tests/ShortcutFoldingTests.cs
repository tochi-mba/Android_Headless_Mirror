using Rex.Core;

namespace Rex.Tests;

/// <summary>Numbered key runs folded for reading, and lock modes in words.</summary>
public sealed class ShortcutFoldingTests
{
    [Fact]
    public void ANumberedRunFoldsOnlyWhileItFollowsItsPattern()
    {
        var all = Shortcuts.All.Where(s => s.IsKey && !s.Browse && !s.Global).ToList();
        var shipped = Shortcuts.Folded(all);
        var favourites = Assert.Single(shipped, s => s.Id == Shortcuts.FavouritePrefix + "1-9");
        Assert.Equal("Ctrl+Alt+Shift+1 to 9", favourites.Gesture);
        Assert.Equal("Open favourite apps 1 to 9", favourites.Description);
        var profiles = Assert.Single(shipped, s => s.Id == Shortcuts.ProfilePrefix + "1-9");
        Assert.Equal("Ctrl+Alt+F1 to F9", profiles.Gesture);
        Assert.Equal("Apply profiles 1 to 9", profiles.Description);
        Assert.DoesNotContain(shipped, s => Shortcuts.Favourite(s.Id) > 0 || Shortcuts.ProfileNumber(s.Id) > 0);

        // The folded row takes the run's place, so the list keeps its order.
        var before = all[all.FindIndex(s => s.Id == Shortcuts.FavouritePrefix + "1") - 1].Id;
        Assert.Equal(before, shipped[shipped.ToList().FindIndex(s => s.Id == favourites.Id) - 1].Id);

        // One key of a run moved, or one missing: that run is listed one by one, the other still folds.
        var moved = Shortcuts.Folded(all.Select(s => s.Id == Shortcuts.ProfilePrefix + "4" ? s with { Gesture = "Ctrl+Alt+Q" } : s));
        Assert.Equal(9, moved.Count(s => Shortcuts.ProfileNumber(s.Id) > 0));
        Assert.Contains(moved, s => s.Id == Shortcuts.FavouritePrefix + "1-9");
        var missing = Shortcuts.Folded(all.Where(s => s.Id != Shortcuts.FavouritePrefix + "9"));
        Assert.Equal(8, missing.Count(s => Shortcuts.Favourite(s.Id) > 0));
        Assert.Contains(missing, s => s.Id == Shortcuts.ProfilePrefix + "1-9");

        // Nothing numbered in it: the list comes back as it was.
        var plain = all.Where(s => Shortcuts.Favourite(s.Id) == 0 && Shortcuts.ProfileNumber(s.Id) == 0).ToList();
        Assert.Equal(plain, Shortcuts.Folded(plain));
    }

    [Theory]
    [InlineData(LockScreenModes.Pattern, "Pattern")]
    [InlineData(LockScreenModes.Other, "PIN, password or other")]
    [InlineData(LockScreenModes.None, "No lock")]
    [InlineData("", "Not asked yet")]
    [InlineData(null, "Not asked yet")]
    public void ALockModeIsSaidInWords(string? mode, string words) =>
        Assert.Equal(words, LockScreenModes.Describe(mode));
}
