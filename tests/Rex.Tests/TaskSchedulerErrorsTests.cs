using System.Runtime.InteropServices;
using Rex.Core;

namespace Rex.Tests;

/// <summary>
/// How Task Scheduler's answers become the app's: a task that is not there is simply not there,
/// whatever exception the late-bound call raised for it, and every other refusal is one the app
/// already reports. Nothing here reaches the real Task Scheduler.
/// </summary>
public sealed class TaskSchedulerErrorsTests
{
    public static TheoryData<Exception, bool> Missing => new()
    {
        { new FileNotFoundException("The system cannot find the file specified."), true },
        { new DirectoryNotFoundException(), true },
        { new COMException("missing", unchecked((int)0x80070002)), true },
        { new COMException("missing folder", unchecked((int)0x80070003)), true },
        { new COMException("denied", unchecked((int)0x80070005)), false },
        { new IOException("busy"), false },
    };

    [Theory]
    [MemberData(nameof(Missing))]
    public void ATaskThatIsNotThereIsKnownWhateverItArrivesAs(Exception error, bool missing) =>
        Assert.Equal(missing, WindowsTaskScheduler.IsMissing(error));

    [Fact]
    public void EveryOtherRefusalIsOneTheAppReports()
    {
        Assert.Equal(7, WindowsTaskScheduler.Guard(() => 7));

        var lost = Assert.Throws<InvalidOperationException>(() => WindowsTaskScheduler.Guard<int>(() => throw new FileNotFoundException("gone")));
        Assert.StartsWith("Task Scheduler reported 0x80070002", lost.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => WindowsTaskScheduler.Guard<int>(() => throw new COMException("odd", unchecked((int)0x80041318))));
        Assert.Throws<InvalidOperationException>(() => WindowsTaskScheduler.Guard<int>(() => throw new Microsoft.CSharp.RuntimeBinder.RuntimeBinderException("no such member")));

        var denied = Assert.Throws<UnauthorizedAccessException>(() => WindowsTaskScheduler.Guard<int>(() => throw new COMException("denied", unchecked((int)0x80070005))));
        Assert.StartsWith("Task Scheduler refused access", denied.Message, StringComparison.Ordinal);
        var already = new UnauthorizedAccessException("as it was");
        Assert.Same(already, Assert.Throws<UnauthorizedAccessException>(() => WindowsTaskScheduler.Guard<int>(() => throw already)));

        // A mistake of the app's own is not dressed up as Task Scheduler's.
        Assert.Throws<ArgumentException>(() => WindowsTaskScheduler.Guard<int>(() => throw new ArgumentException("bug")));
    }
}
