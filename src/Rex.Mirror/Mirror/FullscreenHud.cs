using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Rex.Core;

namespace Rex.Mirror.Mirror;

/// <summary>
/// The compact fullscreen controls. Which buttons appear, where the bar sits and how long it
/// lingers all come from <see cref="HudSettings"/>, so the bar is rebuilt whenever that changes.
/// </summary>
public sealed class FullscreenHud : Border
{
    private readonly StackPanel _buttons;
    private readonly TextBlock _message = new()
    {
        FontSize = 11,
        TextAlignment = TextAlignment.Center,
        MaxWidth = 260,
        TextWrapping = TextWrapping.Wrap,
    };

    private readonly Dictionary<string, Button> _zoomLabels = new(StringComparer.Ordinal);
    private readonly Border _grip;
    private DateTime _visibleUntil;
    private bool _shown;
    private string _builtFor = string.Empty;

    public event Action<string>? ActionRequested;

    public bool IsShown => _shown;
    public double HideSeconds { get; set; } = 3;

    public FullscreenHud()
    {
        Background = new SolidColorBrush(Color.FromArgb(220, 17, 21, 18));
        BorderBrush = new SolidColorBrush(Color.FromArgb(100, 133, 141, 131));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(6);
        Visibility = Visibility.Collapsed;
        _buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _grip = CreateGrip();
        _message.Margin = new Thickness(6, 3, 6, 2);
        _message.Foreground = (Brush)FindResource("Muted");
        var body = new StackPanel();
        body.Children.Add(_buttons);
        body.Children.Add(_message);
        Child = body;
    }

    /// <summary>Rebuilds the buttons when the chosen set, the layout or the scale changes.</summary>
    public void Apply(HudSettings settings)
    {
        HideSeconds = settings.HideSeconds;
        var signature = string.Join(",", settings.Buttons) + "|" + settings.Position + "|" + settings.Scale + "|" + settings.Opacity;
        if (signature == _builtFor)
        {
            return;
        }

        _builtFor = signature;
        _zoomLabels.Clear();
        _buttons.Children.Clear();
        var vertical = HudLayout.IsVertical(settings.Position);
        _buttons.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        _grip.LayoutTransform = vertical ? new RotateTransform(90) : Transform.Identity;
        _buttons.Children.Add(_grip);
        _message.Visibility = settings.ShowMessages ? Visibility.Visible : Visibility.Collapsed;
        LayoutTransform = settings.Scale is > 0.99 and < 1.01 ? Transform.Identity : new ScaleTransform(settings.Scale, settings.Scale);

        foreach (var id in settings.Buttons)
        {
            if (MirrorActions.Find(id) is { } action)
            {
                _buttons.Children.Add(Create(action));
            }
        }

        if (_buttons.Children.Count == 0)
        {
            _message.Visibility = Visibility.Visible;
            _message.Text = "No buttons chosen. Settings → Fullscreen HUD.";
        }
    }

