using System.Windows;
using System.Windows.Interop;
using FluxRoute.ViewModels;
using FluxRoute.Views.Tray;
using Microsoft.Extensions.Logging;
using Application = System.Windows.Application;
using Point = System.Windows.Point;
using FormsCursor = System.Windows.Forms.Cursor;
using FormsScreen = System.Windows.Forms.Screen;

namespace FluxRoute.Services;

public sealed class TrayPopupService : ITrayPopupService
{
    private const string PreviewArgument = "--tray-preview";
    private readonly TrayPopupViewModel _viewModel;
    private readonly ILogger<TrayPopupService>? _logger;
    private TrayPopupWindow? _window;
    private bool _previewMode;

    public event EventHandler? OpenApplicationRequested;
    public event EventHandler? ExitApplicationRequested;

    public TrayPopupService(ILogger<TrayPopupService>? logger = null)
    {
        _logger = logger;
        _viewModel = new TrayPopupViewModel(
            openApplication: RequestOpenApplication,
            exitApplication: RequestExitApplication)
        {
            Version = $"v{typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.6.0"}"
        };
    }

    public static bool IsPreviewRequested(string[]? arguments = null) =>
        (arguments ?? Environment.GetCommandLineArgs())
        .Contains(PreviewArgument, StringComparer.OrdinalIgnoreCase);

    public void ShowPreview()
    {
        UpdateState(
            strategy: "General (ALT)",
            protectionIsRunning: true,
            orchestratorIsRunning: true,
            tgProxyIsRunning: false,
            gameFilterIsEnabled: true);
        _previewMode = true;
        ShowAtCursor();
    }

    public void ShowAtCursor()
    {
        RunOnUiThread(() =>
        {
            try
            {
                CloseWindow();

                var window = new TrayPopupWindow(_viewModel, closeOnDeactivate: !_previewMode)
                {
                    Left = -10000,
                    Top = -10000,
                    Opacity = 0
                };
                window.Closed += OnWindowClosed;
                _window = window;

                window.Show();
                window.UpdateLayout();
                PositionAtCursor(window);
                window.Opacity = 1;
                window.Activate();
                window.Focus();
                _previewMode = false;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Не удалось показать собственное окно трея.");
                CloseWindow();
            }
        });
    }

    public void Close() => RunOnUiThread(CloseWindow);

    public void UpdateState(
        string? strategy,
        bool protectionIsRunning,
        bool orchestratorIsRunning,
        bool tgProxyIsRunning,
        bool gameFilterIsEnabled)
    {
        RunOnUiThread(() => _viewModel.UpdateState(
            strategy,
            protectionIsRunning,
            orchestratorIsRunning,
            tgProxyIsRunning,
            gameFilterIsEnabled));
    }

    private static void PositionAtCursor(TrayPopupWindow window)
    {
        var cursor = FormsCursor.Position;
        var screen = FormsScreen.FromPoint(cursor);
        var source = PresentationSource.FromVisual(window);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;

        var cursorDip = fromDevice.Transform(new Point(cursor.X, cursor.Y));
        var workTopLeft = fromDevice.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var workBottomRight = fromDevice.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        var bounds = TrayPopupPlacement.Calculate(
            cursorDip.X,
            cursorDip.Y,
            window.ActualWidth,
            window.ActualHeight,
            workTopLeft.X,
            workTopLeft.Y,
            workBottomRight.X,
            workBottomRight.Y);

        window.Left = bounds.Left;
        window.Top = bounds.Top;
    }

    private void RequestOpenApplication()
    {
        CloseWindow();
        OpenApplicationRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RequestExitApplication()
    {
        CloseWindow();
        ExitApplicationRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _window))
            _window = null;
    }

    private void CloseWindow()
    {
        if (_window is null)
            return;

        var window = _window;
        _window = null;
        window.Closed -= OnWindowClosed;
        window.Close();
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
            return;

        if (dispatcher.CheckAccess())
            action();
        else
            _ = dispatcher.BeginInvoke(action);
    }
}
