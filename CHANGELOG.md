# Changelog — v1.4.0

> Все изменения относительно [v1.3.1](https://github.com/klondike0x/FluxRoute/releases/tag/v1.3.1)

---

## ✨ Новые возможности

### TG WS Proxy — переход на Flowseal tg-ws-proxy
- Полная миграция с `mtg` на [Flowseal/tg-ws-proxy](https://github.com/Flowseal/tg-ws-proxy)
- Автоматическая установка Python Embeddable 3.11 + pip + cryptography (~12 МБ)
- Поддержка dd-secret (32 hex), Cloudflare Proxy, настройка DC→IP, параметры производительности (`buf-kb`, `pool-size`, `log-max-mb`)
- Генерация секрета одной кнопкой
- Версионирование через `version.txt`, проверка и обновление исходников прокси
- Кнопка «Открыть в Telegram» и «Скопировать ссылку» для быстрого подключения

### Вкладка «Логи»
- Новая объединённая вкладка с полным историческим логом: системный, оркестратор, проверки профилей, winws.exe, TG WS Proxy, обновления, сервис
- Фильтрация по источнику
- Экспорт всех логов в `.txt`

### Вкладка «Обновление» — улучшения
- Отображение **доступной удалённой версии** Flowseal рядом с текущей
- Лог обновления всегда виден (раньше был скрыт)
- Секция **TG WS Proxy**: текущая версия, статус установки, кнопка «Проверить обновления» — перенесена из вкладки TG Прокси
- Секция **FluxRoute (приложение)**: текущая версия, доступная версия, статус, кнопки «Проверить» и «Установить»

### Обновление самого приложения FluxRoute
- Новый сервис `IAppUpdaterService` / `AppUpdaterService` в `FluxRoute.Updater`
- Проверка новой версии **без GitHub API** — через HTTP-редирект `github.com/…/releases/latest → /releases/tag/vX.Y.Z`; никаких лимитов 60 запросов/час
- URL скачивания строится по шаблону напрямую (`/releases/download/{tag}/FluxRoute.exe`) — без дополнительных запросов
- Загружает новый `.exe` во временную директорию, проверяет SHA-256, запускает скрытый `.bat`-заменщик
- `.bat` ждёт завершения текущего PID → копирует новый файл → перезапускает приложение автоматически

### Diagnostic Bundle
- Новая кнопка **📦 Diagnostic Bundle** на вкладке «Диагностика»
- Создаёт ZIP-архив с:
  - `diagnostics.txt` — полная системная диагностика
  - `app_log.txt`, `orchestrator_log.txt`, `update_log.txt`, `service_log.txt`
  - `settings.json` — текущие настройки
  - `serilog/*.log` — последние 3 лог-файла из `%LocalAppData%\FluxRoute\logs`

### Проверка профилей (оркестратор)
- Новый сервис `ProfileProbeService` — автоматическое тестирование BAT-профилей
- `ProcessHealthChecker` — мониторинг стабильности winws.exe во время проверки
- `ProfileScoringService` — балльная система оценки профилей
- `ConnectivityChecker` — параллельная проверка HTTP/Ping целей (`targets.txt`)
- Модели: `ProfileProbeResult`, `ProfileProbeOptions`, `CheckResult`, `ProfileScore`

---

## 🏗️ Архитектурные изменения

### DI Host + интерфейсы сервисов
- `App.xaml.cs` стал composition root на базе `Microsoft.Extensions.Hosting`
- Зарегистрированы интерфейсы: `ISettingsService`, `IUpdaterService`, `IConnectivityChecker`, `IAppUpdaterService`
- Два именованных `HttpClient` через `IHttpClientFactory` с Polly resilience (`AddStandardResilienceHandler`)

### Разделение MainViewModel на feature ViewModels
- `UpdatesViewModel` — логика проверки/установки обновлений движка **и самого приложения**
- `ServiceViewModel` — Game Filter, IPSet, служба zapret
- `DiagnosticsViewModel` — диагностические проверки компонентов
- `MainViewModel` сохраняет тонкие обёртки для совместимости с XAML

### Updater — staging + rollback + SHA-256
- Полный цикл: `staging` → `backup` → `rollback` при неудачной установке
- SHA-256 хэш архива логируется при каждом скачивании

### Serilog file logging
- Структурированные логи записываются в `%LocalAppData%\FluxRoute\logs`
- Настройки уровней: `Information` / `Warning` для Microsoft и System

---

## 🐛 Исправления

- Исправлен краш оркестратора при сканировании профилей
- Исправлены поля TG Proxy (сброс значений, некорректное сохранение настроек)
- Восстановлено TLS-валидирование — убрано отключение через `ServicePointManager`
- `static HttpClient` заменён на `IHttpClientFactory` — устранены утечки соединений

---

## 🧪 Тесты

- Новый проект `FluxRoute.Core.Tests` (xUnit, .NET 10, 35 тестов — все зелёные)
  - `SettingsServiceTests` — defaults, round-trip, восстановление из бэкапа при повреждении
  - `UpdaterServiceTests` — `GetLocalVersion` из `version.txt` и `service.bat`
  - `ProfileParserTests` — `ProfileItem`, `TargetEntry`, `CheckResult`, `ProfileProbeResult`, сканирование BAT

---

## 🔧 Прочее

- Иконка лога обновлена
- Удалены временные файлы: `apply_restore_tg_secret.ps1`, `FluxRoute_latest_fixes_bundle.zip`, `.bak` XAML
- `LogsViewModel` вынесен в отдельный файл
- Добавлен `BoolToInstalledConverter` (конвертер `bool → "✅ Да" / "❌ Нет"`)
