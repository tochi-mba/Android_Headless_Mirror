using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Rex.Mirror.Mirror;

/// <summary>Compact fullscreen controls hosted inside the existing mirror overlay.</summary>
public sealed class FullscreenHud : Border
{
    private readonly StackPanel _rotation = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _message = new() { FontSize = 11, TextAlignment = TextAlignment.Center, MaxWidth = 380 };
    private readonly Button _fit;
    private DateTime _visibleUntil;
    private bool _shown;

    public event Action<string>? ActionRequested;
    public bool Expanded => _rotation.Visibility == Visibility.Visible;
    public bool IsShown => _shown;

    public FullscreenHud()
    {
        Background = new SolidColorBrush(Color.FromArgb(220, 17, 21, 18));
        BorderBrush = new SolidColorBrush(Color.FromArgb(100, 133, 141, 131));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(6);
        Visibility = Visibility.Collapsed;
        var body = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(Icon("back", "IconBack", "Back"));
        row.Children.Add(Icon("home", "IconHome", "Home"));
        row.Children.Add(Icon("recents", "IconRecents", "Recent apps"));
        row.Children.Add(Icon("rotation-menu", "IconRotate", "Rotation · Ctrl+Alt+L landscape, Ctrl+Alt+U portrait"));
        _fit = TextButton("zoom-reset", "Fit", "Show the whole phone · reset zoom");
        row.Children.Add(_fit);
        row.Children.Add(Icon("fullscreen", "IconFullscreen", "Leave fullscreen · F11 or Esc"));
        body.Children.Add(row);

        var modes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        modes.Children.Add(TextButton("rotation-portrait", "Portrait", "Lock Android to portrait · Ctrl+Alt+U"));
        modes.Children.Add(TextButton("rotation-landscape", "Landscape", "Lock Android to landscape · Ctrl+Alt+L"));
        modes.Children.Add(TextButton("rotation-auto", "Auto", "Restore Android sensor rotation · Ctrl+Alt+A"));
        _rotation.Children.Add(modes);
        var turns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        turns.Children.Add(TextButton("rotate-left", "View ↶", "Turn only the PC view left · Ctrl+Alt+Left"));
        turns.Children.Add(TextButton("rotate-right", "View ↷", "Turn only the PC view right · Ctrl+Alt+Right"));
        _rotation.Children.Add(turns);
        body.Children.Add(_rotation);
        _message.Margin = new Thickness(6, 3, 6, 2);
        _message.Foreground = (Brush)FindResource("Muted");
        body.Children.Add(_message);
        Child = body;
    }

    private Button Icon(string action, string icon, string label)
    {
        var button = new Button
        {
            Content = new Path { Data = (Geometry)FindResource(icon), Stroke = (Brush)FindResource("Text"),
                StrokeThickness = 1.7, Width = 18, Height = 18, Stretch = Stretch.Uniform },
            Width = 40, Height = 36, Padding = new Thickness(8),
        };
        Wire(button, action, label);
        return button;
    }

    private Button TextButton(string action, string text, string label)
    {
        var button = new Button { Content = text, MinWidth = 48, MinHeight = 36, Padding = new Thickness(10, 5, 10, 5) };
        Wire(button, action, label);
        return button;
    }

    private void Wire(Button button, string action, string label)
    {
        button.Style = (Style)FindResource("GhostButton");
        button.Focusable = false;
        button.ToolTip = label;
        button.Margin = new Thickness(2);
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, "hud-" + action);
        button.Click += (_, e) =>
        {
            e.Handled = true;
            Reveal();
            if (action == "rotation-menu")
            {
                _rotation.Visibility = Expanded ? Visibility.Collapsed : Visibility.Visible;
            }
            else
            {
                if (action.StartsWith("rotation-", StringComparison.Ordinal))
                    _rotation.Visibility = Visibility.Collapsed;
                ActionRequested?.Invoke(action);
            }
        };
    }

    public void Reveal(string? message = null)
    {
        _visibleUntil = DateTime.UtcNow.AddSeconds(3);
        if (message is not null) _message.Text = message;
        ShowHud(true);
    }

    public void Update(bool enabled, bool pointerAtTop, double zoom)
    {
        if (!enabled)
        {
            _rotation.Visibility = Visibility.Collapsed;
            ShowHud(false);
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;
        _fit.Content = zoom > 1.001 ? $"{zoom * 100:0}%" : "Fit";
        if (pointerAtTop || IsMouseOver) _visibleUntil = DateTime.UtcNow.AddSeconds(3);
        var visible = DateTime.UtcNow < _visibleUntil;
        if (!visible) _rotation.Visibility = Visibility.Collapsed;
        ShowHud(visible);
    }

    private void ShowHud(bool show)
    {
        if (_shown == show) return;
        _shown = show;
        IsHitTestVisible = show;
        BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(160)));
    }
}
