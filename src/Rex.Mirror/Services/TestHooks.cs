namespace Rex.Mirror.Services;

/// <summary>
/// Pipe commands that stand in for things a test cannot do for real, such as an OLE drag from File
/// Explorer. They exist only when REX_TEST_HOOKS is 1, which the test harness sets, and never in a
/// person's app.
/// </summary>
public static class TestHooks
{
    public const string Variable = "REX_TEST_HOOKS";

    public static bool Enabled => Environment.GetEnvironmentVariable(Variable) == "1";
}
