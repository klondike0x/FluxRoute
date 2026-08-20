# 📦 Mods in FluxRoute

FluxRoute supports user mods — external scripts and configuration files that can be enabled or disabled from the **Mods** tab.

## Mod structure

Each mod is a separate folder in `mods/` next to `FluxRoute.exe` and contains a `manifest.json` file.

```text
mods/
├── example-mod/
│   ├── manifest.json
│   ├── start.bat
│   └── stop.bat
└── status.json         ← created automatically and stores statuses
```

## `manifest.json`

```json
{
  "name": "Example Mod",
  "version": "1.0.0",
  "author": "klondike0x",
  "description": "Mod description",
  "dependencies": [],
  "scripts": {
    "start": "start.bat",
    "stop": "stop.bat"
  },
  "config": {}
}
```

| Field | Type | Description |
|---|---|---|
| `name` | string | Mod name |
| `version` | string | Version in SemVer format |
| `author` | string | Author |
| `description` | string | Description |
| `dependencies` | string[] | Folder names of mods that must be active before this mod starts |
| `scripts.start` | string | Start command, for example `script.bat --verbose` |
| `scripts.stop` | string | Stop command |
| `config` | object | Arbitrary mod configuration |

## Scripts

Supported script types are `.bat`, `.exe`, `.ps1`, and `.py` if Python is installed. Scripts run through `Process.Start` with the mod folder as the working directory.

- **start** runs when the mod is activated and should return exit code `0` on success.
- **stop** runs when the mod is deactivated. If it is missing, the mod is simply marked as inactive.
- Scripts may receive arguments, for example: `"start": "script.bat --verbose"`.

## Dependencies

Dependencies must be active before the mod starts. They are checked automatically during activation.

```json
{
  "dependencies": ["core-mod", "network-mod"]
}
```

## Statuses

- 🟢 **Active** — the mod is running and `start` completed successfully
- ⚫ **Inactive** — the mod was found but is not running
- 🔴 **Error** — an error occurred while starting or stopping
- ⬜ **NotLoaded** — the mod has not been scanned yet

Statuses are stored in `mods/status.json` and restored after restart.

## Example

1. Create `mods/my-mod/`.
2. Add `manifest.json` with the required metadata and scripts.
3. Add `start.bat`:

```batch
@echo off
echo Hello from My Mod!
exit /b 0
```

4. Open FluxRoute → the **Mods** tab → click **Enable**.

## Logging and API

Mod actions are logged to `%LOCALAPPDATA%\FluxRoute\logs\`. The manager is implemented by `ModManager`; the UI is provided by `ModsPage.xaml` and `ModsViewModel`.

```csharp
var mods = await modManager.ScanModsAsync();
bool activated = await modManager.ActivateModAsync("my-mod");
bool deactivated = await modManager.DeactivateModAsync("my-mod");
ModStatus status = modManager.GetModStatus("my-mod");
```