    /// <summary>
    /// The handle that moves the bar. A grip of its own means dragging can never be confused with
    /// pressing a control, and it shows the bar can be moved at all, which a bare drag target does
    /// not. It carries an automation id so the move can be driven from a test.
    /// </summary>
    private Border CreateGrip()
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var column = 0; column < 2; column++)
        {
            var stack = new StackPanel { Margin = new Thickness(1, 0, 1, 0) };
            for (var row = 0; row < 3; row++)
            {
                stack.Children.Add(new Ellipse
                {
                    Width = 2.5,
                    Height = 2.5,
                    Margin = new Thickness(0, 1.5, 0, 1.5),
                    Fill = (Brush)FindResource("Muted"),
                });
            }

            dots.Children.Add(stack);
        }

        var grip = new Border
        {
            Child = dots,
            Padding = new Thickness(5, 8, 5, 8),
            Margin = new Thickness(0, 2, 4, 2),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.SizeAll,
            ToolTip = "Drag these controls anywhere · double-click to put them back",
        };

        AutomationProperties.SetName(grip, "Move the controls");
        AutomationProperties.SetAutomationId(grip, "hud-grip");

        return grip;
    }

    /// <summary>
    /// True when this point belongs to one of the bar's controls. Everything else about the bar -
    /// the grip, the space between the buttons, the status line - is somewhere it can be dragged
    /// by. The controls are found by where they were laid out rather than by hit testing, which
    /// this window cannot rely on: it never takes activation.
    /// </summary>
    public bool HitsButton(Point point)
    {
        foreach (var button in _buttons.Children.OfType<System.Windows.Controls.Primitives.ButtonBase>())
        {
            try
            {
                var origin = button.TransformToAncestor(this).Transform(new Point());
                if (new Rect(origin, button.RenderSize).Contains(point))
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                // The bar is between layouts; treat it as somewhere the bar can be dragged by.
            }
        }

        return false;
    }

    private Button Create(MirrorAction action)
    {
        var icon = HudIcons.For(action.Id);
        var button = new Button
        {
            Style = (Style)FindResource("GhostButton"),
            Focusable = false,
            Margin = new Thickness(2),
            ToolTip = action.Label + " · " + action.Detail,
            MinHeight = 36,
        };

        if (icon is not null && TryFindResource(icon) is Geometry geometry)
        {
            button.Content = new Path
            {
                Data = geometry,
                Stroke = (Brush)FindResource("Text"),
                StrokeThickness = 1.7,
                Width = 18,
                Height = 18,
                Stretch = Stretch.Uniform,
            };
            button.Width = 40;
            button.Padding = new Thickness(8);
        }
        else
        {
            button.Content = action.Label;
            button.MinWidth = 48;
            button.Padding = new Thickness(10, 5, 10, 5);
        }

        if (action.Id == "zoom-reset")
        {
            _zoomLabels[action.Id] = button;
        }

        AutomationProperties.SetName(button, action.Label);
        AutomationProperties.SetAutomationId(button, "hud-" + action.Id);
        button.Click += (_, e) =>
        {
            e.Handled = true;
            Reveal();
            ActionRequested?.Invoke(action.Id);
        };
        return button;
    }

    public void Reveal(string? message = null)
    {
        _visibleUntil = DateTime.UtcNow.AddSeconds(HideSeconds);
        if (message is not null)
        {
            _message.Text = message;
        }

        ShowHud(true);
    }

    /// <summary>Keeps the bar up while the pointer is in its zone, on it, or using it.</summary>
    public void Update(bool enabled, bool pointerInZone, double zoom)
    {
        if (!enabled)
        {
            ShowHud(false);
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        if (_zoomLabels.TryGetValue("zoom-reset", out var fit) && fit.Content is string)
        {
            fit.Content = zoom > 1.001 ? $"{zoom * 100:0}%" : "Fit";
        }

        if (pointerInZone || IsMouseOver || IsKeyboardFocusWithin)
        {
            _visibleUntil = DateTime.UtcNow.AddSeconds(HideSeconds);
        }

        ShowHud(DateTime.UtcNow < _visibleUntil);
    }

    private void ShowHud(bool show)
    {
        if (_shown == show)
        {
            return;
        }

        _shown = show;
        IsHitTestVisible = show;
        BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(160)));
    }
}

/// <summary>The icon each action shows in the HUD; actions without one use their label.</summary>
public static class HudIcons
{
    private static readonly Dictionary<string, string> Icons = new(StringComparer.Ordinal)
    {
        ["home"] = "IconHome",
        ["back"] = "IconBack",
        ["recents"] = "IconRecents",
        ["power"] = "IconPower",
        ["wake"] = "IconSun",
        ["sleep"] = "IconMoon",
        ["volume-up"] = "IconVolumeUp",
        ["volume-down"] = "IconVolumeDown",
        ["notifications"] = "IconBell",
        ["quick-settings"] = "IconSliders",
        ["rotate-device"] = "IconRotate",
        ["rotate-left"] = "IconRotate",
        ["rotate-right"] = "IconRotate",
        ["pause"] = "IconPause",
        ["screenshot"] = "IconCamera",
        ["fullscreen"] = "IconFullscreen",
        ["zoom-out"] = "IconZoomOut",
    };

    public static string? For(string actionId) => Icons.GetValueOrDefault(actionId);
}
