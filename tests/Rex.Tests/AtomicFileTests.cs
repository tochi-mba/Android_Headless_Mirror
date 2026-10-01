using Rex.Core;

namespace Rex.Tests;

/// <summary>
/// Replacing config.json while something else reads it. Windows refuses to replace a file another
/// handle has open; a test polling config.json once made "Reset everything" crash the app that way.
/// A write now waits out a reader for a moment.
/// </summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("rex-atomic-").FullName;

    private string File(string name) => Path.Combine(_folder, name);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task AWriteWaitsOutABriefReader()
    {
        var path = File("config.json");
        System.IO.File.WriteAllText(path, "old");
        var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var write = Task.Run(() => AtomicFile.Write(path, "new", keepBackupAt: File("config.json.rex-backup"), validate: null), TestContext.Current.CancellationToken);
        // Elapsed time is the behaviour here: the reader lets go well inside the write's patience.
        await Task.Delay(150, TestContext.Current.CancellationToken);
        await reader.DisposeAsync();
        await write;

        Assert.Equal("new", System.IO.File.ReadAllText(path));
        Assert.Equal("old", System.IO.File.ReadAllText(File("config.json.rex-backup")));
    }

    [Fact]
    public void AWriteGivesUpOnAReaderThatNeverLetsGoAndLeavesNothingBehind()
    {
        var path = File("config.json");
        System.IO.File.WriteAllText(path, "old");
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var refused = Record.Exception(() => AtomicFile.Write(path, "new", keepBackupAt: null, validate: null));

        Assert.True(refused is IOException or UnauthorizedAccessException, refused?.ToString());
        Assert.Equal(["config.json"], Directory.GetFiles(_folder).Select(Path.GetFileName));
    }
}
