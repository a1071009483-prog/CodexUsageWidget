[CmdletBinding()]
param(
    [string] $InstallDirectory = '',

    [string] $BackupDirectory = '',

    [switch] $StartWithWindows = $true,

    [switch] $Launch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$appName = 'CodexUsageWidget'

if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $localAppData = [Environment]::GetFolderPath('LocalApplicationData')
    $InstallDirectory = Join-Path $localAppData $appName
}

$executable = Join-Path $InstallDirectory "$appName.exe"
$backupParent = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "$appName-backups"

if ([string]::IsNullOrWhiteSpace($BackupDirectory)) {
    if (-not (Test-Path -LiteralPath $backupParent -PathType Container)) {
        throw "No backups found at $backupParent. Specify -BackupDirectory explicitly."
    }

    $BackupDirectory = Get-ChildItem -LiteralPath $backupParent -Directory |
        Sort-Object CreationTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName

    if ([string]::IsNullOrWhiteSpace($BackupDirectory)) {
        throw "No backups found at $backupParent."
    }
}

if (-not (Test-Path -LiteralPath $BackupDirectory -PathType Container)) {
    throw "Backup directory not found: $BackupDirectory"
}

if (-not (Test-Path -LiteralPath (Join-Path $BackupDirectory "$appName.exe") -PathType Leaf)) {
    throw "Backup at $BackupDirectory does not contain CodexUsageWidget.exe."
}

# Stop any running instance before replacing files.
$process = Get-Process -Name $appName -ErrorAction SilentlyContinue
if ($process) {
    Write-Host "Stopping running $appName instance..."
    $process | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# Remove the current installation and restore the backup.
if (Test-Path -LiteralPath $InstallDirectory -PathType Container) {
    Write-Host "Removing current installation at $InstallDirectory ..."
    Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
}

Write-Host "Restoring backup from $BackupDirectory to $InstallDirectory ..."
Copy-Item -LiteralPath $BackupDirectory -Destination $InstallDirectory -Recurse -Force

if ($StartWithWindows) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Set-ItemProperty -LiteralPath $runKey -Name $appName -Value "`"$executable`"" -Type String -Force
    Write-Host "Registered $appName to start with the current user's Windows session."
}

Write-Host "Rollback complete. Restored from: $BackupDirectory"

if ($Launch) {
    Write-Host "Starting $appName ..."
    Start-Process -FilePath $executable
}
