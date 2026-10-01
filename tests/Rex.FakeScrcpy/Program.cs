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

        if (args.Contains("--list-apps"))
        {
            return ListApps(args);
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

        // A phone too old to send its sound: real scrcpy says so and carries on with the picture.
        var oldPhone = Path.Combine(Path.GetDirectoryName(LogPath) ?? AppContext.BaseDirectory, "no-audio");
        if (!args.Contains("--no-audio") && File.Exists(oldPhone))
        {
            Console.Error.WriteLine("WARN: Audio disabled: it is not supported before Android 11");
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
    /// The phone's apps the way real scrcpy prints them: a header, then one app a line with the
    /// name padded to 30 characters, and a long name with its package on the next line. It takes a
    /// moment, as the real one does, and logs when it starts and ends so a test can see it never
    /// overlaps another server starting. A marker file makes it fail the way a phone that left does.
    /// </summary>
    private static int ListApps(string[] args)
    {
        Log("list-apps start " + string.Join(' ', args));
        Thread.Sleep(300);
        if (File.Exists(Path.Combine(Path.GetDirectoryName(LogPath) ?? AppContext.BaseDirectory, "fail-list-apps")))
        {
            Console.Error.WriteLine("ERROR: Could not find any ADB device");
            Log("list-apps end failed");
            return 1;
        }

        (string Name, string Package, bool System)[] apps =
        [
            ("Example One", "com.example.one", false),
            ("Example Two", "com.example.two", false),
            ("Spotify", "com.spotify.music", false),
            ("YouTube", "com.google.android.youtube", false),
            ("Café Maps", "com.example.cafe", false),
            ("Notes", "com.example.notes", false),
            ("Notes", "org.other.notes", false),
            ("A Very Long Application Name Here", "com.example.longname", false),
            ("Calculator", "com.example.calculator", false),
            ("Weather", "com.example.weather", false),
            ("Banking", "com.example.bank", false),
            ("Podcasts", "com.example.podcasts", false),
            ("Settings", "com.android.settings", true),
            ("Phone", "com.android.dialer", true),
        ];
        var output = new StringBuilder("[server] INFO: List of apps:");
        foreach (var (name, package, system) in apps.OrderBy(a => !a.System).ThenBy(a => a.Name, StringComparer.Ordinal))
        {
            output.Append('\n').Append(system ? " * " : " - ").Append(name);
            output.Append(name.Length < 30 ? new string(' ', 30 - name.Length) : '\n' + new string(' ', 33));
            output.Append(' ').Append(package);
        }

        // The server's words reach scrcpy's output as the UTF-8 the phone sent, accents and all.
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(Encoding.UTF8.GetBytes(output.Append('\n').ToString()));
        Log("list-apps end");
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
        // When the session's window is up, its server has started: a test orders this against other starts.
        Shown += (_, _) => Program.Log($"shown at={_port}");

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

        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "home-screen")))
        {
            DrawHomeScreen(g, rect);
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

    /// <summary>
    /// A made-up home screen for the website's pictures: a clock, a search bar and rounded tiles in
    /// the app's own colours. Nothing in it comes from a real phone, an app or a brand.
    /// </summary>
    private static void DrawHomeScreen(Graphics g, Rectangle rect)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var background = new LinearGradientBrush(rect, Color.FromArgb(20, 27, 22), Color.FromArgb(44, 58, 47), 90f))
        {
            g.FillRectangle(background, rect);
        }

        var unit = Math.Max(1f, rect.Width / 360f);
        using var text = new SolidBrush(Color.FromArgb(242, 245, 238));
        using var small = new Font("Segoe UI", 9f * unit, FontStyle.Bold);
        using var clock = new Font("Segoe UI", 34f * unit, FontStyle.Regular);
        g.DrawString("9:41", small, text, 18 * unit, 10 * unit);
        g.FillRectangle(text, rect.Width - 40 * unit, 14 * unit, 20 * unit, 9 * unit);
        g.DrawString("9:41", clock, text, 22 * unit, 70 * unit);
        using var muted = new SolidBrush(Color.FromArgb(133, 141, 131));
        using var date = new Font("Segoe UI", 11f * unit);
        g.DrawString("Thursday 1 October", date, muted, 26 * unit, 128 * unit);

        Color[] colours =
        [
            Color.FromArgb(215, 255, 63), Color.FromArgb(255, 119, 77), Color.FromArgb(88, 166, 255), Color.FromArgb(186, 140, 255),
            Color.FromArgb(72, 214, 166), Color.FromArgb(255, 196, 77), Color.FromArgb(242, 245, 238), Color.FromArgb(255, 105, 150),
        ];
        var size = 54 * unit;
        var gap = (rect.Width - 4 * size) / 5f;
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                var x = gap + column * (size + gap);
                var y = 210 * unit + row * (size + 30 * unit);
                FillRounded(g, colours[(row * 4 + column) % colours.Length], x, y, size, size, 16 * unit);
            }
        }

        FillRounded(g, Color.FromArgb(48, 242, 245, 238), 18 * unit, rect.Height - 150 * unit, rect.Width - 36 * unit, 44 * unit, 22 * unit);
        for (var column = 0; column < 4; column++)
        {
            FillRounded(g, colours[column + 4], gap + column * (size + gap), rect.Height - 90 * unit, size, size, 16 * unit);
        }
    }

    private static void FillRounded(Graphics g, Color colour, float x, float y, float width, float height, float radius)
    {
        using var path = new GraphicsPath();
        path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
        path.AddArc(x + width - radius * 2, y, radius * 2, radius * 2, 270, 90);
        path.AddArc(x + width - radius * 2, y + height - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(x, y + height - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        using var brush = new SolidBrush(colour);
        g.FillPath(brush, path);
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
