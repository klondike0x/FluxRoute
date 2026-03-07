# FluxRouteDev

## Быстрый запуск dev-режима (Windows)

### PowerShell: корректный запуск из текущей папки

Если вы запускаете из PowerShell, используйте префикс `./` (или `\.\` в Windows-стиле), иначе получите ошибку `CommandNotFoundException`:

```powershell
.\run-dev.ps1
```

или (рекомендуется, чтобы обойти ограничения ExecutionPolicy только для текущего процесса):

```powershell
.\run-dev.cmd
```

### Передача аргументов

```powershell
.\run-dev.cmd -Branch main -NoPull
```

### Почему возникает ошибка

- `run-dev.ps1` без `./` / `.\` в PowerShell обычно не ищется в текущей директории.
- На некоторых системах запуск `.ps1` дополнительно блокируется политикой выполнения (`ExecutionPolicy`).
- `run-dev.cmd` запускает PowerShell с `-ExecutionPolicy Bypass` только для текущего процесса и не меняет глобальные настройки системы.
