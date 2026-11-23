# Installs (-Mode install) or removes (-Mode restore) the Russian A HUB translation and restarts A HUB.
#   Apply.ps1 [-Mode install|restore] [-AhubDir "C:\path\to\A HUB"]
# Without -AhubDir the folder of a running A HUB is used, so start A HUB first.
param([ValidateSet('install', 'restore')][string]$Mode = 'install', [string]$AhubDir)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cfg = Join-Path $env:LOCALAPPDATA 'A HUB\configs\config.json'

if (-not $AhubDir) {
    $running = Get-Process -Name 'A HUB' -ErrorAction SilentlyContinue | Where-Object { $_.Path } | Select-Object -First 1
    if (-not $running) { throw 'Запустите A HUB (или укажите -AhubDir "папка A HUB") и повторите.' }
    $AhubDir = Split-Path $running.Path -Parent
}
$target = Join-Path $AhubDir 'translations\ru_RU.qm'
if (-not (Test-Path (Split-Path $target -Parent))) { throw "Не найдена папка translations в $AhubDir" }

Get-Process -Name 'A HUB' -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); $_.WaitForExit() }
$backup = Join-Path $root 'backup\ru_RU.qm.orig'
if ($Mode -eq 'install') {
    # Keep the file shipped with A HUB so that "restore" can put it back.
    if (-not (Test-Path $backup)) { New-Item -ItemType Directory -Force (Split-Path $backup) | Out-Null; Copy-Item $target $backup }
    Copy-Item (Join-Path $root 'dist\ru_RU.qm') $target -Force
} elseif (Test-Path $backup) {
    Copy-Item $backup $target -Force
}
$language = if ($Mode -eq 'install') { 'ru_RU' } else { 'en_US' }
if (Test-Path $cfg) {
    $text = [IO.File]::ReadAllText($cfg) -replace '"Language":\s*"[^"]*"', ('"Language": "' + $language + '"')
    [IO.File]::WriteAllText($cfg, $text, (New-Object Text.UTF8Encoding($false)))
}
Start-Process -FilePath (Join-Path $AhubDir 'A HUB.exe') -WorkingDirectory $AhubDir
if ($Mode -eq 'install') { 'Перевод установлен, A HUB перезапущен.' } else { 'A HUB возвращён на английский.' }
