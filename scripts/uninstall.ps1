[CmdletBinding()]
param(
    [string] $InstallDirectory = '',

    [switch] $RemoveLocalData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$appName = 'CodexUsageWidget'

if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $localAppData = [Environment]::GetFolderPath('LocalApplicationData')
    $InstallDirectory = Join-Path $localAppData $appName
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -LiteralPath $runKey -Name $appName -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -LiteralPath $runKey -Name $appName -Force
    Write-Host "Removed $appName from the current user's startup entries."
}

$startMenu = [Environment]::GetFolderPath('StartMenu')
$shortcutPath = Join-Path $startMenu 'Programs' "$appName.lnk"
if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
    Remove-Item -LiteralPath $shortcutPath -Force
    Write-Host "Removed start-menu shortcut."
}

if (Test-Path -LiteralPath $InstallDirectory -PathType Container) {
    Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
    Write-Host "Removed installation directory: $InstallDirectory"
}

if ($RemoveLocalData) {
    $localAppData = [Environment]::GetFolderPath('LocalApplicationData')
    $dataDirectory = Join-Path $localAppData $appName
    if (Test-Path -LiteralPath $dataDirectory -PathType Container) {
        Remove-Item -LiteralPath $dataDirectory -Recurse -Force
        Write-Host "Removed local data directory: $dataDirectory"
    }
}

Write-Host "Uninstall complete."
