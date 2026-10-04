using System.Drawing;
using System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace Rex.Mirror.Services;

/// <summary>
/// Paints the tray icon's menu in the app's own colours. The notification area only offers a
/// Windows Forms menu, which comes up light and square, the one piece of the app that looked like
/// it belonged to something else. Under High Contrast the system renderer is kept, so the menu uses
/// the colours the person chose.
/// </summary>
internal sealed class TrayMenuRenderer : WinForms.ToolStripProfessionalRenderer
{
    internal static readonly Color Panel = Color.FromArgb(0x11, 0x15, 0x12);
    internal static readonly Color Hover = Color.FromArgb(0x20, 0x28, 0x20);
    internal static readonly Color Line = Color.FromArgb(0x29, 0x30, 0x2A);
    internal static readonly Color Text = Color.FromArgb(0xF2, 0xF5, 0xEE);
    internal static readonly Color Muted = Color.FromArgb(0x85, 0x8D, 0x83);
    internal static readonly Color Signal = Color.FromArgb(0xD7, 0xFF, 0x3F);

    public TrayMenuRenderer()
        : base(new Palette())
    {
        RoundedEdges = false;
    }

    /// <summary>Styles a menu for the app, or leaves it to the system when High Contrast is on.</summary>
    public static void Apply(WinForms.ToolStrip menu)
    {
        if (System.Windows.SystemParameters.HighContrast)
        {
            menu.RenderMode = WinForms.ToolStripRenderMode.System;
            return;
        }

        menu.Renderer = new TrayMenuRenderer();
        menu.BackColor = Panel;
        menu.ForeColor = Text;
        menu.Padding = new WinForms.Padding(2, 4, 2, 4);
        foreach (WinForms.ToolStripItem item in menu.Items)
        {
            item.ForeColor = Text;
            if (item is WinForms.ToolStripMenuItem menuItem)
            {
                item.Padding = new WinForms.Padding(4, 5, 4, 5);
                // A submenu is a menu of its own, painted the same way.
                if (menuItem.HasDropDownItems)
                {
                    Apply(menuItem.DropDown);
                }
            }
        }
    }

    protected override void OnRenderArrow(WinForms.ToolStripArrowRenderEventArgs e)
    {
        // The stock arrow is black too.
        e.ArrowColor = e.Item?.Enabled == false ? Muted : Text;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(WinForms.ToolStripItemImageRenderEventArgs e)
    {
        // The stock tick is black, which disappears on a dark menu.
        var box = e.ImageRectangle;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Signal, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var x = box.Left + (box.Width - 12) / 2f;
        var y = box.Top + (box.Height - 10) / 2f;
        e.Graphics.DrawLines(pen, [new PointF(x, y + 5), new PointF(x + 4, y + 9), new PointF(x + 12, y + 1)]);
    }

    protected override void OnRenderSeparator(WinForms.ToolStripSeparatorRenderEventArgs e)
    {
        var bounds = e.Item.ContentRectangle;
        using var pen = new Pen(Line);
        var y = bounds.Top + bounds.Height / 2;
        e.Graphics.DrawLine(pen, bounds.Left + 8, y, bounds.Right - 8, y);
    }

    private sealed class Palette : WinForms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Panel;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color ImageMarginGradientBegin => Panel;
        public override Color ImageMarginGradientMiddle => Panel;
        public override Color ImageMarginGradientEnd => Panel;
        public override Color CheckBackground => Panel;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Panel;
    }
}
