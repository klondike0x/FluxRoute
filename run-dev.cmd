@echo off
setlocal

REM Wrapper to run run-dev.ps1 even when script execution is restricted.
REM Applies bypass only to this PowerShell process.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-dev.ps1" %*
set EXIT_CODE=%ERRORLEVEL%

if not "%EXIT_CODE%"=="0" (
    echo.
    echo FluxRoute Dev Runner finished with code %EXIT_CODE%.
)

exit /b %EXIT_CODE%
