using System.Windows;
using Rex.Core;
using Rex.Mirror.Session;
using WinForms = System.Windows.Forms;

namespace Rex.Mirror.Services;

/// <summary>The notification-area icon: the app's home while it waits for a phone in the background.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppHost _host;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _startup;
    private readonly WinForms.ToolStripMenuItem _stopStart;
    private readonly WinForms.ToolStripMenuItem _open;
    private readonly WinForms.ToolStripMenuItem _globalKeys;

    public TrayIcon(AppHost host)
    {
        _host = host;
        var menu = new WinForms.ContextMenuStrip();
        _open = new WinForms.ToolStripMenuItem("Open Android Headless Mirror") { Font = new System.Drawing.Font(WinForms.Control.DefaultFont, System.Drawing.FontStyle.Bold) };
        _open.Click += (_, _) => host.Window?.ShowFromTray();
        _stopStart = new WinForms.ToolStripMenuItem("Stop mirror");
        _stopStart.Click += (_, _) =>
        {
            if (host.Session.IsMirroring)
            {
                host.Session.StopMirror();
            }
            else
            {
                host.Session.StartAgain();
            }
        };
        _startup = new WinForms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        _startup.Click += (_, _) => host.Window?.SetStartWithWindows(_startup.Checked);
        _globalKeys = new WinForms.ToolStripMenuItem("Shortcuts from anywhere") { CheckOnClick = true };
        _globalKeys.Click += (_, _) => host.UpdateConfig(c => c.GlobalKeys.Enabled = _globalKeys.Checked);
        var quit = new WinForms.ToolStripMenuItem("Quit");
        quit.Click += (_, _) => host.Window?.QuitApplication();

        menu.Items.AddRange([_open, new WinForms.ToolStripSeparator(), _stopStart, _startup, _globalKeys, new WinForms.ToolStripSeparator(), quit]);
        TrayMenuRenderer.Apply(menu);
        // High Contrast can be switched while the app runs; the menu follows at the next opening.
        menu.Opening += (_, _) => { TrayMenuRenderer.Apply(menu); Refresh(); };

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Android Headless Mirror",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => host.Window?.ShowFromTray();
        host.Session.Changed += Refresh;
    }

    public void Refresh()
    {
        var session = _host.Session;
        _stopStart.Text = session.IsMirroring ? "Stop mirror" : "Start mirror";
        _stopStart.Enabled = session.Phase != SessionPhase.NeedsSetup;
        _startup.Checked = StartupRegistration.IsEnabled();
        var keys = _host.Config.GlobalKeys;
        _globalKeys.Checked = keys.Enabled;
        // The key that brings the window up, beside the item that does the same.
        _open.ShortcutKeyDisplayString = keys.Enabled ? keys.ShowHide : string.Empty;
        var text = session.Phase == SessionPhase.Mirroring && session.Identity is not null
            ? $"Android Headless Mirror · {session.Identity.DisplayName}"
            : "Android Headless Mirror · " + Trim(session.Message, 40);
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void Notify(string title, string text) => _icon.ShowBalloonTip(3000, title, text, WinForms.ToolTipIcon.None);

    private static System.Drawing.Icon LoadIcon()
    {
        var uri = new Uri("pack://application:,,,/Assets/rex.ico");
        using var stream = Application.GetResourceStream(uri)!.Stream;
        return new System.Drawing.Icon(stream);
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    public void Dispose()
    {
        _host.Session.Changed -= Refresh;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
