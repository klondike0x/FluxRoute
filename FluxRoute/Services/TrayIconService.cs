using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace FluxRoute.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private bool _disposed;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "FluxRoute",
            Icon = LoadEmbeddedIcon(),
            Visible = false,
            ContextMenuStrip = CreateContextMenu()
        };

        _notifyIcon.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetVisible(bool visible)
    {
        _notifyIcon.Visible = visible;
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (_notifyIcon.Visible)
            _notifyIcon.ShowBalloonTip(3000, title, text, icon);
    }

    public void UpdateTooltip(string text)
    {
        // NotifyIcon.Text has a 128 char limit
        _notifyIcon.Text = text.Length > 127 ? text[..127] : text;
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        var showItem = new ToolStripMenuItem("Открыть");
        showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(showItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    private static Icon LoadEmbeddedIcon()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream("FluxRoute.ico");
        if (stream is not null)
            return new Icon(stream);

        // Fallback: use default application icon
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
