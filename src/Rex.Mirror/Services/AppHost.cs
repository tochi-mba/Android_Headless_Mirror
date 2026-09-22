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

    /// <summary>Applies an edited copy of the configuration, persists it and notifies listeners.</summary>
    public void SaveConfig(RexConfig updated)
    {
        updated.Normalize();
        Config = updated;
        try
        {
            ConfigFile.Save(Paths.Config, updated);
        }
        catch (IOException ex)
        {
            Log.Error("Could not save config.json", ex);
        }

        ConfigChanged?.Invoke();
    }

    public void UpdateConfig(Action<RexConfig> mutate)
    {
        var copy = Config.Copy();
        mutate(copy);
        SaveConfig(copy);
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
