[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $SourceDirectory,

    [string] $IsccPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'ReleaseChecksums.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$issPath = Join-Path $repoRoot 'installer\CodexUsageWidget.iss'
$releaseRoot = Join-Path $repoRoot 'artifacts\release'

$sourceExe = Join-Path $SourceDirectory 'CodexUsageWidget.exe'
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw "Signed application payload not found: $sourceExe. Run scripts/package.ps1 first."
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    if (-not [string]::IsNullOrWhiteSpace($env:ISCC_PATH)) {
        $IsccPath = $env:ISCC_PATH
    }
    else {
        $defaultIscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
        if (Test-Path -LiteralPath $defaultIscc -PathType Leaf) {
            $IsccPath = $defaultIscc
        }
        else {
            throw "Inno Setup 6 ISCC.exe was not found. Install Inno Setup 6 or pass -IsccPath / set ISCC_PATH."
        }
    }
}

if (-not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw "ISCC.exe not found at: $IsccPath"
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

& $IsccPath `
    "/DAppVersion=$Version" `
    "/DSourceDir=$SourceDirectory" `
    "/O$releaseRoot" `
    $issPath

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$setupPath = Join-Path $releaseRoot "CodexUsageWidget-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Installer output not found: $setupPath"
}

# The installer is a primary release artifact; refresh checksums to cover it.
Write-ReleaseChecksums -ReleaseRoot $releaseRoot

Write-Host "Built installer: $setupPath"
