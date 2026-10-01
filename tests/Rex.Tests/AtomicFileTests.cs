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
    public void AWriteWaitsOutABriefReader()
    {
        var path = File("config.json");
        System.IO.File.WriteAllText(path, "old");
        var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        // The write and the reader each have a thread of their own. On a busy runner the thread
        // pool can be slow to hand out a thread, and a reader released from it once let go after
        // the write had already given up.
        Exception? failure = null;
        var write = new Thread(() =>
        {
            try
            {
                AtomicFile.Write(path, "new", keepBackupAt: File("config.json.rex-backup"), validate: null);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        write.Start();

        // Elapsed time is the behaviour here: the reader lets go well inside the write's patience.
        Thread.Sleep(150);
        reader.Dispose();
        Assert.True(write.Join(TimeSpan.FromSeconds(10)), "The write never finished.");

        Assert.Null(failure);
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
