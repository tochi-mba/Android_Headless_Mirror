using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror;

/// <summary>
/// Process entry: enforces a single instance per user (a second launch just asks the running
/// one to show itself), parses the few command-line switches, builds the app host and shows
/// the window.
/// </summary>
public partial class App : Application
{
    private Mutex? _instanceMutex;
    private AppHost? _host;
    private MainWindow? _window;
    private RexLog? _startupLog;
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];
    private string _startupPhase = "process entry";
    private bool _fatalShown;

    public static LaunchOptions Options { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Options = LaunchOptions.Parse(e.Args);
        _startupLog = CreateStartupLog();
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        LogProcessContext(e.Args);

        try
        {
            SetStartupPhase("single-instance check");
            _instanceMutex = new Mutex(initiallyOwned: true, "Local\\RexMirror-" + Ipc.PipeName(), out var createdNew);
            if (!createdNew)
            {
                // Another copy is already running: bring it forward instead of racing for the phone.
                _ = new IpcClient().SendAsync(new IpcRequest("show")).GetAwaiter().GetResult();
                ActiveLog?.Info($"Run {_runId}: another instance is already running; asked it to show.");
                Shutdown(0);
                return;
            }

            SetStartupPhase("appearance setup");
            ApplyContrast();
            SystemParameters.StaticPropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SystemParameters.HighContrast))
                {
                    ApplyContrast();
                }
            };

            SetStartupPhase("configuration and services");
            _host = AppHost.Create(Options);
            _host.Log.Info($"Run {_runId}: startup context continues from the bootstrap log.");

            SetStartupPhase("main-window construction");
            _window = new MainWindow(_host);
            SetStartupPhase("background services");
            _host.Start(_window);

            SetStartupPhase(Options.StartInBackground ? "hidden startup complete" : "showing the window");
            if (!Options.StartInBackground)
            {
                _window.ShowFromTray();
            }

            SetStartupPhase("running");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ReportFatal("Startup could not finish", ex, showTechnicalSummary: false);
            Shutdown(2);
        }
        catch (Exception ex)
        {
            ReportFatal("Startup failed unexpectedly", ex, showTechnicalSummary: true);
            Shutdown(3);
        }
    }

    /// <summary>
    /// Hands the palette over to Windows in High Contrast, and takes it back when that is turned
    /// off again. Everything else about the look stays where it is: only the colour keys change.
    /// </summary>
    private void ApplyContrast()
    {
        var wanted = SystemParameters.HighContrast;
        var loaded = Resources.MergedDictionaries.FirstOrDefault(d => d.Source?.OriginalString.EndsWith("HighContrast.xaml", StringComparison.Ordinal) == true);
        if (wanted && loaded is null)
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("HighContrast.xaml", UriKind.Relative) });
        }
        else if (!wanted && loaded is not null)
        {
            Resources.MergedDictionaries.Remove(loaded);
        }

        _host?.Log.Info("High contrast: " + (wanted ? "on" : "off"));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _host?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportFatal("Unhandled UI failure", e.Exception, showTechnicalSummary: true);
        e.Handled = true;
        Shutdown(3);
    }

    private RexLog? ActiveLog => _host?.Log ?? _startupLog;

    private RexLog CreateStartupLog()
    {
        string path;
        try
        {
            var paths = Options.Root.HasValue() ? AppPaths.FromRoot(Options.Root!) : AppPaths.Discover();
            path = paths.LogFile;
        }
        catch
        {
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "REX", AppPaths.ProductFolderName, "logs", "mirror.log");
        }

        return new RexLog(path, new LoggingSettings());
    }

    private void LogProcessContext(IReadOnlyList<string> args)
    {
        ActiveLog?.Info(
            $"Run {_runId}: process started; version={typeof(App).Assembly.GetName().Version}; pid={Environment.ProcessId}; " +
            $"background={Options.StartInBackground}; interactive={Environment.UserInteractive}; elevated={IsElevated()}; " +
            $"OS={RuntimeInformation.OSDescription}; framework={RuntimeInformation.FrameworkDescription}; " +
            $"process={RuntimeInformation.ProcessArchitecture}; base={AppContext.BaseDirectory}; executable={Environment.ProcessPath}; " +
            $"arguments={SafeArguments(args)}");
    }

    private void SetStartupPhase(string phase)
    {
        _startupPhase = phase;
        ActiveLog?.Info($"Run {_runId}: startup phase: {phase}.");
    }

    private void ReportFatal(string label, Exception exception, bool showTechnicalSummary)
    {
        if (_fatalShown)
        {
            ActiveLog?.Critical($"Run {_runId}: another fatal failure during {_startupPhase}", exception);
            return;
        }

        _fatalShown = true;
        var log = ActiveLog;
        log?.Critical($"Run {_runId}: {label} during startup phase '{_startupPhase}'", exception);
        var root = ExceptionDiagnostics.RootCause(exception);
        var firstLine = showTechnicalSummary ? ExceptionDiagnostics.Summary(root) : root.Message;
        var logPath = log?.Path ?? "the local application log";
        try
        {
            MessageBox.Show(
                $"Android Headless Mirror could not continue during {_startupPhase}.\n\n{firstLine}\n\nFull details were written to:\n{logPath}\n\nRun 'rex diagnostics' to collect the rest of the setup state.",
                "Android Headless Mirror",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // A fatal path must not obscure the original exception if Windows cannot draw a dialog.
        }
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            ActiveLog?.Critical($"Run {_runId}: unhandled AppDomain failure during '{_startupPhase}' (terminating={e.IsTerminating})", exception);
        }
        else
        {
            ActiveLog?.Error($"Run {_runId}: unhandled non-Exception object during '{_startupPhase}': {e.ExceptionObject}");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ActiveLog?.Error($"Run {_runId}: unobserved task failure during '{_startupPhase}'", e.Exception);
        e.SetObserved();
    }

    private static string SafeArguments(IEnumerable<string> args) =>
        string.Join(' ', args.Select(argument => argument.Contains(' ') ? '"' + argument + '"' : argument));

    private static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}

public sealed record LaunchOptions
{
    /// <summary>--background: start hidden in the tray (used by Start with Windows).</summary>
    public bool StartInBackground { get; init; }

    /// <summary>--root PATH: package folder override (otherwise discovered).</summary>
    public string? Root { get; init; }

    public static LaunchOptions Parse(string[] args)
    {
        var options = new LaunchOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--background":
                    options = options with { StartInBackground = true };
                    break;
                case "--root" when i + 1 < args.Length:
                    options = options with { Root = args[++i] };
                    break;
            }
        }

        return options;
    }
}

internal static class StringExtensions
{
    public static bool HasValue(this string? value) => !string.IsNullOrWhiteSpace(value);
}
