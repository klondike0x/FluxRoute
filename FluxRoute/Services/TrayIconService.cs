using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace FluxRoute.Services;

/// <summary>
/// v1.6.0: Расширенный трей-сервис с динамическим контекстным меню и индикаторами статуса.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private bool _disposed;

    // Динамические пункты меню — храним ссылки для обновления Enabled/Text без пересоздания
    private readonly ToolStripMenuItem _strategyItem;
    private readonly ToolStripMenuItem _orchestratorItem;
    private readonly ToolStripMenuItem _tgProxyItem;
    private readonly ToolStripMenuItem _gameFilterItem;

    // Иконки для разных статусов (кешируем, чтобы не грузить из ресурсов каждый раз)
    private readonly Icon _iconDefault;
    private Icon? _iconRunning;
    private Icon? _iconError;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        _iconDefault = LoadEmbeddedIcon();

        // Пункты меню создаются один раз при конструировании
        var showItem = new ToolStripMenuItem("Открыть");
        showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        _strategyItem = new ToolStripMenuItem("Стратегия: —")
        {
            Enabled = false,
            ForeColor = Color.FromArgb(0x8B, 0x94, 0x9E)
        };

        _orchestratorItem = new ToolStripMenuItem("Оркестратор: ВЫКЛ")
        {
            Enabled = false,
            ForeColor = Color.FromArgb(0x4A, 0x51, 0x70)
        };

        _tgProxyItem = new ToolStripMenuItem("Telegram-прокси: ВЫКЛ")
        {
            Enabled = false,
            ForeColor = Color.FromArgb(0x4A, 0x51, 0x70)
        };

        _gameFilterItem = new ToolStripMenuItem("Игровой режим: ВЫКЛ")
        {
            Enabled = false,
            ForeColor = Color.FromArgb(0x4A, 0x51, 0x70)
        };

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new ContextMenuStrip();
        menu.Items.Add(showItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_strategyItem);
        menu.Items.Add(_orchestratorItem);
        menu.Items.Add(_tgProxyItem);
        menu.Items.Add(_gameFilterItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = "FluxRoute",
            Icon = _iconDefault,
            Visible = false,
            ContextMenuStrip = menu
        };

        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
                ShowRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    // ── Публичные методы (обратная совместимость) ──

    public void SetVisible(bool visible)
    {
        _notifyIcon.Visible = visible;
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                try
                {
                    if (_notifyIcon.Visible)
                        _notifyIcon.ShowBalloonTip(3000, title, text, icon);
                }
                catch (InvalidOperationException) { }
            }),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    public void UpdateTooltip(string text)
    {
        _notifyIcon.Text = text.Length > 127 ? text[..127] : text;
    }

    // ── v1.6.0: Динамическое обновление меню ──

    private static readonly Color GreenOn = Color.FromArgb(0x00, 0xD6, 0x8F);
    private static readonly Color GrayOff = Color.FromArgb(0x4A, 0x51, 0x70);

    /// <summary>
    /// Обновляет все статусные пункты меню трея.
    /// </summary>
    public void UpdateMenu(string? strategy, bool orchestratorRunning, bool tgProxyRunning, bool gameFilterEnabled)
    {
        _strategyItem.Text = string.IsNullOrEmpty(strategy)
            ? "Стратегия: —"
            : $"Стратегия: {strategy}";
        _strategyItem.ForeColor = string.IsNullOrEmpty(strategy) ? GrayOff : Color.FromArgb(0xC9, 0xD1, 0xD9);

        _orchestratorItem.Text = orchestratorRunning ? "Оркестратор: ВКЛ" : "Оркестратор: ВЫКЛ";
        _orchestratorItem.ForeColor = orchestratorRunning ? GreenOn : GrayOff;

        _tgProxyItem.Text = tgProxyRunning ? "Telegram-прокси: ВКЛ" : "Telegram-прокси: ВЫКЛ";
        _tgProxyItem.ForeColor = tgProxyRunning ? GreenOn : GrayOff;

        _gameFilterItem.Text = gameFilterEnabled ? "Игровой режим: ВКЛ" : "Игровой режим: ВЫКЛ";
        _gameFilterItem.ForeColor = gameFilterEnabled ? GreenOn : GrayOff;
    }

    // ── v1.6.0: Цветная иконка по статусу ──

    private const int OverlayRadius = 5;
    private const int OverlayMargin = 2;

    /// <summary>
    /// Меняет иконку в трее в зависимости от статуса:
    /// Running — зелёный кружок, Stopped — серая иконка, Error — красный кружок.
    /// </summary>
    public void UpdateIcon(bool isRunning, bool isError = false)
    {
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

    private void TrySetIcon(Icon icon)
    {
        // NotifyIcon.Icon — property, setter может бросить если handle не готов.
        // Обёртка защищает от краша при быстрой смене состояний.
        try
        {
            // Доступ через BeginInvoke на UI-потоке для thread-safety
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
                return;

            if (dispatcher.CheckAccess())
                ApplyIcon(icon);
            else
                _ = dispatcher.BeginInvoke(() => ApplyIcon(icon));
        }
        catch { }
    }

    private void ApplyIcon(Icon icon)
    {
        try { _notifyIcon.Icon = icon; }
        catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Накладывает цветной кружок в правый нижний угол иконки.
    /// </summary>
    private Icon BuildOverlayIcon(Color overlayColor)
    {
        var baseBitmap = _iconDefault.ToBitmap();
        var w = baseBitmap.Width;
        var h = baseBitmap.Height;

        using var g = Graphics.FromImage(baseBitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var x = w - OverlayRadius * 2 - OverlayMargin;
        var y = h - OverlayRadius * 2 - OverlayMargin;

        // Тёмная обводка для контраста
        using var outlineBrush = new SolidBrush(Color.FromArgb(0x0C, 0x0F, 0x17));
        g.FillEllipse(outlineBrush, x - 1, y - 1, OverlayRadius * 2 + 2, OverlayRadius * 2 + 2);

        // Цветной индикатор
        using var brush = new SolidBrush(overlayColor);
        g.FillEllipse(brush, x, y, OverlayRadius * 2, OverlayRadius * 2);

        return Icon.FromHandle(baseBitmap.GetHicon());
    }

    // ── Иконка по умолчанию ──

    private static Icon LoadEmbeddedIcon()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream("FluxRoute.ico");
        if (stream is not null)
            return new Icon(stream);

        return SystemIcons.Application;
    }

    // ── IDisposable ──

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        _iconDefault?.Dispose();
        _iconRunning?.Dispose();
        _iconError?.Dispose();
    }
}
