using System.Windows;
using System.Windows.Controls;
using Rex.Core;

namespace Rex.Mirror.Mirror;

/// <summary>
/// Lays out the phone's own view, its copies, and a second screen in the mirror area.
///
/// The first child is always the main view. On its own it fills the area, so zoom has all of it to
/// use, exactly as before copies existed. With copies, every view gets an equal cell at full height,
/// side by side with the configured gap (<see cref="CopiesLayout"/>). Copies that do not fit, and
/// every copy while the phone is on its side, get no room at all; <see cref="Arranged"/> tells the
/// window which views are showing so it can hide the rest and keep the overlay in step.
///
/// While a second screen is open it is the second child, and the phone and it share the area as
/// <see cref="ViewsLayout"/> says (copies wait meanwhile, with no room).
/// </summary>
public sealed class MirrorGroupPanel : Panel
{
    /// <summary>The picture's width over its height; copies are shown only while it is below one.</summary>
    public double Aspect { get; set; } = 1080.0 / 2400.0;

    /// <summary>The space between views, in device-independent pixels.</summary>
    public double Gap { get; set; } = 12;

    /// <summary>How many views were given room in the last arrangement (the main view always is).</summary>
    public int Shown { get; private set; } = 1;

    /// <summary>The second screen's view while one is open; it is laid out with the phone, not as a copy.</summary>
    public UIElement? Screen { get; set; }

    public SecondScreenSettings ScreenSettings { get; set; } = new();

    public ViewsSettings ViewsSettings { get; set; } = new();

    /// <summary>The splitter's share for the phone, or 0 for its natural size.</summary>
    public double Split { get; set; }

    /// <summary>How the phone and the second screen were placed last, or null without a second screen.</summary>
    public ViewsArrangement? Arrangement { get; private set; }

    /// <summary>Raised after every arrangement, with the cell each shown view got (DIPs, in this panel).</summary>
    public event Action<IReadOnlyList<Rect>>? Arranged;

    private bool HasScreen => Screen is not null && Children.Count > 1 && ReferenceEquals(Children[1], Screen);

    /// <summary>The number of views that would be shown for the given size, without arranging.</summary>
    public int ShownFor(double width) =>
        HasScreen ? 2 : Children.Count <= 1 || !CopiesLayout.IsUpright(Aspect) ? 1 : CopiesLayout.ShownCount(Children.Count, width, Gap);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : 0;
        var cells = Cells(width, height);
        for (var i = 0; i < Children.Count; i++)
        {
            var cell = i < cells.Count ? cells[i] : default;
            Children[i].Measure(new Size(cell.Width, cell.Height));
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cells = Cells(finalSize.Width, finalSize.Height);
        var shown = new List<Rect>(cells.Count);
        for (var i = 0; i < Children.Count; i++)
        {
            if (i < cells.Count)
            {
                var cell = cells[i];
                var rect = new Rect(cell.X, cell.Y, Math.Max(0, cell.Width), Math.Max(0, cell.Height));
                Children[i].Arrange(rect);
                shown.Add(rect);
            }
            else
            {
                // Not shown: no room. The window hides its native viewport as well, so nothing of
                // it can stay painted where the last layout left it.
                Children[i].Arrange(new Rect(0, 0, 0, 0));
            }
        }

        Shown = shown.Count;
        Arranged?.Invoke(shown);
        return finalSize;
    }

    private IReadOnlyList<RectD> Cells(double width, double height)
    {
        if (!HasScreen)
        {
            Arrangement = null;
            return CopiesLayout.Cells(width, height, Aspect, ShownFor(width), Gap);
        }

        // The phone gets no cell at all when the second screen is instead of it, or squeezed out.
        Arrangement = ViewsLayout.Arrange(width, height, Aspect, ScreenSettings, ViewsSettings, Gap, Split);
        return [Arrangement.Phone ?? new RectD(0, 0, 0, 0), Arrangement.Screen];
    }
}
