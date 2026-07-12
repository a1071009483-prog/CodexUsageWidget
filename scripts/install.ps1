[CmdletBinding()]
param(
    [string] $SourceDirectory = '',

    [string] $InstallDirectory = '',

    [switch] $StartWithWindows = $true,

    [switch] $CreateStartMenuShortcut
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$appName = 'CodexUsageWidget'

if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $SourceDirectory = Join-Path $repoRoot 'artifacts' 'publish' 'win-x64' 'Release'
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

if (-not (Test-Path -LiteralPath $InstallDirectory)) {
    New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
}

Write-Host "Installing $appName to $InstallDirectory ..."
Get-ChildItem -LiteralPath $SourceDirectory | Copy-Item -Destination $InstallDirectory -Recurse -Force

if ($StartWithWindows) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Set-ItemProperty -LiteralPath $runKey -Name $appName -Value "`"$executable`"" -Type String -Force
    Write-Host "Registered $appName to start with the current user's Windows session."
}

if ($CreateStartMenuShortcut) {
    $startMenu = [Environment]::GetFolderPath('StartMenu')
    $programsFolder = Join-Path $startMenu 'Programs'
    if (-not (Test-Path -LiteralPath $programsFolder)) {
        New-Item -ItemType Directory -Path $programsFolder -Force | Out-Null
    }

    $shortcutPath = Join-Path $programsFolder "$appName.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = $InstallDirectory
    $shortcut.Save()
    Write-Host "Created start-menu shortcut: $shortcutPath"
}

Write-Host "Installation complete. Run `"$executable`" to start the widget."
