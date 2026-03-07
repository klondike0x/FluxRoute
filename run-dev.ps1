# Запуск в PowerShell: .\run-dev.ps1 (или .\run-dev.cmd)
# Без префикса .\ PowerShell может не найти скрипт в текущей папке.

# run-dev.ps1
param(
    [string]$Branch = "main",           # Ветка по умолчанию
    [string]$ProjectPath = "FluxRouteDev/FluxRouteDev.csproj",
    [switch]$NoPull                     # Если true — не делать pull
)

$needStashPop = $false

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  🔷 FluxRoute — Dev Runner" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1️⃣ Проверка Git
if (-not (Test-Path ".git")) {
    Write-Host "✗ Это не Git-репозиторий!" -ForegroundColor Red
    Write-Host "  Выполните: git init" -ForegroundColor Yellow
    exit 1
}

# 2️⃣ Pull изменений (если не указан флаг -NoPull)
if (-not $NoPull) {
    Write-Host "📥 Получение изменений из ветки '$Branch'..." -ForegroundColor Cyan
    
    # Сохраняем текущие изменения (если есть)
    $uncommitted = git status --porcelain
    if ($uncommitted) {
        Write-Host "⚠ Найдены несохранённые изменения. Делаем stash..." -ForegroundColor Yellow
        git stash push -m "Auto-stash before pull"
        $needStashPop = $true
    }
    
    # Pull
    git pull origin $Branch --rebase
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Ошибка pull! Проверьте подключение к репозиторию." -ForegroundColor Red
        if ($needStashPop) { git stash pop }
        exit 1
    }
    
    # Возвращаем stash если был
    if ($needStashPop) {
        Write-Host "📦 Восстанавливаем локальные изменения..." -ForegroundColor Cyan
        git stash pop
    }
    
    Write-Host "✓ Файлы обновлены" -ForegroundColor Green
    Write-Host ""
}

# 3️⃣ Сборка проекта
Write-Host "🔨 Сборка проекта..." -ForegroundColor Cyan
dotnet build $ProjectPath --configuration Debug --verbosity quiet

if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Ошибка сборки!" -ForegroundColor Red
    exit 1
}

Write-Host "✓ Сборка успешна" -ForegroundColor Green
Write-Host ""

# 4️⃣ Запуск проекта
Write-Host "🚀 Запуск FluxRoute..." -ForegroundColor Cyan
Write-Host "  (Нажмите Ctrl+C для остановки)" -ForegroundColor Gray
Write-Host ""

dotnet run --project $ProjectPath --no-build
