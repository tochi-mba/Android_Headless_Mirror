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

        var denyUhid = Path.Combine(Path.GetDirectoryName(LogPath) ?? AppContext.BaseDirectory, "deny-uhid");
        if (args.Contains("--keyboard=uhid") && File.Exists(denyUhid))
        {
            Console.Error.WriteLine("[server] ERROR: Controller error");
            Console.Error.WriteLine("java.io.IOException: android.system.ErrnoException: open failed: EACCES (Permission denied)");
            Console.Error.WriteLine("at com.genymobile.scrcpy.control.UhidManager.open(UhidManager.java:84)");
            return 1;
        }

        // A phone with no video encoder left: every copy fails the way real scrcpy reports it.
        var failCopies = Path.Combine(Path.GetDirectoryName(LogPath) ?? AppContext.BaseDirectory, "fail-copies");
        if (args.Contains("--no-cleanup") && File.Exists(failCopies))
        {
            Console.Error.WriteLine("[server] ERROR: Could not create default video encoder for h264");
            Console.Error.WriteLine("[server] ERROR: Exception on thread Thread[video,5,main]");
            return 1;
        }

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

    /// <summary>
    /// Appends one line. The main session and every copy are processes of their own writing the
    /// same log, so a writer that finds it held by another waits its turn instead of dropping the
    /// line: a test waiting for that line would otherwise wait in vain. One writer at a time, so
    /// no two lines land on the same spot; the tests read alongside without getting in the way.
    /// </summary>
    public static void Log(string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            try
            {
                using var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete);
                stream.Write(bytes);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(5);
            }
        }
    }
}

internal sealed class MirrorForm : Form
{
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;
    private const int WmPointerdown = 0x0246;
    private const int WmPointerup = 0x0247;
    private const int WmKeyup = 0x0101;
    private const int VkRightControl = 0xA3;
    private const int WmLbuttondown = 0x0201;

    private readonly string[] _args;

    /// <summary>
    /// The session's port, which says which one it is: the main session's, or a copy's. Every input
    /// line in the log carries it, so a test can tell which view a click or a key reached.
    /// </summary>
    private readonly string _port;

