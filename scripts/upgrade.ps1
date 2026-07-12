[CmdletBinding()]
param(
    [string] $SourceDirectory = '',

    [string] $InstallDirectory = '',

    [switch] $StartWithWindows = $true,

    [switch] $Launch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$appName = 'CodexUsageWidget'

if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $SourceDirectory = Join-Path (Join-Path (Join-Path (Join-Path $repoRoot 'artifacts') 'publish') 'win-x64') 'Release'
}

if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $localAppData = [Environment]::GetFolderPath('LocalApplicationData')
    $InstallDirectory = Join-Path $localAppData $appName
}

$executable = Join-Path $InstallDirectory "$appName.exe"

if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
    throw "Source directory not found: $SourceDirectory. Run scripts/package.ps1 first."
}

if (-not (Test-Path -LiteralPath (Join-Path $SourceDirectory "$appName.exe") -PathType Leaf)) {
    throw "CodexUsageWidget.exe was not found in $SourceDirectory."
}

# Stop any running instance before touching the installation files.
$process = Get-Process -Name $appName -ErrorAction SilentlyContinue
if ($process) {
    Write-Host "Stopping running $appName instance..."
    $process | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# Back up the current installation so rollback is possible.
$backupDirectory = ''
if (Test-Path -LiteralPath $InstallDirectory -PathType Container) {
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backupParent = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "$appName-backups"
    if (-not (Test-Path -LiteralPath $backupParent)) {
        New-Item -ItemType Directory -Path $backupParent -Force | Out-Null
    }

    $backupDirectory = Join-Path $backupParent "$timestamp"
    Write-Host "Creating backup of current installation at $backupDirectory ..."
    Copy-Item -LiteralPath $InstallDirectory -Destination $backupDirectory -Recurse -Force
}

# Install the new build.
if (-not (Test-Path -LiteralPath $InstallDirectory)) {
    New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
}

Write-Host "Upgrading $appName at $InstallDirectory ..."
Get-ChildItem -LiteralPath $SourceDirectory | Copy-Item -Destination $InstallDirectory -Recurse -Force

if ($StartWithWindows) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Set-ItemProperty -LiteralPath $runKey -Name $appName -Value "`"$executable`"" -Type String -Force
    Write-Host "Registered $appName to start with the current user's Windows session."
}

# Clean up older backups, keeping the most recent three.
$backupParent = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "$appName-backups"
if (Test-Path -LiteralPath $backupParent) {
    Get-ChildItem -LiteralPath $backupParent -Directory |
        Sort-Object CreationTime -Descending |
        Select-Object -Skip 3 |
        Remove-Item -Recurse -Force
}

Write-Host "Upgrade complete. Backup: $backupDirectory"

if ($Launch) {
    Write-Host "Starting $appName ..."
    Start-Process -FilePath $executable
}
