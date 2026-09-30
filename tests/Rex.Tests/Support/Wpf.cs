using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Rex.Tests.Support;

/// <summary>
/// Builds and inspects the app's own WPF controls in unit tests: one STA thread with the app's
/// theme loaded as the application's resources, and no window, so nothing reaches the desktop.
/// Everything runs on that one thread, one test at a time, the way WPF wants it.
/// </summary>
public static class Wpf
{
    private static readonly Lazy<Dispatcher> Ui = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void Run(Action work) => Run(() =>
    {
        work();
        return true;
    });

    public static T Run<T>(Func<T> work)
    {
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        Ui.Value.Invoke(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        failure?.Throw();
        return result;
    }

    /// <summary>Measures and arranges an element that is in no window, so its template exists and it has a size.</summary>
    public static T Layout<T>(T element, double width = 320, double height = double.PositiveInfinity) where T : FrameworkElement
    {
        element.Measure(new Size(width, height));
        var size = element.DesiredSize;
        element.Arrange(new Rect(0, 0, width, double.IsInfinity(height) ? size.Height : height));
        element.UpdateLayout();
        return element;
    }

    /// <summary>Every element under this one in the visual tree, templates included.</summary>
    public static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Visuals(child))
            {
                yield return deeper;
            }
        }
    }

    /// <summary>Every element under this one in the logical tree, which holds the content of a closed expander too.</summary>
    public static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var deeper in Logical(child))
            {
                yield return deeper;
            }
        }
    }

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        ExceptionDispatchInfo? failure = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                    new Uri("/RexMirror;component/Theme.xaml", UriKind.Relative)));
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                ready.Set();
            }

            if (failure is null)
            {
                Dispatcher.Run();
            }
        })
        {
            IsBackground = true,
            Name = "WPF unit tests",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        failure?.Throw();
        return dispatcher!;
    }
}
