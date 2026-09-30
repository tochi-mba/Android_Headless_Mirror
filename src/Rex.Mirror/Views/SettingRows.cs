using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Rex.Mirror.Views;

/// <summary>
/// What a setting row does beyond laying out its label and its control.
///
/// A switch is 38 DIP wide and its label is most of the row, so a row that only answers on the
/// switch makes people aim at the smallest part of it. The style turns <see cref="ClickToToggleProperty"/>
/// on for every row: a click on the label flips the switch, and while the pointer is anywhere on
/// the row the switch lights up as it would under the pointer, so the row says it is one target.
/// </summary>
public static class SettingRows
{
    public static readonly DependencyProperty ClickToToggleProperty = DependencyProperty.RegisterAttached(
        "ClickToToggle", typeof(bool), typeof(SettingRows), new PropertyMetadata(false, OnClickToToggleChanged));

    /// <summary>Set on a row's switch while the pointer is on the row; the switch's template lights its track.</summary>
    public static readonly DependencyProperty HotProperty = DependencyProperty.RegisterAttached(
        "Hot", typeof(bool), typeof(SettingRows), new PropertyMetadata(false));

    private static readonly DependencyProperty PressedProperty = DependencyProperty.RegisterAttached(
        "Pressed", typeof(bool), typeof(SettingRows), new PropertyMetadata(false));

    public static bool GetClickToToggle(DependencyObject element) => (bool)element.GetValue(ClickToToggleProperty);

    public static void SetClickToToggle(DependencyObject element, bool value) => element.SetValue(ClickToToggleProperty, value);

    public static bool GetHot(DependencyObject element) => (bool)element.GetValue(HotProperty);

    public static void SetHot(DependencyObject element, bool value) => element.SetValue(HotProperty, value);

    /// <summary>The switch a row holds, or null for a row whose control is something else.</summary>
    internal static ToggleButton? SwitchOf(HeaderedContentControl row) => row.Content as CheckBox;

    /// <summary>
    /// Flips the row's switch as a click on it would, unless it is off. Returns whether it did,
    /// so the click that asked for it is not also handled by something further out.
    /// </summary>
    internal static bool Toggle(HeaderedContentControl row)
    {
        if (SwitchOf(row) is not { IsEnabled: true } toggle)
        {
            return false;
        }

        toggle.IsChecked = toggle.IsChecked != true;
        return true;
    }

    private static void OnClickToToggleChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not HeaderedContentControl row)
        {
            return;
        }

        row.MouseLeftButtonDown -= OnDown;
        row.MouseLeftButtonUp -= OnUp;
        row.MouseEnter -= OnEnter;
        row.MouseLeave -= OnLeave;
        if (e.NewValue is true)
        {
            row.MouseLeftButtonDown += OnDown;
            row.MouseLeftButtonUp += OnUp;
            row.MouseEnter += OnEnter;
            row.MouseLeave += OnLeave;
        }
    }

    // The switch handles a click on itself and marks it handled, so these only see clicks on the
    // rest of the row. A release only counts when the press started on the same row.
    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is HeaderedContentControl row && SwitchOf(row) is not null)
        {
            row.SetValue(PressedProperty, true);
        }
    }

    private static void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not HeaderedContentControl row || !(bool)row.GetValue(PressedProperty))
        {
            return;
        }

        row.SetValue(PressedProperty, false);
        e.Handled = Toggle(row);
    }

    private static void OnEnter(object sender, MouseEventArgs e)
    {
        if (sender is HeaderedContentControl row && SwitchOf(row) is { } toggle)
        {
            row.Cursor = Cursors.Hand;
            SetHot(toggle, true);
        }
    }

    private static void OnLeave(object sender, MouseEventArgs e)
    {
        if (sender is not HeaderedContentControl row)
        {
            return;
        }

        row.SetValue(PressedProperty, false);
        if (SwitchOf(row) is { } toggle)
        {
            SetHot(toggle, false);
        }
    }
}
