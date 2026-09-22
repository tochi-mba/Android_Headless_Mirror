using System.IO;
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
    }

    public AppPaths Paths { get; }
    public RexConfig Config { get; private set; }
    public StateStore State { get; }
    public RexLog Log { get; }
    public IProcessRunner Runner { get; }
    public SessionController Session { get; }
    public MainWindow? Window { get; private set; }
    public TrayIcon? Tray => _tray;

    public event Action? ConfigChanged;

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
        catch (IOException ex)
        {
            Log.Error("Could not save config.json", ex);
            return;
        }

        Config = latest;
        ConfigChanged?.Invoke();
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

        ConfigChanged?.Invoke();
    }

    public string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "RexMirror.exe");

    public void Dispose()
    {
        Session.Dispose();
        _pipe?.Dispose();
        _tray?.Dispose();
        Log.Info("Android Headless Mirror exited.");
    }
}
