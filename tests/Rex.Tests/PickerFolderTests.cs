using Rex.Mirror.Views;

namespace Rex.Tests;

/// <summary>Where a folder picker starts: a missing folder is made first, an impossible one gives way.</summary>
public sealed class PickerFolderTests
{
    [Fact]
    public void AMissingFolderIsMadeAndAnImpossibleOneGivesWay()
    {
        var root = Path.Combine(Path.GetTempPath(), "rex-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Not there yet, as on a fresh install: made, and the picker starts in it.
            var recordings = Path.Combine(root, "captures", "recordings");
            Assert.Equal(recordings, PickerFolder.Ready(recordings, root));
            Assert.True(Directory.Exists(recordings));

            // Already there: left alone.
            Assert.Equal(recordings, PickerFolder.Ready(recordings, root));

            // A name Windows refuses gives way to a folder that is always there.
            Assert.Equal(root, PickerFolder.Ready(Path.Combine(root, "captures", "<no such folder>"), root));

            // So does a folder whose place a plain file already holds.
            var taken = Path.Combine(root, "taken");
            File.WriteAllText(taken, string.Empty);
            Assert.Equal(root, PickerFolder.Ready(taken, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
