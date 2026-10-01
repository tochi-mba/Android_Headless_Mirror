namespace Rex.Core;

/// <summary>
/// Lets one scrcpy server start at a time, whatever starts it: the mirror, a copy, or reading the
/// phone's apps. Each scrcpy copies its server onto the phone and starts it there, and two doing so
/// at once can overwrite the file the other is starting. Whoever enters holds it until its window
/// is up (or, for the app list, until scrcpy has exited), and leaves by disposing what it got.
/// </summary>
public sealed class ServerStartGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>True while someone is starting a server.</summary>
    public bool Busy => _gate.CurrentCount == 0;

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Leave(_gate);
    }

    private sealed class Leave(SemaphoreSlim gate) : IDisposable
    {
        private int _left;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _left, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
