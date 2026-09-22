using System.Text;
using Rex.Tests.Support;

namespace Rex.Tests;

public sealed class TestInfrastructureTests
{
    [Fact]
    public void LiveAdbLogCanBeReadWhileTheFakeToolIsAppending()
    {
        using var package = new TestPackage();
        Directory.CreateDirectory(package.ToolsFolder);
        using var writer = new FileStream(package.FakeAdbLog, FileMode.Append, FileAccess.Write, FileShare.Read);
        writer.Write(Encoding.UTF8.GetBytes("devices -l\n"));
        writer.Flush();
        Assert.Equal(["devices -l"], package.AdbCalls());
    }
}
