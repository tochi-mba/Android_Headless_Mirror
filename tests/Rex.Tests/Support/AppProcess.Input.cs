namespace Rex.Tests.Support;

public sealed partial class AppProcess
{
    private System.Windows.Forms.Form? _foregroundTestWindow;
    private Thread? _foregroundTestThread;
    private readonly System.Collections.Concurrent.ConcurrentQueue<int> _foregroundKeys = new();

    /// <summary>The keys the test's own window in front received, as virtual-key codes.</summary>
    public IReadOnlyCollection<int> KeysReachingTheWindowInFront => _foregroundKeys;

    /// <summary>Holds the keys down in order, presses the last one <paramref name="times"/> times as a held key repeats, then lets go.</summary>
    public async Task HoldChordAsync(int times, params byte[] keys)
    {
        foreach (var key in keys) keybd_event(key, 0, 0, UIntPtr.Zero);
        for (var repeat = 1; repeat < times; repeat++) keybd_event(keys[^1], 0, 0, UIntPtr.Zero);
        for (var index = keys.Length - 1; index >= 0; index--) keybd_event(keys[index], 0, 2, UIntPtr.Zero);
        await Task.Delay(250, TestContext.Current.CancellationToken);
    }

    /// <summary>Left Ctrl with right Alt, which is AltGr on many layouts, and a key.</summary>
    public async Task PressAltGrAsync(byte key)
    {
        const uint Extended = 1;
        keybd_event(0xA2, 0, 0, UIntPtr.Zero);
        keybd_event(0xA5, 0, Extended, UIntPtr.Zero);
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 2, UIntPtr.Zero);
        keybd_event(0xA5, 0, Extended | 2, UIntPtr.Zero);
        keybd_event(0xA2, 0, 2, UIntPtr.Zero);
        await Task.Delay(250, TestContext.Current.CancellationToken);
    }

    /// <summary>Sends a chord to whichever window is already in front, without changing focus first.</summary>
    public async Task PressChordAsync(params byte[] keys)
    {
        foreach (var key in keys) keybd_event(key, 0, 0, UIntPtr.Zero);
        for (var index = keys.Length - 1; index >= 0; index--) keybd_event(keys[index], 0, 2, UIntPtr.Zero);
        await Task.Delay(250, TestContext.Current.CancellationToken);
    }

    /// <summary>Shows a small test-owned window and makes it foreground for global-shortcut tests.</summary>
    public async Task PutAnotherWindowInFrontAsync()
    {
        CloseForegroundTestWindow();
        var ready = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);
        _foregroundTestThread = new Thread(() =>
        {
            using var form = new System.Windows.Forms.Form
            {
                Text = "REX test foreground window",
                Width = 320,
                Height = 180,
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                KeyPreview = true,
            };
            form.KeyDown += (_, e) => _foregroundKeys.Enqueue((int)e.KeyCode);
            _foregroundTestWindow = form;
            form.Shown += (_, _) =>
            {
                SetForegroundWindow(form.Handle);
                ready.TrySetResult(form.Handle);
            };
            System.Windows.Forms.Application.Run(form);
        })
        {
            IsBackground = true,
            Name = "REX test foreground window",
        };
        _foregroundTestThread.SetApartmentState(ApartmentState.STA);
        _foregroundTestThread.Start();

        var handle = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        for (var attempt = 0; attempt < 40 && GetAncestor(GetForegroundWindow(), 2) != handle; attempt++)
        {
            SetForegroundWindow(handle);
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Equal(handle, GetAncestor(GetForegroundWindow(), 2));
    }

    private void CloseForegroundTestWindow()
    {
        var form = Interlocked.Exchange(ref _foregroundTestWindow, null);
        if (form is not null && !form.IsDisposed)
        {
            try { form.BeginInvoke(form.Close); }
            catch (InvalidOperationException) { }
        }

        var thread = Interlocked.Exchange(ref _foregroundTestThread, null);
        if (thread?.IsAlive == true) thread.Join(TimeSpan.FromSeconds(2));
    }
}
