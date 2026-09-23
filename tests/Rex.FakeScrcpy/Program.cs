using System.Diagnostics;
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

        if (args.Contains("--rex-test-child"))
        {
            Log("child-pid " + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }

        Log("pid " + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Log("args " + string.Join(' ', args));

        var shortcutModifier = args.FirstOrDefault(a => a.StartsWith("--shortcut-mod=", StringComparison.Ordinal));
        if (shortcutModifier?.Contains('+', StringComparison.Ordinal) == true)
        {
            Console.Error.WriteLine($"ERROR: Shortcut mod combination with '+' is not supported anymore: '{shortcutModifier[15..]}' (see #4741)");
            return 1;
        }

        if (args.Contains("--rex-spawn-child"))
        {
            var childStart = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            childStart.ArgumentList.Add("--rex-test-child");
            _ = Process.Start(childStart) ?? throw new InvalidOperationException("Could not start fake scrcpy child.");
        }

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

        // The window starts in the shape it was asked for. Anything a previous run left behind
        // would otherwise turn it before the test that owns it has begun.
        try
        {
            File.Delete(RotationFile);
        }
        catch (IOException)
        {
            // Another process has it open; the value below is read as the starting point instead.
        }

        _rotationWatch = new System.Windows.Forms.Timer { Interval = 100 };
        _rotationWatch.Tick += (_, _) => FollowRotation();
        _rotationWatch.Start();
    }

    private readonly System.Windows.Forms.Timer _rotationWatch;
    private bool _landscape;

    /// <summary>
    /// Turns the window when the phone turns, the way scrcpy does. The desktop app has no other way
    /// to learn that the picture changed shape, so without this the reflow cannot be tested.
    /// </summary>
    private static string RotationFile => Path.Combine(AppContext.BaseDirectory, "fake-rotation.txt");

    private void FollowRotation()
    {
        if (!File.Exists(RotationFile))
        {
            return;
        }

        string value;
        try
        {
            value = File.ReadAllText(RotationFile).Trim();
        }
        catch (IOException)
        {
            return;
        }

        var landscape = value is "1" or "3";
        if (landscape == _landscape)
        {
            return;
        }

        _landscape = landscape;
        ClientSize = new Size(ClientSize.Height, ClientSize.Width);
        Program.Log($"rotation {value} {ClientSize.Width}x{ClientSize.Height}");
        Invalidate();
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
        var fixture = Path.Combine(AppContext.BaseDirectory, "preview.png");
        if (File.Exists(fixture))
        {
            using var preview = Image.FromFile(fixture);
            g.DrawImage(preview, rect);
        }

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
        if (m.Msg == 0x020A) Program.Log("mousewheel");
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
