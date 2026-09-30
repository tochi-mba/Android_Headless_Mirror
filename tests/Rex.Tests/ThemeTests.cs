using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Rex.Mirror.Views;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The shared styles in Theme.xaml, built for real on a WPF thread and measured: where content
/// sits, how big a target is, what focus does to the layout.
/// </summary>
public sealed class ThemeTests
{
    [Fact]
    public void ButtonsCentreTheirContentUnlessAskedNotTo()
    {
        Wpf.Run(() =>
        {
            var plain = Wpf.Layout(new Button { Content = "Restart" }, 200);
            Assert.Equal(HorizontalAlignment.Center, Presenter(plain).HorizontalAlignment);

            var left = Wpf.Layout(new Button { Content = "Restart", HorizontalContentAlignment = HorizontalAlignment.Left }, 200);
            Assert.Equal(HorizontalAlignment.Left, Presenter(left).HorizontalAlignment);
        });
    }

    [Fact]
    public void ComboBoxesStartTheirTextAtTheLeftWithTheChevronAtTheRight()
    {
        Wpf.Run(() =>
        {
            var combo = new ComboBox();
            combo.Items.Add(new ComboBoxItem { Content = "Native" });
            combo.SelectedIndex = 0;
            Wpf.Layout(combo, 240);

            var text = Wpf.Visuals(combo).OfType<TextBlock>().Single(t => t.Text == "Native");
            var chevron = Wpf.Visuals(combo).OfType<Path>().Single();
            var textLeft = text.TransformToAncestor(combo).Transform(new Point()).X;
            var chevronRight = chevron.TransformToAncestor(combo).Transform(new Point(chevron.ActualWidth, 0)).X;

            // Padding is 10 on each side; the text starts there and the chevron ends there.
            Assert.InRange(textLeft, 9, 12);
            Assert.InRange(chevronRight, 240 - 12, 240 - 8);
        });
    }

    [Fact]
    public void EmptyTextBoxesAreOutlinedClearlyEnoughToSee()
    {
        var theme = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Rex.Mirror", "Theme.xaml"));
        var field = Brush(theme, "Field");
        Assert.True(Contrast(field, Colour(theme, "PanelColor")) >= 3, "A text box's outline must be 3:1 against the panel.");
        Assert.True(Contrast(field, Colour(theme, "InkColor")) >= 3, "A text box's outline must be 3:1 against its own fill.");

        Wpf.Run(() =>
        {
            var box = Wpf.Layout(new TextBox(), 200);
            Assert.Equal(field, ((SolidColorBrush)box.BorderBrush).Color);
            Assert.Contains(box.Template.Triggers.OfType<Trigger>(), t => t.Property == UIElement.IsMouseOverProperty);
        });
    }

    [Fact]
    public void SwitchesAreBigEnoughToHitAndFocusDoesNotMoveThem()
    {
        Wpf.Run(() =>
        {
            var toggle = Wpf.Layout(new CheckBox(), 200);
            Assert.True(toggle.DesiredSize.Width >= 44, $"A switch is {toggle.DesiredSize.Width} DIP wide.");
            Assert.True(toggle.DesiredSize.Height >= 32, $"A switch is {toggle.DesiredSize.Height} DIP tall.");

            var track = (Border)toggle.Template.FindName("Track", toggle);
            Assert.Equal(new Thickness(1), track.BorderThickness);
            AssertFocusOnlyRecolours(toggle.Template.Triggers);
            Assert.Contains(toggle.Template.Triggers.OfType<Trigger>(), t => t.Property == UIElement.IsMouseOverProperty);
            Assert.Contains(toggle.Template.Triggers.OfType<Trigger>(), t => t.Property == SettingRows.HotProperty);
        });
    }

