# FluxRouteDev

## Быстрый запуск dev-режима (Windows)

Если при запуске `run-dev.ps1` появляется ошибка про `ExecutionPolicy` (UnauthorizedAccess),
используйте обёртку:

```bat
run-dev.cmd
```

Дополнительно можно передать те же аргументы, что и в PowerShell-скрипт:

```bat
run-dev.cmd -Branch main -NoPull
```

`run-dev.cmd` запускает PowerShell с `-ExecutionPolicy Bypass` только для текущего процесса,
не меняя глобальную политику системы.