    public MirrorForm(string[] args)
    {
        _args = args;
        _port = Option("--port") ?? "27183";
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
        // would otherwise turn it before the test that owns it has begun. A copy joins a phone that
        // is already mirrored, in whatever way it is turned, so it leaves the phone's state alone.
        if (!args.Contains("--no-cleanup"))
        {
            try
            {
                File.Delete(RotationFile);
            }
            catch (IOException)
            {
                // Another process has it open; the value below is read as the starting point instead.
            }
        }

        _rotationWatch = new System.Windows.Forms.Timer { Interval = 100 };
        _rotationWatch.Tick += (_, _) => FollowRotation();
        _rotationWatch.Start();

        // Real scrcpy prints this at its first frame and every time the picture changes shape.
        ReportTexture(landscape: false);

        // scrcpy's frame rate counter: on from the start with --print-fps, turned on and off with
        // the shortcut (Right Ctrl + I), and printing a reading every second while it runs.
        _frameRateCounter = args.Contains("--print-fps");
        _frameRate = new System.Windows.Forms.Timer { Interval = 1000 };
        _frameRate.Tick += (_, _) =>
        {
            if (_frameRateCounter)
            {
                Console.Out.WriteLine("INFO: 60 fps");
                Console.Out.Flush();
            }
        };
        _frameRate.Start();

        // A phone playing a video: with the marker present the whole picture cycles through
        // colours, so a test can see the soft background and the navigator follow it.
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "animate")))
        {
            _animation = new System.Windows.Forms.Timer { Interval = 120 };
            _animation.Tick += (_, _) => { _frame++; Invalidate(); };
            _animation.Start();
        }
    }

    private readonly System.Windows.Forms.Timer? _animation;
    private int _frame;
    private readonly System.Windows.Forms.Timer _frameRate;
    private bool _frameRateCounter;
    private bool _rightControl;

    /// <summary>A strong, clearly different colour for each frame of the fake video.</summary>
    internal static Color FrameColour(int frame) => (frame % 3) switch
    {
        0 => Color.FromArgb(230, 40, 40),
        1 => Color.FromArgb(40, 200, 60),
        _ => Color.FromArgb(40, 90, 240),
    };

    private readonly System.Windows.Forms.Timer _rotationWatch;

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

        // Keep the window the shape the phone is in, rather than turning it once and hoping.
        //
        // The app resizes this window to wherever it thinks the picture goes, so a single swap can
        // be undone a moment later by a resize that was decided before the turn. Checking the shape
        // every tick means the two converge whichever order they happen in, and once the app has
        // adopted the new aspect there is nothing left to correct.
        var landscape = value is "1" or "3";
        if (landscape != _reportedLandscape)
        {
            ReportTexture(landscape);
        }

        // With the marker present the window keeps its size when the phone turns, the case where
        // scrcpy's own resize never reaches the app: only the texture report says the phone turned.
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "keep-window")))
        {
            return;
        }

        if (landscape == ClientSize.Width > ClientSize.Height)
        {
            return;
        }

        // scrcpy does not swap the window's sides: it fits the turned picture inside the window it
        // already has, so going to landscape keeps the portrait width and the picture arrives
        // small. It is the app's job to notice and give it the room.
        var aspect = (double)ClientSize.Height / ClientSize.Width;
        var fitted = ClientSize.Width / (double)ClientSize.Height > aspect
            ? new Size(Math.Max(1, (int)Math.Round(ClientSize.Height * aspect)), ClientSize.Height)
            : new Size(ClientSize.Width, Math.Max(1, (int)Math.Round(ClientSize.Width / aspect)));
        ClientSize = fitted;
        Program.Log($"rotation {value} {ClientSize.Width}x{ClientSize.Height}");
        Invalidate();
    }

    private bool? _reportedLandscape;

    private void ReportTexture(bool landscape)
    {
        _reportedLandscape = landscape;
        Console.Out.WriteLine(landscape ? "INFO: Texture: 2400x1080" : "INFO: Texture: 1080x2400");
        Console.Out.Flush();
        Program.Log(landscape ? "texture 2400x1080" : "texture 1080x2400");
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

        if (_animation is not null)
        {
            using var solid = new SolidBrush(FrameColour(_frame / 4));
            g.FillRectangle(solid, rect);
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
        var copy = _port == "27183" ? string.Empty : " · copy " + (int.Parse(_port, CultureInfo.InvariantCulture) - 27183).ToString(CultureInfo.InvariantCulture);
        g.DrawString($"fake scrcpy{copy}\n{rect.Width}×{rect.Height}", font, Brushes.White, 20, 20);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x020A) Program.Log($"mousewheel at={_port}");
        if (m.Msg is WmKeydown or WmKeyup && (int)m.WParam == VkRightControl)
        {
            _rightControl = m.Msg == WmKeydown;
        }

        if (m.Msg == WmKeydown && (int)m.WParam == 'I' && _rightControl)
        {
            _frameRateCounter = !_frameRateCounter;
            Program.Log($"fps counter {(_frameRateCounter ? "on" : "off")} at={_port}");
        }

        if (m.Msg is WmKeydown or WmSyskeydown)
        {
            Program.Log($"key vk={(int)m.WParam} scan=0x{((int)m.LParam >> 16) & 0xFF:X2} ext={(((int)m.LParam >> 24) & 1)} at={_port}");
        }
        else if (m.Msg == WmPointerdown)
        {
            Program.Log($"pointerdown id={(int)m.WParam & 0xFFFF} at={_port}");
        }
        else if (m.Msg == WmPointerup)
        {
            Program.Log($"pointerup id={(int)m.WParam & 0xFFFF} at={_port}");
        }
        else if (m.Msg == WmLbuttondown)
        {
            Program.Log($"click at={_port}");
        }

        base.WndProc(ref m);
    }
}
