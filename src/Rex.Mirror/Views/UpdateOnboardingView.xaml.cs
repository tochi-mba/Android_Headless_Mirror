using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rex.Core;

namespace Rex.Mirror.Views;

/// <summary>
/// The small onboarding shown only after an update which introduced a workflow worth teaching.
/// Its cards are selected from the version the person last ran, so it never repeats first-run
/// setup or describes features they already had.
/// </summary>
public partial class UpdateOnboardingView : UserControl
{
    private MainWindow? _window;
    private IReadOnlyList<UpdateFeature> _features = [];

    public UpdateOnboardingView() => InitializeComponent();

    public void Attach(MainWindow window) => _window = window;

    public void Show(string previousVersion, string currentVersion, IReadOnlyList<UpdateFeature> features)
    {
        UpdateTitle.Text = features.Count == 1 ? $"New in {currentVersion}" : $"What changed since {previousVersion}";
        UpdateIntro.Text = features.Count == 1
            ? "One thing you did not have before. Your settings are as you left them."
            : $"{features.Count} things you did not have before, and only those. Your settings are as you left them.";
        _features = features;
        UpdateFeatures.ItemsSource = features;
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => UpdateTour.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void Close() => Visibility = Visibility.Collapsed;

    private void OnFullNotes(object sender, RoutedEventArgs e) => _window?.FinishUpdateOnboarding(openFullNotes: true);

    private void OnDone(object sender, RoutedEventArgs e) => _window?.FinishUpdateOnboarding(openFullNotes: false);

    private void OnTour(object sender, RoutedEventArgs e) => _window?.TourTheUpdate(_features);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _window?.FinishUpdateOnboarding(openFullNotes: false);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
