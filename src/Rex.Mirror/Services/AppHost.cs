using System.IO;
using System.Windows.Threading;
using Rex.Core;
using Rex.Mirror.Session;

namespace Rex.Mirror.Services;

/// <summary>
/// Composition root: owns the long-lived services (config, state, log, adb, session,
/// pipe server, tray) and hands them to the window. Everything here is created once.
/// </summary>
public sealed class AppHost : IDisposable
{
    private PipeServer? _pipe;
    private TrayIcon? _tray;

    private AppHost(AppPaths paths, RexConfig config, StateStore state, RexLog log)
    {
        Paths = paths;
        Config = config;
        State = state;
        Log = log;
        Runner = new ProcessRunner();
        Session = new SessionController(this);
        Usb = new UsbDoctor(this, UsbSystem.FromEnvironment(Runner));
    }

    public AppPaths Paths { get; }
    public RexConfig Config { get; private set; }
    public StateStore State { get; }
    public RexLog Log { get; }
    public IProcessRunner Runner { get; }
    public SessionController Session { get; }

    /// <summary>Notices a phone Windows could not read over USB and gets it repaired.</summary>
    public UsbDoctor Usb { get; }
    public MainWindow? Window { get; private set; }
    public TrayIcon? Tray => _tray;

    public event Action? ConfigChanged;

    /// <summary>How many times config.json was reloaded after a change made outside the app.</summary>
    public int ConfigReloads { get; private set; }

    /// <summary>Raised for live previews (slider drags); the write to disk follows shortly after.</summary>
    public event Action? ConfigPreviewed;

    private readonly List<Action<RexConfig>> _pendingPreviews = [];
    private DispatcherTimer? _previewFlush;
    private FileSystemWatcher? _configWatcher;
    private DispatcherTimer? _configReload;
    private string _lastConfigText = string.Empty;
    private DateTime _configWrittenUtc;
    private DateTime _configCheckedUtc;

    public static AppHost Create(LaunchOptions options)
    {
        var paths = options.Root.HasValue() ? AppPaths.FromRoot(options.Root!) : AppPaths.Discover();
        var config = ConfigFile.Load(paths.Config);
        var log = new RexLog(paths.LogFile, config.Logging);
        var state = new StateStore(paths.State);
        log.Info($"Android Headless Mirror starting (root={paths.Root})");
        return new AppHost(paths, config, state, log);
    }

    public void Start(MainWindow window)
    {
        Window = window;
        _tray = new TrayIcon(this);
        _pipe = new PipeServer(this);
        _pipe.Start();
        Session.Start();
        Usb.Start();
        WatchConfigFile();
    }

    /// <summary>
    /// rex config set (and hand edits) change config.json while the app runs; pick them up so the
    /// window, the restart notice and the overlay reflect the file. Our own writes are recognised
    /// by content and ignored.
    /// </summary>
    private void WatchConfigFile()
    {
        _lastConfigText = ReadConfigText();
        _configWrittenUtc = File.GetLastWriteTimeUtc(Paths.Config);
        _configReload = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _configReload.Tick += (_, _) =>
        {
            _configReload.Stop();
            var text = ReadConfigText();
            if (text.Length == 0)
            {
                // Caught while Windows replaced the file: the next look at its write time tries again.
                _configWrittenUtc = default;
                return;
            }

            if (text == _lastConfigText)
            {
                return;
            }

            _lastConfigText = text;
            ReloadConfigFromDisk();
        };

        var dispatcher = Dispatcher.CurrentDispatcher;
        _configWatcher = new FileSystemWatcher(Paths.Root, "config.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        FileSystemEventHandler touched = (_, _) => dispatcher.BeginInvoke(() => { _configReload.Stop(); _configReload.Start(); });
        _configWatcher.Changed += touched;
        _configWatcher.Created += touched;
        _configWatcher.Renamed += (sender, e) => touched(sender, e);
        _configWatcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// A file watcher can miss a change (a busy disk, a network folder, its buffer full), and then
    /// nothing reloads. The window's tick calls this; every two seconds it looks at when config.json
    /// was last written and, if that moved, reloads as a watched change would.
    /// </summary>
    public void CheckConfigFile()
    {
        var now = DateTime.UtcNow;
        if (_configReload is null || now - _configCheckedUtc < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _configCheckedUtc = now;
        var written = File.GetLastWriteTimeUtc(Paths.Config);
        if (written != _configWrittenUtc)
        {
            _configWrittenUtc = written;
            Log.Info("config.json's write time moved; reading it again.");
            if (!_configReload.IsEnabled)
            {
                _configReload.Start();
            }
        }
    }

    private string ReadConfigText()
    {
        try
        {
            return File.ReadAllText(Paths.Config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not read config.json while watching it; Windows may still be replacing the file.");
            return string.Empty;
        }
    }

    /// <summary>
    /// Applies one mutation to the latest on-disk configuration inside a cross-process
    /// transaction so GUI and CLI edits cannot overwrite unrelated newer settings.
    /// </summary>
    public void UpdateConfig(Action<RexConfig> mutate)
    {
        RexConfig latest;
        try
        {
            using var transaction = CrossProcessFileLock.Acquire(Paths.Config);
            latest = ConfigFile.Load(Paths.Config);
            mutate(latest);
            latest.Normalize();
            ConfigFile.Save(Paths.Config, latest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another program holding config.json, or a folder made read-only, must not take the app down.
            Log.Error("Could not save config.json", ex);
            Window?.SetStatus("Could not save the settings: " + ex.Message + " Close anything that has config.json open and try again.", isError: true);
            return;
        }

        Config = latest;
        _lastConfigText = ReadConfigText();
        ConfigChanged?.Invoke();
    }

    /// <summary>
    /// Applies a mutation to the in-memory configuration immediately (for live previews while a
    /// slider moves) and writes the accumulated mutations to disk 400 ms after the last one.
    /// </summary>
    public void PreviewConfig(Action<RexConfig> mutate)
    {
        var copy = Config.Copy();
        mutate(copy);
        copy.Normalize();
        Config = copy;
        _pendingPreviews.Add(mutate);
        ConfigPreviewed?.Invoke();

        _previewFlush ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _previewFlush.Stop();
        _previewFlush.Tick -= FlushPreviews;
        _previewFlush.Tick += FlushPreviews;
        _previewFlush.Start();
    }

    private void FlushPreviews(object? sender, EventArgs e)
    {
        _previewFlush!.Stop();
        var pending = _pendingPreviews.ToArray();
        _pendingPreviews.Clear();
        UpdateConfig(config =>
        {
            foreach (var mutate in pending)
            {
                mutate(config);
            }
        });
    }

    /// <summary>Reloads a configuration that was changed transactionally outside AppHost.</summary>
    public void ReloadConfigFromDisk()
    {
        try
        {
            using var transaction = CrossProcessFileLock.Acquire(Paths.Config);
            Config = ConfigFile.Load(Paths.Config);
        }
        catch (IOException ex)
        {
            Log.Error("Could not reload config.json", ex);
            return;
        }

        ConfigReloads++;
        Log.Info("config.json was changed outside the app; reloaded it.");
        ConfigChanged?.Invoke();
    }

    public string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "RexMirror.exe");

    public void Dispose()
    {
        _configWatcher?.Dispose();
        _configReload?.Stop();
        Usb.Dispose();
        Session.Dispose();
        _pipe?.Dispose();
        _tray?.Dispose();
        Log.Info("Android Headless Mirror exited.");
    }
}
