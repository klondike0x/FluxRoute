# 📦 Моды во FluxRoute

FluxRoute поддерживает систему модов — внешних скриптов и конфигов, которые можно включать и выключать через интерфейс.

## Структура мода

Каждый мод — это отдельная папка в `mods/` (рядом с `.exe`), содержащая `manifest.json`.

```
mods/
├── example-mod/
│   ├── manifest.json
│   ├── start.bat
│   └── stop.bat
└── status.json         ← автосоздаётся, хранит статусы
```

## manifest.json

```json
{
  "name": "Example Mod",
  "version": "1.0.0",
  "author": "klondike0x",
  "description": "Описание мода",
  "dependencies": [],
  "scripts": {
    "start": "start.bat",
    "stop": "stop.bat"
  },
  "config": {}
}
```

| Поле | Тип | Описание |
|---|---|---|
| `name` | string | Название мода |
| `version` | string | Версия (SemVer) |
| `author` | string | Автор |
| `description` | string | Описание |
| `dependencies` | string[] | Имена папок других модов, которые должны быть активны |
| `scripts.start` | string | Команда для запуска (может быть с аргументами: `script.bat --arg`) |
| `scripts.stop` | string | Команда для остановки |
| `config` | object | Произвольная конфигурация мода |

## Скрипты

Поддерживаются: `.bat`, `.exe`, `.ps1`, `.py` (если Python установлен).

Скрипт запускается через `Process.Start` в рабочей папке мода.

- **start** — вызывается при активации мода. Должен вернуть exit code 0 при успехе.
- **stop** — вызывается при деактивации. Если скрипт stop отсутствует — мод просто помечается как неактивный.

Скрипты могут принимать аргументы. Например: `"start": "script.bat --verbose"`.

## Зависимости

Если у мода есть зависимости, они должны быть **активированы до** запуска этого мода. Проверка происходит автоматически при активации.

```json
{
  "dependencies": ["core-mod", "network-mod"]
}
```

## Статусы

- 🟢 **Active** — мод запущен (скрипт start выполнен успешно)
- ⚫ **Inactive** — мод найден, но не запущен
- 🔴 **Error** — ошибка при запуске или остановке
- ⬜ **NotLoaded** — мод ещё не сканировался

Статусы сохраняются в `mods/status.json` и восстанавливаются при перезапуске.

## Пример создания мода

1. Создайте папку `mods/my-mod/`
2. Создайте `manifest.json`:

```json
{
  "name": "My Mod",
  "version": "1.0.0",
  "author": "YourName",
  "description": "Мой первый мод",
  "dependencies": [],
  "scripts": {
    "start": "start.bat",
    "stop": "start.bat --stop"
  },
  "config": {}
}
```

3. Создайте `start.bat`:

```batch
@echo off
echo Hello from My Mod!
exit /b 0
```

4. Откройте FluxRoute → вкладка «Моды» → нажмите «Включить»

## Логирование

Действия с модами записываются в лог-файл приложения (`logs/fluxroute-*.log`).

## Технические детали

- Менеджер модов: `ModManager` (singleton, DI)
- Статусы: `mods/status.json`
- Интерфейс: `ModsPage.xaml` + `ModsViewModel`
- Тесты: `ModManagerTests.cs`

### API (для разработчиков)

```csharp
// Сканирование модов
var mods = await modManager.ScanModsAsync();

// Активация
bool ok = await modManager.ActivateModAsync("my-mod");

// Деактивация
bool ok = await modManager.DeactivateModAsync("my-mod");

// Проверка зависимостей
bool depsOk = await modManager.CheckDependenciesAsync("my-mod");

// Статус
ModStatus status = modManager.GetModStatus("my-mod");
```
