using Rex.Core;

namespace Rex.Tests;

public sealed class WorkGateTests
{
    [Fact]
    public void OnlyOnePieceOfWorkIsInsideAtATime()
    {
        var gate = new WorkGate();

        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
        gate.Exit();
        Assert.True(gate.TryEnter());
        gate.Exit();
        Assert.False(gate.IsClosed);
    }

    [Fact]
    public async Task AClosedGateWaitsForTheWorkInsideIt()
    {
        var gate = new WorkGate();
        Assert.True(gate.TryEnter());
        using var leave = new ManualResetEventSlim();
        var worker = Task.Run(() =>
        {
            leave.Wait(TestContext.Current.CancellationToken);
            gate.Exit();
        }, TestContext.Current.CancellationToken);

        var closing = Task.Run(() => gate.Close(TimeSpan.FromSeconds(30)), TestContext.Current.CancellationToken);
        Assert.True(SpinWait.SpinUntil(() => gate.IsClosed, TimeSpan.FromSeconds(30)));

        Assert.False(closing.IsCompleted);
        Assert.False(gate.TryEnter());
        leave.Set();

        Assert.True(await closing);
        await worker;
        Assert.False(gate.TryEnter());
    }

    [Fact]
    public void AClosedGateGivesUpOnWorkThatNeverLeaves()
    {
        var gate = new WorkGate();
        Assert.True(gate.TryEnter());

        Assert.False(gate.Close(TimeSpan.FromMilliseconds(50)));
        Assert.True(gate.IsClosed);
    }

    [Fact]
    public void ExitingAfterCloseNeverThrows()
    {
        var gate = new WorkGate();
        Assert.True(gate.TryEnter());
        Assert.False(gate.Close(TimeSpan.Zero));

        gate.Exit();

        Assert.True(gate.Close(TimeSpan.Zero));
        Assert.False(gate.TryEnter());
    }
}
