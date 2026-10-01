namespace Rex.Core;

/// <summary>
/// Lets one piece of work in at a time, and can be closed: closing turns new work away and waits
/// for the work already inside to leave, so whatever that work uses can then be released safely.
///
/// It exists for a resource used on a worker thread and released from another: releasing it while
/// the worker is still inside is a crash (a freed graphics device read from), and a semaphore that is
/// disposed while held fails the worker that later releases it.
/// </summary>
public sealed class WorkGate
{
    private readonly object _lock = new();
    private bool _busy;
    private bool _closed;

    /// <summary>Lets the caller in when nothing else is inside and the gate is open.</summary>
    public bool TryEnter()
    {
        lock (_lock)
        {
            if (_busy || _closed)
            {
                return false;
            }

            _busy = true;
            return true;
        }
    }

    /// <summary>Leaves. Safe to call after <see cref="Close"/>, and from any thread.</summary>
    public void Exit()
    {
        lock (_lock)
        {
            _busy = false;
            Monitor.PulseAll(_lock);
        }
    }

    public bool IsClosed
    {
        get
        {
            lock (_lock)
            {
                return _closed;
            }
        }
    }

    /// <summary>
    /// Turns new work away and waits for the work inside to leave. True once nothing is inside; false
    /// when the work is still inside at the timeout, in which case what it uses must be left alone.
    /// </summary>
    public bool Close(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        lock (_lock)
        {
            _closed = true;
            while (_busy)
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero)
                {
                    return false;
                }

                Monitor.Wait(_lock, left);
            }

            return true;
        }
    }
}
