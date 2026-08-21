# 📦 Моды во FluxRoute

FluxRoute поддерживает систему модов — внешних скриптов и конфигов, которые можно включать и выключать через интерфейс. Каждый мод — отдельная папка в `mods/` рядом с `FluxRoute.exe` и файл `manifest.json`.

### Структура мода

```text
mods/
├── example-mod/
│   ├── manifest.json
│   ├── start.bat
│   └── stop.bat
└── status.json         ← создаётся автоматически и хранит статусы
```

### `manifest.json`

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
| `version` | string | Версия в формате SemVer |
| `author` | string | Автор |
| `description` | string | Описание |
| `dependencies` | string[] | Имена папок модов, которые должны быть активны до запуска этого мода |
| `scripts.start` | string | Команда запуска, например `script.bat --verbose` |
| `scripts.stop` | string | Команда остановки |
| `config` | object | Произвольная конфигурация мода |

### Скрипты

Поддерживаются `.bat`, `.exe`, `.ps1` и `.py` (если Python установлен). Скрипт запускается через `Process.Start` в рабочей папке мода.

- **start** — вызывается при активации мода и должен вернуть exit code `0` при успехе.
- **stop** — вызывается при деактивации. Если скрипт отсутствует, мод просто помечается как неактивный.
- Скрипты могут принимать аргументы: например, `"start": "script.bat --verbose"`.

### Зависимости

Если у мода есть зависимости, они должны быть активированы до запуска этого мода. Проверка выполняется автоматически при активации.

```json
{
  "dependencies": ["core-mod", "network-mod"]
}
```

### Статусы

- 🟢 **Active** — мод запущен, скрипт `start` завершился успешно
- ⚫ **Inactive** — мод найден, но не запущен
- 🔴 **Error** — ошибка при запуске или остановке
- ⬜ **NotLoaded** — мод ещё не сканировался

Статусы сохраняются в `mods/status.json` и восстанавливаются при перезапуске.

### Пример создания мода

1. Создайте папку `mods/my-mod/`.
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

4. Откройте FluxRoute → вкладка **«Моды»** → нажмите **«Включить»**.

### Логирование и технические детали

- Действия с модами записываются в `logs/fluxroute-*.log`.
- Менеджер модов: `ModManager` (singleton, DI).
- Интерфейс: `ModsPage.xaml` и `ModsViewModel`.
- Статусы: `mods/status.json`.
- Тесты: `ModManagerTests.cs`.

### API для разработчиков

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