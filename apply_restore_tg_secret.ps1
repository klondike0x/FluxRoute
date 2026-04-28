$ErrorActionPreference = "Stop"

function Read-TextFile([string]$Path) {
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Write-TextFile([string]$Path, [string]$Text) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $utf8NoBom)
}

function Count-GridOpen([string]$Line) {
    return ([regex]::Matches($Line, '<Grid(?=[\s>/])')).Count
}

function Count-GridClose([string]$Line) {
    return ([regex]::Matches($Line, '</Grid>')).Count
}

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not (Test-Path (Join-Path $Root "FluxRoute.slnx"))) {
    throw "Run this script from the FluxRoute repository root. FluxRoute.slnx must be next to this script."
}

$xamlPath = Join-Path $Root "FluxRoute\Views\MainWindow.xaml"
if (-not (Test-Path $xamlPath)) {
    throw "File not found: FluxRoute\Views\MainWindow.xaml"
}

$text = Read-TextFile $xamlPath
$original = $text

# Backup once before editing.
$backup = "$xamlPath.bak_before_restore_secret"
if (-not (Test-Path $backup)) {
    Copy-Item $xamlPath $backup
}

# Remove only the TgProxyDomain / SNI input block, without touching TgProxySecret.
if ($text -match 'TgProxyDomain') {
    $lines = $text -split "`r?`n", -1
    for ($i = $lines.Count - 1; $i -ge 0; $i--) {
        if ($lines[$i] -match 'TgProxyDomain') {
            $start = $i
            $textBoxStart = $i
            for ($j = $i; $j -ge [Math]::Max(0, $i - 30); $j--) {
                if ($lines[$j] -match '<TextBox\b') {
                    $textBoxStart = $j
                    break
                }
            }
            $start = $textBoxStart
            for ($j = $textBoxStart; $j -ge [Math]::Max(0, $textBoxStart - 30); $j--) {
                if (($lines[$j] -match '<TextBlock\b') -and ($lines[$j] -match 'Fake-TLS|SNI') -and ($lines[$j] -notmatch 'Secret')) {
                    $start = $j
                    break
                }
            }

            $end = $i
            for ($k = $i; $k -lt [Math]::Min($lines.Count, $i + 30); $k++) {
                if (($lines[$k] -match '</TextBox>') -or ($lines[$k] -match '/>')) {
                    $end = $k
                    break
                }
            }

            if ($end -ge $start) {
                $before = @()
                if ($start -gt 0) { $before = $lines[0..($start - 1)] }
                $after = @()
                if ($end + 1 -lt $lines.Count) { $after = $lines[($end + 1)..($lines.Count - 1)] }
                $lines = @($before + $after)
            }
        }
    }
    $text = [string]::Join("`r`n", $lines)
}

# Restore Secret UI if it was removed by an earlier script.
if ($text -notmatch 'TgProxySecret') {
    $lines = $text -split "`r?`n", -1
    $portLine = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match 'TgProxyPort') {
            $portLine = $i
            break
        }
    }

    if ($portLine -lt 0) {
        throw "Could not find TgProxyPort in MainWindow.xaml. Send me FluxRoute\Views\MainWindow.xaml and I will make an exact file."
    }

    # Prefer inserting after the Grid that contains the host/port row.
    $gridStart = -1
    for ($j = $portLine; $j -ge [Math]::Max(0, $portLine - 80); $j--) {
        if (($lines[$j] -match '<Grid(?=[\s>/])') -and ($lines[$j] -notmatch '<Grid\.')) {
            $gridStart = $j
            break
        }
    }

    $insertAfter = $portLine
    if ($gridStart -ge 0) {
        $depth = 0
        for ($k = $gridStart; $k -lt $lines.Count; $k++) {
            $depth += Count-GridOpen $lines[$k]
            $depth -= Count-GridClose $lines[$k]
            if (($k -gt $gridStart) -and ($depth -le 0)) {
                $insertAfter = $k
                break
            }
        }
    } else {
        for ($k = $portLine; $k -lt [Math]::Min($lines.Count, $portLine + 40); $k++) {
            if (($lines[$k] -match '</TextBox>') -or ($lines[$k] -match '/>')) {
                $insertAfter = $k
                break
            }
        }
    }

    $indent = ([regex]::Match($lines[$insertAfter], '^\s*')).Value
    $block = @(
        "$indent<TextBlock Text=`"Secret`" Margin=`"0,12,0,6`" />",
        "$indent<Grid>",
        "$indent    <Grid.ColumnDefinitions>",
        "$indent        <ColumnDefinition Width=`"*`" />",
        "$indent        <ColumnDefinition Width=`"8`" />",
        "$indent        <ColumnDefinition Width=`"36`" />",
        "$indent    </Grid.ColumnDefinitions>",
        "$indent    <TextBox Grid.Column=`"0`"",
        "$indent             Height=`"28`"",
        "$indent             VerticalContentAlignment=`"Center`"",
        "$indent             Text=`"{Binding TgProxySecret, UpdateSourceTrigger=PropertyChanged}`" />",
        "$indent    <Button Grid.Column=`"2`"",
        "$indent            Width=`"36`"",
        "$indent            Height=`"28`"",
        "$indent            Command=`"{Binding GenerateTgProxySecretCommand}`"",
        "$indent            ToolTip=`"Generate Secret`">",
        "$indent        <TextBlock Text=`"&#xE72C;`" FontFamily=`"Segoe MDL2 Assets`" />",
        "$indent    </Button>",
        "$indent</Grid>",
        "$indent<TextBlock Text=`"Secret is generated automatically in dd + 32 hex format.`" FontSize=`"11`" Opacity=`"0.65`" Margin=`"0,6,0,0`" />"
    )

    $before = @()
    if ($insertAfter -ge 0) { $before = $lines[0..$insertAfter] }
    $after = @()
    if ($insertAfter + 1 -lt $lines.Count) { $after = $lines[($insertAfter + 1)..($lines.Count - 1)] }
    $lines = @($before + $block + $after)
    $text = [string]::Join("`r`n", $lines)
}

# Remove helper text mentioning Fake-TLS/ee-secret if it survived, but do not remove the Secret input.
$text = [regex]::Replace($text, '(?im)^\s*<TextBlock\b(?=[^>]*(Fake-TLS|ee-secret))(?!(?=[^>]*Text="Secret"))[\s\S]*?(?:/>|</TextBlock>)\s*', "")

if ($text -ne $original) {
    Write-TextFile $xamlPath $text
    Write-Host "OK: Secret field restored and SNI field removed in MainWindow.xaml"
} else {
    Write-Host "INFO: MainWindow.xaml already looks correct. Nothing changed."
}

$check = Read-TextFile $xamlPath
if ($check -notmatch 'TgProxySecret') {
    throw "Secret field was not restored. Send me FluxRoute\Views\MainWindow.xaml and I will make an exact ready replacement."
}
if ($check -match 'TgProxyDomain') {
    Write-Warning "TgProxyDomain is still present in MainWindow.xaml. Send me the file if you want a fully exact cleanup."
}