    [Fact]
    public void ExpanderHeadersAndTabsDrawTheirOwnFocusRingWithoutShiftingAnything()
    {
        Wpf.Run(() =>
        {
            var expander = Wpf.Layout(new Expander { Header = "Display", Content = new TextBlock { Text = "Row" } }, 300);
            var header = Wpf.Visuals(expander).OfType<ToggleButton>().Single();
            Assert.Null(header.FocusVisualStyle);
            var ring = (Border)header.Template.FindName("Ring", header);
            Assert.Equal(new Thickness(1), ring.BorderThickness);
            Assert.Equal(Colors.Transparent, ((SolidColorBrush)ring.BorderBrush).Color);
            AssertFocusOnlyRecolours(header.Template.Triggers);

            // The words line up with the rows below them; only the ring reaches past.
            var words = Wpf.Visuals(header).OfType<TextBlock>().Single(t => t.Text == "Display");
            Assert.Equal(0, words.TransformToAncestor(expander).Transform(new Point()).X, 1);

            var tab = Wpf.Layout(new RadioButton { Style = (Style)Application.Current.FindResource("SidebarTab"), Content = "Phone" }, 80);
            var tabBorder = (Border)tab.Template.FindName("Border", tab);
            Assert.Equal(new Thickness(1), tabBorder.BorderThickness);
            AssertFocusOnlyRecolours(tab.Template.Triggers);
        });
    }

    [Fact]
    public void AScrollThumbIsNeverShorterThan32Dip()
    {
        Wpf.Run(() =>
        {
            var bar = new ScrollBar { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100_000, ViewportSize = 10 };
            Wpf.Layout(bar, 8, 300);
            Assert.True(bar.Track.Thumb.ActualHeight >= 32, $"The thumb is {bar.Track.Thumb.ActualHeight} DIP tall.");
        });
    }

    [Fact]
    public void ClickingARowsLabelFlipsItsSwitch()
    {
        Wpf.Run(() =>
        {
            var label = new TextBlock { Text = "Phone audio on this PC" };
            var toggle = new CheckBox();
            var row = Wpf.Layout(new HeaderedContentControl { Style = (Style)Application.Current.FindResource("SettingRow"), Header = label, Content = toggle });

            Enter(row);
            Assert.True(SettingRows.GetHot(toggle), "The switch lights up while the pointer is on its row.");
            Assert.Equal(Cursors.Hand, row.Cursor);

            Click(label);
            Assert.True(toggle.IsChecked);
            Click(label);
            Assert.False(toggle.IsChecked);

            // A release that did not start on the row is not a click on it.
            label.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
            Assert.False(toggle.IsChecked);

            toggle.IsEnabled = false;
            Click(label);
            Assert.False(toggle.IsChecked);

            row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            Assert.False(SettingRows.GetHot(toggle));

            // A row whose control is not a switch has nothing to flip.
            var combo = new HeaderedContentControl { Style = (Style)Application.Current.FindResource("SettingRow"), Header = "Frame rate", Content = new ComboBox() };
            Assert.False(SettingRows.Toggle(Wpf.Layout(combo)));
        });
    }

    private static void Enter(UIElement element) =>
        element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });

    private static void Click(UIElement element)
    {
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
    }

    private static ContentPresenter Presenter(Control control) => Wpf.Visuals(control).OfType<ContentPresenter>().First();

    /// <summary>A focus ring that changes a border's thickness moves everything inside it by a pixel.</summary>
    private static void AssertFocusOnlyRecolours(TriggerCollection triggers)
    {
        var focus = triggers.OfType<Trigger>().Where(t => t.Property == UIElement.IsKeyboardFocusedProperty).ToArray();
        Assert.NotEmpty(focus);
        Assert.All(focus.SelectMany(t => t.Setters.OfType<Setter>()), setter =>
            Assert.NotEqual(Border.BorderThicknessProperty, setter.Property));
    }

    private static Color Colour(string theme, string key) =>
        (Color)ColorConverter.ConvertFromString(Regex.Match(theme, $"<Color x:Key=\"{key}\">(#[0-9A-Fa-f]{{6}})</Color>").Groups[1].Value);

    private static Color Brush(string theme, string key) =>
        (Color)ColorConverter.ConvertFromString(Regex.Match(theme, $"<SolidColorBrush x:Key=\"{key}\" Color=\"(#[0-9A-Fa-f]{{6}})\"").Groups[1].Value);

    /// <summary>WCAG contrast ratio between two opaque colours.</summary>
    internal static double Contrast(Color a, Color b)
    {
        var first = Luminance(a);
        var second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);

        static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }

    [Fact]
    public void TheContrastHelperAgreesWithKnownValues()
    {
        Assert.Equal(21, Contrast(Colors.White, Colors.Black), 1);
        Assert.Equal(1, Contrast(Colors.Gray, Colors.Gray), 3);
        Assert.Equal(4.5, Contrast((Color)ColorConverter.ConvertFromString("#767676"), Colors.White), 1);
    }
}
