# FluxRouteDev

## Быстрый запуск dev-режима (Windows)


### Что запускать

Рекомендуемый вариант (обходит ограничения PowerShell ExecutionPolicy только для текущего процесса):

```bat
run-dev.cmd
```

Из PowerShell можно так:
=======
### PowerShell: корректный запуск из текущей папки

Если вы запускаете из PowerShell, используйте префикс `./` (или `\.\` в Windows-стиле), иначе получите ошибку `CommandNotFoundException`:

```powershell
.\run-dev.ps1
```

или (рекомендуется, чтобы обойти ограничения ExecutionPolicy только для текущего процесса):

```powershell
.\run-dev.cmd
```


Или напрямую `.ps1` (если политика выполнения разрешает):

```powershell
.\run-dev.ps1
```

### Аргументы

```powershell
.\run-dev.cmd -Branch main -NoPull
```

### Что исправлено в скриптах

- `run-dev.ps1` теперь всегда делает `Set-Location $PSScriptRoot`, поэтому корректно работает даже если запуск был не из папки репозитория.
- Добавлены проверки наличия `git`, `dotnet` и файла проекта до начала сборки.
- `run-dev.cmd` теперь делает `pushd "%~dp0"` перед запуском PowerShell-скрипта и возвращается обратно через `popd`.
=======
### Передача аргументов

```powershell
.\run-dev.cmd -Branch main -NoPull
```

### Почему возникает ошибка

- `run-dev.ps1` без `./` / `.\` в PowerShell обычно не ищется в текущей директории.
- На некоторых системах запуск `.ps1` дополнительно блокируется политикой выполнения (`ExecutionPolicy`).
- `run-dev.cmd` запускает PowerShell с `-ExecutionPolicy Bypass` только для текущего процесса и не меняет глобальные настройки системы.

Если при запуске `run-dev.ps1` появляется ошибка про `ExecutionPolicy` (UnauthorizedAccess),
используйте обёртку:

```bat
run-dev.cmd
```

Дополнительно можно передать те же аргументы, что и в PowerShell-скрипт:

```bat
run-dev.cmd -Branch main -NoPull
```

`run-dev.cmd` запускает PowerShell с `-ExecutionPolicy Bypass` только для текущего процесса, не меняя глобальную политику системы.
