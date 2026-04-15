using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Application = System.Windows.Application;

using FluxRoute.Views;

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    private async Task CheckUpdatesOnStartupAsync()
    {
        if (!AutoUpdateEnabled) return;

        var (update, _) = await _updater.CheckForUpdateAsync(EngineDir);
        if (update is null) return;

        _pendingUpdate = update;
        if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateStatus = $"Доступна новая версия: {update.Version}";

                if (CustomDialog.Show(
                    "⬆️ Обновление доступно",
                    $"Доступно обновление Flowseal zapret!\n\nВерсия: {update.Version}\n\nОбновить сейчас?",
                    "Обновить", "Позже"))
                    _ = InstallUpdateAsync();
            });
        }
    }

    [RelayCommand]
    private async Task CheckUpdates()
    {
        UpdateStatus = "🔍 Проверяем обновления...";
        var (update, error) = await _updater.CheckForUpdateAsync(EngineDir);

        if (update is null)
        {
            if (error is not null)
            {
                UpdateStatus = $"❌ {error}";
                Logs.Add($"Ошибка проверки: {error}");
                UpdateLogs.Add($"❌ {error}");
            }
            else
            {
                UpdateStatus = $"✅ Актуальная версия ({CurrentEngineVersion})";
                Logs.Add("Обновлений не найдено.");
                UpdateLogs.Add("✅ Обновлений не найдено");
            }
            return;
        }

        _pendingUpdate = update;
        UpdateStatus = $"⬆️ Доступна версия {update.Version}";
        Logs.Add($"Доступно обновление: {update.Version}");
        UpdateLogs.Add($"⬆️ Доступно: {update.Version}");
    }

    [RelayCommand]
    private async Task InstallUpdates()
    {
        if (_pendingUpdate is null)
        {
            await CheckUpdates();
            if (_pendingUpdate is null)
            {
                UpdateStatus = "🔄 Принудительная проверка...";
                var (latest, forceError) = await _updater.GetLatestReleaseAsync();
                if (latest is null)
                {
                    var errMsg = forceError ?? "Неизвестная ошибка";
                    UpdateStatus = $"❌ {errMsg}";
                    UpdateLogs.Add($"❌ {errMsg}");
                    return;
                }

                if (!CustomDialog.Show(
                    "🔄 Принудительное обновление",
                    $"Локальная версия совпадает с последней ({latest.Version}).\n\nПринудительно переустановить Flowseal?\nЭто скачает и заменит все файлы engine/.",
                    "Переустановить", "Отмена", isDanger: true))
                {
                    UpdateStatus = $"✅ Актуальная версия ({CurrentEngineVersion})";
                    return;
                }

                _pendingUpdate = latest;
                UpdateLogs.Add($"🔄 Принудительная переустановка {latest.Version}...");
            }
        }
        await InstallUpdateAsync();
    }

    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate is null) return;
        IsUpdating = true;
        Stop();

        var success = await _updater.InstallUpdateAsync(EngineDir, _pendingUpdate,
            msg =>
            {
                if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateStatus = msg;
                        Logs.Add(msg);
                        UpdateLogs.Add(msg);
                    });
                }
            });

        if (success)
        {
            CurrentEngineVersion = _updater.GetLocalVersion(EngineDir);
            _pendingUpdate = null;
            LoadProfiles();
        }
        else
        {
            UpdateLogs.Add("⚠️ Нажмите «Обновить» для повторной попытки");
        }

        IsUpdating = false;
    }

    private async Task AutoDownloadEngineAsync()
    {
        IsDownloadingEngine = true;
        EngineDownloadStatus = "🔍 Поиск последней версии Flowseal...";

        try
        {
            var (update, error) = await _updater.GetLatestReleaseAsync();
            if (update is null)
            {
                if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        EngineDownloadStatus = $"❌ {error ?? "Не удалось получить информацию о релизе"}";
                        Logs.Add($"❌ Flowseal: {error ?? "неизвестная ошибка"}");
                        IsDownloadingEngine = false;
                    });
                }
                return;
            }

            var confirmed = false;
            if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
            {
                confirmed = Application.Current.Dispatcher.Invoke(() =>
                    CustomDialog.Show(
                        "⬇️ Скачивание Flowseal",
                        $"Для работы FluxRoute необходим движок Flowseal (v{update.Version}).\n\n" +
                        $"Источник: официальный GitHub-репозиторий\n" +
                        $"github.com/Flowseal/zapret-discord-youtube\n\n" +
                        $"Ссылка на скачивание:\n{update.DownloadUrl}\n\n" +
                        $"Это open-source проект — исходный код доступен публично.\n" +
                        $"После скачивания SHA-256 хеш будет отображён в логах.",
                        "Скачать", "Отмена"));
            }

            if (!confirmed)
            {
                if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        EngineDownloadStatus = "⏹ Скачивание отменено пользователем";
                        Logs.Add("⏹ Пользователь отменил скачивание Flowseal");
                        IsDownloadingEngine = false;
                    });
                }
                return;
            }

            var success = await _updater.InstallUpdateAsync(EngineDir, update,
                msg =>
                {
                    if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            EngineDownloadStatus = msg;
                            Logs.Add(msg);
                        });
                    }
                });

            if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (success)
                    {
                        CurrentEngineVersion = _updater.GetLocalVersion(EngineDir);
                        EngineDownloadStatus = $"✅ Flowseal {update.Version} установлен!";
                        Logs.Add($"✅ Flowseal {update.Version} установлен автоматически");
                        AddToRecentLogs($"✅ Flowseal {update.Version} установлен");
                        LoadProfiles();
                        RefreshDiagnostics();
                    }
                    else
                    {
                        Logs.Add("❌ Установка Flowseal не завершена");
                        AddToRecentLogs("❌ Ошибка установки Flowseal");
                    }
                    IsDownloadingEngine = false;
                });
            }
        }
        catch (Exception ex)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    EngineDownloadStatus = $"❌ Ошибка: {ex.Message}";
                    Logs.Add($"❌ Автоскачивание Flowseal: {ex.Message}");
                    IsDownloadingEngine = false;
                });
            }
        }
    }

    private void DisableNativeUpdateCheck()
    {
        try
        {
            var flagFile = Path.Combine(EngineDir, "utils", "check_updates.enabled");
            if (File.Exists(flagFile)) { File.Delete(flagFile); Logs.Add("Встроенная проверка обновлений zapret отключена."); }
        }
        catch (Exception ex) { Logs.Add($"Не удалось отключить проверку обновлений: {ex.Message}"); }
    }
}
