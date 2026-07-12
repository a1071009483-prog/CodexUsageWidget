[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $RuntimeIdentifier = 'win-x64',

    [string] $OutputDirectory = '',

    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'CodexUsageWidget.sln'
$appProject = Join-Path $repoRoot 'src\CodexUsageWidget.App\CodexUsageWidget.App.csproj'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Join-Path (Join-Path (Join-Path $repoRoot 'artifacts') 'publish') $RuntimeIdentifier) $Configuration
}

$dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    throw 'A .NET 8 SDK is required. Install it and ensure dotnet is on PATH.'
}

$arguments = @(
    'publish'
    $appProject
    '--configuration', $Configuration
    '--runtime', $RuntimeIdentifier
    '--self-contained', 'true'
    '--output', $OutputDirectory
    '--nologo'
    '-p:PublishSingleFile=true'
    '-p:EnableWindowsTargeting=true'
)

if ($NoRestore) {
    $arguments += '--no-restore'
}

& $dotnet.Source @arguments

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$noticesSource = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md'
$noticesDest = Join-Path $OutputDirectory 'THIRD-PARTY-NOTICES.md'
if (Test-Path -LiteralPath $noticesSource -PathType Leaf) {
    Copy-Item -LiteralPath $noticesSource -Destination $noticesDest -Force
}

Write-Host "Published self-contained build to: $OutputDirectory"
