param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir
)

$ErrorActionPreference = 'Stop'

$versionUrl = 'https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt'
$zipTemplate = 'https://github.com/Flowseal/zapret-discord-youtube/releases/download/{0}/zapret-discord-youtube-{0}.zip'
$publishPath = (Resolve-Path -LiteralPath $PublishDir).Path
$enginePath = Join-Path $publishPath 'engine'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('fluxroute-flowseal-' + [guid]::NewGuid().ToString('N'))
$zipPath = Join-Path $tempRoot 'flowseal.zip'
$extractPath = Join-Path $tempRoot 'extract'

try {
    New-Item -ItemType Directory -Path $tempRoot, $extractPath -Force | Out-Null

    $version = (Invoke-RestMethod -Uri $versionUrl -TimeoutSec 60).ToString().Trim()
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw 'Flowseal version.txt is empty.'
    }

    $zipUrl = $zipTemplate -f $version
    Write-Host "Downloading official Flowseal build $version..."
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extractPath -Force

    $serviceBat = Get-ChildItem -LiteralPath $extractPath -Filter 'service.bat' -File -Recurse | Select-Object -First 1
    if ($null -eq $serviceBat) {
        throw 'The downloaded Flowseal archive does not contain service.bat.'
    }

    $sourceEngine = $serviceBat.Directory.FullName
    if (Test-Path -LiteralPath $enginePath) {
        Remove-Item -LiteralPath $enginePath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $enginePath -Force | Out-Null
    Copy-Item -Path (Join-Path $sourceEngine '*') -Destination $enginePath -Recurse -Force
    Set-Content -LiteralPath (Join-Path $enginePath 'version.txt') -Value $version -Encoding utf8

    Write-Host "Bundled Flowseal $version into $enginePath"
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}