using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class AndroidShellRunnerTests
{
    [Theory]
    [InlineData("/data/user/0/com.example", "/data/user/0/com.example")]
    [InlineData("simple-value", "simple-value")]
    [InlineData("", "''")]
    [InlineData("hello world", "'hello world'")]
    [InlineData("a'b", "'a'\"'\"'b'")]
    [InlineData("$(id)", "'$(id)'")]
    [InlineData("a;reboot", "'a;reboot'")]
    public void Quote_ProducesSingleShellArgument(string input, string expected)
    {
        Assert.Equal(expected, AndroidShellQuoting.Quote(input));
    }

    [Fact]
    public void BuildCommand_QuotesEveryArgument()
    {
        var command = AndroidShellQuoting.BuildCommand(
            "cat",
            new[] { "/data/user/0/hello world/file.txt" });

        Assert.Equal("cat '/data/user/0/hello world/file.txt'", command);
    }

    [Fact]
    public async Task Run_NormalCommand_UsesAdbShellWithoutSu()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();
        runner.Result = new ProcessResult(0, "2000\n", "");
        var shell = new AndroidShellRunner(
            runner,
            package.Root,
            new RootPolicy(package.Config));

        var result = await shell.RunAsync(
            "adb.exe",
            "USB123",
            new PrivilegedCommand("id", "id", new[] { "-u" }),
            rootMode: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("adb.exe", call.FileName);
        Assert.Equal(
            new[] { "-s", "USB123", "shell", "sh", "-c", "id -u" },
            call.Arguments);
    }

    [Fact]
    public async Task Run_PrivilegedCommand_UsesSuCommandBoundary()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();
        runner.Result = new ProcessResult(0, "0\n", "");
        var shell = new AndroidShellRunner(
            runner,
            package.Root,
            new RootPolicy(package.Config));

        var result = await shell.RunAsync(
            "adb.exe",
            "USB123",
            new PrivilegedCommand("root.id", "id", new[] { "-u" }),
            RootExecutionMode.Su,
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        var call = Assert.Single(runner.Calls);
        Assert.Equal(
            new[] { "-s", "USB123", "shell", "su", "-c", "id -u" },
            call.Arguments);
    }

    [Fact]
    public async Task Run_QuotesUserControlledArgumentsBeforeSu()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();
        runner.Result = new ProcessResult(0, "", "");
        var shell = new AndroidShellRunner(
            runner,
            package.Root,
            new RootPolicy(package.Config));

        await shell.RunAsync(
            "adb.exe",
            "USB123",
            new PrivilegedCommand(
                "root.files",
                "cat",
                new[] { "/data/user/0/a;reboot" }),
            RootExecutionMode.Su,
            TestContext.Current.CancellationToken);

        var call = Assert.Single(runner.Calls);
        Assert.Equal("cat '/data/user/0/a;reboot'", call.Arguments[^1]);
    }

    [Fact]
    public async Task Run_TruncatesBoundedOutput()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, new string('x', 100), "")
        };
        var shell = new AndroidShellRunner(
            runner,
            package.Root,
            new RootPolicy(package.Config));

        var result = await shell.RunAsync(
            "adb.exe",
            "USB123",
            new PrivilegedCommand(
                "bounded",
                "cat",
                new[] { "/proc/cpuinfo" },
                MaxOutputCharacters: 16),
            rootMode: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Truncated);
        Assert.Contains("[REX output truncated]", result.StdOut);
        Assert.True(result.StdOut.Length < 100);
    }
}
