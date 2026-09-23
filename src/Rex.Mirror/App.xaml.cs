using System.IO;
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

    public static LaunchOptions Options { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Options = LaunchOptions.Parse(e.Args);

        _instanceMutex = new Mutex(initiallyOwned: true, "Local\\RexMirror-" + Ipc.PipeName(), out var createdNew);
        if (!createdNew)
        {
            // Another copy is already running: bring it forward instead of racing for the phone.
            _ = new IpcClient().SendAsync(new IpcRequest("show")).GetAwaiter().GetResult();
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        ApplyContrast();
        SystemParameters.StaticPropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SystemParameters.HighContrast))
            {
                ApplyContrast();
            }
        };

        try
        {
            _host = AppHost.Create(Options);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Android Headless Mirror", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        _window = new MainWindow(_host);
        _host.Start(_window);

        if (!Options.StartInBackground)
        {
            _window.ShowFromTray();
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
        _host?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Log.Error("Unhandled UI exception: " + e.Exception);
        MessageBox.Show(
            "Something went wrong: " + e.Exception.Message + "\n\nDetails were written to logs\\mirror.log.",
            "Android Headless Mirror",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
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
