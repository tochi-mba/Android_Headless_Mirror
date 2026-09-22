using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;

namespace Rex.FakeScrcpy;

/// <summary>
/// A stand-in for scrcpy.exe: opens a plain top-level window with the requested title, size
/// and position, paints a phone-like gradient, and logs its arguments plus every keyboard
/// and touch message it receives. The desktop app embeds it exactly like the real thing.
/// </summary>
internal static class Program
{
    private static readonly string LogPath =
        Environment.GetEnvironmentVariable("REX_FAKE_SCRCPY_LOG") ?? Path.Combine(AppContext.BaseDirectory, "fake-scrcpy.log");

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--version"))
        {
            Console.WriteLine("scrcpy 4.1-fake <https://github.com/Genymobile/scrcpy>");
            return 0;
        }

        Log("args " + string.Join(' ', args));

        var exitAfter = Environment.GetEnvironmentVariable("REX_FAKE_SCRCPY_EXIT_MS");
        if (!string.IsNullOrWhiteSpace(exitAfter))
        {
            Thread.Sleep(int.Parse(exitAfter, CultureInfo.InvariantCulture));
            return int.TryParse(Environment.GetEnvironmentVariable("REX_FAKE_SCRCPY_EXIT_CODE"), out var code) ? code : 1;
        }

        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new MirrorForm(args));
        return 0;
    }

    public static void Log(string line)
    {
        try
        {
            File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (IOException)
        {
            // The test is reading the file; drop the line.
        }
    }
}

internal sealed class MirrorForm : Form
{
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;
    private const int WmPointerdown = 0x0246;
    private const int WmPointerup = 0x0247;

    private readonly string[] _args;

    public MirrorForm(string[] args)
    {
        _args = args;
        Text = Option("--window-title") ?? "scrcpy";
        FormBorderStyle = args.Contains("--window-borderless") ? FormBorderStyle.None : FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.Manual;
        var x = Int("--window-x", 100);
        var y = Int("--window-y", 100);
        var width = Int("--window-width", 400);
        var height = Int("--window-height", 860);
        Location = new Point(x, y);
        ClientSize = new Size(width, height);
        DoubleBuffered = true;
        BackColor = Color.Black;
        KeyPreview = true;
    }

    private string? Option(string name)
    {
        var prefix = name + "=";
        return _args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    }

    private int Int(string name, int fallback) =>
        int.TryParse(Option(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using var brush = new LinearGradientBrush(rect, Color.FromArgb(26, 32, 27), Color.FromArgb(56, 70, 58), 90f);
        g.FillRectangle(brush, rect);

        using var pen = new Pen(Color.FromArgb(215, 255, 63), 3f);
        g.DrawRectangle(pen, rect.X + 6, rect.Y + 6, rect.Width - 12, rect.Height - 12);

        using var font = new Font("Segoe UI", 14f, FontStyle.Bold);
        g.DrawString($"fake scrcpy\n{rect.Width}×{rect.Height}", font, Brushes.White, 20, 20);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg is WmKeydown or WmSyskeydown)
        {
            Program.Log($"key vk={(int)m.WParam} scan=0x{((int)m.LParam >> 16) & 0xFF:X2} ext={(((int)m.LParam >> 24) & 1)}");
        }
        else if (m.Msg == WmPointerdown)
        {
            Program.Log($"pointerdown id={(int)m.WParam & 0xFFFF}");
        }
        else if (m.Msg == WmPointerup)
        {
            Program.Log($"pointerup id={(int)m.WParam & 0xFFFF}");
        }

        base.WndProc(ref m);
    }
}
