using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FluxRoute.Services;

/// <summary>
/// Управляет системной иконкой FluxRoute и собственным WPF-окном трея.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ITrayPopupService _popupService;
    private readonly Icon _iconDefault;
    private Icon? _iconRunning;
    private Icon? _iconError;
    private bool _disposed;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService(ITrayPopupService popupService)
    {
        _popupService = popupService ?? throw new ArgumentNullException(nameof(popupService));
        _iconDefault = LoadEmbeddedIcon();

        _popupService.OpenApplicationRequested += OnPopupOpenApplicationRequested;
        _popupService.ExitApplicationRequested += OnPopupExitApplicationRequested;

        _notifyIcon = new NotifyIcon
        {
            Text = "FluxRoute",
            Icon = _iconDefault,
            Visible = false
        };
        _notifyIcon.MouseClick += OnNotifyIconMouseClick;
    }

    public void SetVisible(bool visible)
    {
        ThrowIfDisposed();
        _notifyIcon.Visible = visible;

        if (!visible)
            _popupService.Close();
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                try
                {
                    if (!_disposed && _notifyIcon.Visible)
                        _notifyIcon.ShowBalloonTip(3000, title, text, icon);
                }
                catch (InvalidOperationException)
                {
                    // Windows может удалить handle иконки во время завершения приложения.
                }
            }),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    public void UpdateTooltip(string text)
    {
        ThrowIfDisposed();
        _notifyIcon.Text = text.Length > 127 ? text[..127] : text;
    }

    /// <summary>
    /// Синхронизирует содержимое собственного окна трея с состоянием приложения.
    /// </summary>
    public void UpdateMenu(
        string? strategy,
        bool protectionRunning,
        bool orchestratorRunning,
        bool tgProxyRunning,
        bool gameFilterEnabled)
    {
        ThrowIfDisposed();
        _popupService.UpdateState(
            strategy,
            protectionRunning,
            orchestratorRunning,
            tgProxyRunning,
            gameFilterEnabled);
    }

    /// <summary>
    /// Меняет иконку в зависимости от состояния защиты.
    /// </summary>
    public void UpdateIcon(bool isRunning, bool isError = false)
    {
        ThrowIfDisposed();

        if (isError)
        {
            _iconError ??= BuildOverlayIcon(Color.FromArgb(0xE7, 0x4C, 0x3C));
            TrySetIcon(_iconError);
        }
        else if (isRunning)
        {
            _iconRunning ??= BuildOverlayIcon(Color.FromArgb(0x00, 0xD6, 0x8F));
            TrySetIcon(_iconRunning);
        }
        else
        {
            TrySetIcon(_iconDefault);
        }
    }

    private void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _popupService.Close();
            ShowRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (e.Button == MouseButtons.Right)
        {
            _popupService.ShowAtCursor();
        }
    }

    private void OnPopupOpenApplicationRequested(object? sender, EventArgs e) =>
        ShowRequested?.Invoke(this, EventArgs.Empty);

    private void OnPopupExitApplicationRequested(object? sender, EventArgs e) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);

    private void TrySetIcon(Icon icon)
    {
        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
                return;

            if (dispatcher.CheckAccess())
                ApplyIcon(icon);
            else
                _ = dispatcher.BeginInvoke(() => ApplyIcon(icon));
        }
        catch (InvalidOperationException)
        {
            // Handle трея мог быть уничтожен при завершении приложения.
        }
    }

    private void ApplyIcon(Icon icon)
    {
        try
        {
            if (!_disposed)
                _notifyIcon.Icon = icon;
        }
        catch (InvalidOperationException)
        {
            // Handle трея мог быть уничтожен при завершении приложения.
        }
    }

    private Icon BuildOverlayIcon(Color overlayColor)
    {
        const int overlayRadius = 5;
        const int overlayMargin = 2;

        using var baseBitmap = _iconDefault.ToBitmap();
        using var graphics = Graphics.FromImage(baseBitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var x = baseBitmap.Width - overlayRadius * 2 - overlayMargin;
        var y = baseBitmap.Height - overlayRadius * 2 - overlayMargin;

        using var outlineBrush = new SolidBrush(Color.FromArgb(0x0C, 0x0F, 0x17));
        graphics.FillEllipse(outlineBrush, x - 1, y - 1, overlayRadius * 2 + 2, overlayRadius * 2 + 2);

        using var statusBrush = new SolidBrush(overlayColor);
        graphics.FillEllipse(statusBrush, x, y, overlayRadius * 2, overlayRadius * 2);

        var handle = baseBitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            _ = DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Icon LoadEmbeddedIcon()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("FluxRoute.ico");
        return stream is not null ? new Icon(stream) : SystemIcons.Application;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _popupService.OpenApplicationRequested -= OnPopupOpenApplicationRequested;
        _popupService.ExitApplicationRequested -= OnPopupExitApplicationRequested;
        _popupService.Close();

        _notifyIcon.MouseClick -= OnNotifyIconMouseClick;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        _iconRunning?.Dispose();
        _iconError?.Dispose();
        _iconDefault.Dispose();
    }
}
