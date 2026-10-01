namespace Rex.Core;

/// <summary>One transfer as it moves from the waiting list to a finished state.</summary>
public sealed record TransferJob(Guid Id, TransferItem Item)
{
    internal List<(TimeSpan Elapsed, long Bytes)> Samples { get; } = [];
    public TransferState State { get; internal set; } = TransferState.Waiting;
    public long Sent { get; internal set; }
    public string? Why { get; internal set; }
    public DateTimeOffset ChangedAt { get; internal set; } = DateTimeOffset.UtcNow;

    public int Percent => TransferProgress.Percent(Sent, Item.Entry.Size);
    public double BytesPerSecond => TransferProgress.Speed(Samples);
    public TimeSpan? TimeLeft => TransferProgress.Left(Sent, Item.Entry.Size, BytesPerSecond);
}

/// <summary>
/// The deterministic part of sending files. The caller performs the ADB work; this class decides
/// which jobs may start, records their results, retries them and keeps only the requested history.
/// </summary>
public sealed class TransferQueue
{
    private readonly List<TransferJob> _jobs = [];
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private int _atOnce;
    private int _history;

    public TransferQueue(int atOnce, int history)
    {
        Configure(atOnce, history);
    }

    public IReadOnlyList<TransferJob> Jobs => _jobs;
    public int Running => _jobs.Count(job => job.State is TransferState.Sending or TransferState.Installing);

    public void Configure(int atOnce, int history)
    {
        _atOnce = Math.Clamp(atOnce, 1, TransferSettings.MostAtOnce);
        _history = Math.Clamp(history, 0, TransferSettings.MostHistory);
        Trim();
    }

    public IReadOnlyList<TransferJob> Add(IEnumerable<TransferItem> items)
    {
        var added = items.Select(item => new TransferJob(Guid.NewGuid(), item)).ToArray();
        _jobs.AddRange(added);
        return added;
    }

    /// <summary>Starts as many waiting jobs as there is room for and returns them in queue order.</summary>
    public IReadOnlyList<TransferJob> Take()
    {
        var room = Math.Max(0, _atOnce - Running);
        var jobs = _jobs.Where(job => job.State == TransferState.Waiting).Take(room).ToArray();
        foreach (var job in jobs)
        {
            Change(job, job.Item.Kind == TransferKind.Install ? TransferState.Installing : TransferState.Sending);
            job.Samples.Add((_clock.Elapsed, 0));
        }

        return jobs;
    }

    /// <summary>Marks a job started by scrcpy's own drop handler rather than by this queue.</summary>
    public bool Start(Guid id, bool installing)
    {
        var job = Find(id);
        if (job is null || job.State != TransferState.Waiting)
        {
            return false;
        }

        Change(job, installing ? TransferState.Installing : TransferState.Sending);
        job.Samples.Add((_clock.Elapsed, 0));
        return true;
    }

    public bool Progress(Guid id, long sent)
    {
        var job = Find(id);
        if (job is null || job.State != TransferState.Sending)
        {
            return false;
        }

        job.Sent = Math.Clamp(sent, 0, Math.Max(0, job.Item.Entry.Size));
        job.Samples.Add((_clock.Elapsed, job.Sent));
        var oldest = _clock.Elapsed - TransferProgress.Window - TimeSpan.FromSeconds(1);
        job.Samples.RemoveAll(sample => sample.Elapsed < oldest);
        job.ChangedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public bool Finish(Guid id, bool ok, string? why = null)
    {
        var job = Find(id);
        if (job is null || job.State is not (TransferState.Sending or TransferState.Installing))
        {
            return false;
        }

        job.Sent = ok ? job.Item.Entry.Size : job.Sent;
        job.Why = why;
        Change(job, ok ? (job.Item.Kind == TransferKind.Install ? TransferState.Installed : TransferState.Done) : TransferState.Failed);
        Trim();
        return true;
    }

    public bool Skip(Guid id, string? why = null) => End(id, TransferState.Skipped, why);
    public bool Cancel(Guid id) => End(id, TransferState.Cancelled, null);

    public bool Retry(Guid id)
    {
        var job = Find(id);
        if (job is null || job.State is not (TransferState.Failed or TransferState.Cancelled or TransferState.Skipped))
        {
            return false;
        }

        job.Sent = 0;
        job.Samples.Clear();
        job.Why = null;
        Change(job, TransferState.Waiting);
        return true;
    }

    public void ClearFinished()
    {
        _jobs.RemoveAll(job => TransferProgress.IsFinished(job.State));
    }

    private bool End(Guid id, TransferState state, string? why)
    {
        var job = Find(id);
        if (job is null || TransferProgress.IsFinished(job.State))
        {
            return false;
        }

        job.Why = why;
        Change(job, state);
        Trim();
        return true;
    }

    private TransferJob? Find(Guid id) => _jobs.FirstOrDefault(job => job.Id == id);

    private static void Change(TransferJob job, TransferState state)
    {
        job.State = state;
        job.ChangedAt = DateTimeOffset.UtcNow;
    }

    private void Trim()
    {
        var finished = _jobs.Where(job => TransferProgress.IsFinished(job.State)).OrderByDescending(job => job.ChangedAt).ToArray();
        foreach (var job in finished.Skip(_history))
        {
            _jobs.Remove(job);
        }
    }
}
