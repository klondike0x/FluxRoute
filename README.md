# FluxRouteDev

## Быстрый запуск dev-режима (Windows)

### Что запускать

Рекомендуемый вариант (обходит ограничения PowerShell ExecutionPolicy только для текущего процесса):

```bat
run-dev.cmd
```

Из PowerShell можно так:

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
