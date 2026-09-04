using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TheWindowHider.UI;

/// <summary>
/// Owns the notification-area icon and its menu. Uses WinForms NotifyIcon so there is no
/// external tray dependency and no binary icon asset to ship (the icon is drawn at runtime).
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _toggleItem;
    private IntPtr _iconHandle;
    private bool _masterEnabled = true;

    public event Action? OpenRequested;
    public event Action? ExitRequested;
    public event Action<bool>? SetHidingRequested; // desired master state

    public TrayIconManager()
    {
        Icon icon = BuildIcon();

        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Open The Window Hider");
        openItem.Click += (_, _) => OpenRequested?.Invoke();
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);

        _toggleItem = new ToolStripMenuItem("Pause hiding");
        _toggleItem.Click += (_, _) => SetHidingRequested?.Invoke(!_masterEnabled);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "The Window Hider",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public void SetMasterState(bool enabled)
    {
        _masterEnabled = enabled;
        _toggleItem.Text = enabled ? "Pause hiding" : "Resume hiding";
        _notifyIcon.Text = enabled ? "The Window Hider (active)" : "The Window Hider (paused)";
    }

    public void ShowBalloon(string title, string text)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.ShowBalloonTip(2500);
    }

    private Icon BuildIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Rounded teal square backdrop.
            using var back = new SolidBrush(Color.FromArgb(255, 45, 212, 191));
            FillRoundedRect(g, back, new Rectangle(1, 1, 30, 30), 8);

            // A simple "eye" glyph.
            using var dark = new SolidBrush(Color.FromArgb(255, 15, 23, 42));
            using var pen = new Pen(Color.FromArgb(255, 15, 23, 42), 2.4f);
            g.DrawArc(pen, new Rectangle(6, 9, 20, 14), 200, 140);   // upper lid
            g.DrawArc(pen, new Rectangle(6, 9, 20, 14), 20, 140);    // lower lid
            g.FillEllipse(dark, new Rectangle(13, 12, 6, 6));        // pupil

            // Diagonal "hidden" slash.
            using var slash = new Pen(Color.FromArgb(255, 15, 23, 42), 3f);
            g.DrawLine(slash, 7, 25, 25, 7);
        }

        _iconHandle = bmp.GetHicon();
        return Icon.FromHandle(_iconHandle);
    }

    private static void FillRoundedRect(Graphics g, Brush brush, Rectangle r, int radius)
    {
        using var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (_iconHandle != IntPtr.Zero)
            DestroyIcon(_iconHandle);
    }
}
