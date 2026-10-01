namespace Rex.Core;

/// <summary>What the copies should do next.</summary>
public enum CopyStepKind
{
    /// <summary>Nothing to do right now.</summary>
    Wait,

    /// <summary>Start the copy with this index.</summary>
    Launch,

    /// <summary>Stop the copy with this index.</summary>
    Stop,
}

public readonly record struct CopyStep(CopyStepKind Kind, int Index)
{
    public static readonly CopyStep Nothing = new(CopyStepKind.Wait, -1);
}

/// <summary>
/// Decides which copy of the phone to start or stop next, one step at a time.
///
/// Every copy is its own scrcpy session against the same phone, and sessions started at the same
/// moment race: they push the same server file to the phone and can bind the same local port. So
/// copies start one after another, only once the main picture is up, and never while another is
/// still starting. A copy that fails to start twice in a row is given up on (the phone may have no
/// encoder left for it) instead of being retried for ever. This class holds that policy and no
/// processes, so it is tested without a phone.
/// </summary>
public sealed class CopiesPlan
{
    /// <summary>Failed starts in a row after which a copy is given up.</summary>
    public const int MaxAttempts = 2;

    private readonly SortedSet<int> _running = [];
    private readonly Dictionary<int, int> _failures = [];
    private int? _starting;

    /// <summary>How many copies the person wants, not counting the phone's own view.</summary>
    public int Wanted { get; private set; }

    public IReadOnlyCollection<int> Running => _running;

    public bool Starting => _starting is not null;

    /// <summary>Raised with a reason when a copy is given up on.</summary>
    public event Action<int, string>? GaveUp;

    public void Want(int copies) => Wanted = Math.Max(0, copies);

    /// <summary>
    /// How many copies there is room to show. The rest wait, stopped, until there is room again:
    /// a copy nobody can see still costs the phone an encoder and the PC a decoder.
    /// </summary>
    public int Room { get; set; } = int.MaxValue;

    /// <summary>The copies that should be running now: what is wanted, as far as there is room.</summary>
    public int Target => Math.Min(Wanted, Math.Max(0, Room));

    /// <summary>The next step, given whether the main picture is up.</summary>
    public CopyStep Next(bool mainIsMirroring)
    {
        if (!mainIsMirroring)
        {
            // Without the main session there is nothing to copy; everything stops, and what was
            // wanted is kept for when the phone comes back.
            return _running.Count > 0 ? new CopyStep(CopyStepKind.Stop, _running.Max) : CopyStep.Nothing;
        }

        if (_running.Count > Target)
        {
            return new CopyStep(CopyStepKind.Stop, _running.Max);
        }

        // One start at a time, and none once enough are running.
        if (_starting is not null || _running.Count >= Target)
        {
            return CopyStep.Nothing;
        }

        var index = Enumerable.Range(0, Wanted).First(i => !_running.Contains(i));
        return new CopyStep(CopyStepKind.Launch, index);
    }

    public void Started(int index) => _starting = index;

    /// <summary>The copy's window appeared; it counts as running from now on.</summary>
    public void Ready(int index)
    {
        if (_starting == index)
        {
            _starting = null;
        }

        _failures.Remove(index);
        _running.Add(index);
    }

    /// <summary>
    /// The copy could not start, or ended on its own. After <see cref="MaxAttempts"/> failures in a
    /// row it is given up on: one fewer copy is wanted and <see cref="GaveUp"/> says why.
    /// </summary>
    public void Failed(int index, string reason)
    {
        if (_starting == index)
        {
            _starting = null;
        }

        _running.Remove(index);
        var failures = _failures.GetValueOrDefault(index) + 1;
        _failures[index] = failures;
        if (failures >= MaxAttempts)
        {
            _failures.Remove(index);
            Wanted = Math.Max(0, Wanted - 1);
            GaveUp?.Invoke(index, reason);
        }
    }

    /// <summary>The copy was stopped on purpose (removed, or the main session ended).</summary>
    public void Stopped(int index)
    {
        if (_starting == index)
        {
            _starting = null;
        }

        _running.Remove(index);
    }
}
