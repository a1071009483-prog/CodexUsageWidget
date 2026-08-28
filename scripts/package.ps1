[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $RuntimeIdentifier = 'win-x64',

    [string] $OutputDirectory = '',

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [switch] $Clean,

    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'ReleaseChecksums.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'CodexUsageWidget.sln'
$appProject = Join-Path $repoRoot 'src\CodexUsageWidget.App\CodexUsageWidget.App.csproj'

if ($Clean) {
    $publishCleanRoot = Join-Path $repoRoot 'artifacts\publish'
    $releaseCleanRoot = Join-Path $repoRoot 'artifacts\release'
    Remove-Item -LiteralPath $publishCleanRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $releaseCleanRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Join-Path (Join-Path (Join-Path $repoRoot 'artifacts') 'publish') $RuntimeIdentifier) $Configuration
}

$dotnet = $null
$repoDotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
if (Test-Path -LiteralPath $repoDotnet -PathType Leaf) {
    $dotnet = $repoDotnet
}
else {
    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw "A .NET 8 SDK is required. Install it or place the repository-local SDK at '$repoDotnet'."
    }

    $dotnet = $dotnetCommand.Source
}

# FileVersion requires four numeric components and cannot contain prerelease text.
$fileVersion = ($Version -split '-')[0] + '.0'

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
    '-p:DebugType=none'
    '-p:DebugSymbols=false'
    "-p:Version=$Version"
    "-p:InformationalVersion=$Version"
    "-p:FileVersion=$fileVersion"
)

if ($NoRestore) {
    $arguments += '--no-restore'
}

& $dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$noticesSource = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md'
$noticesDest = Join-Path $OutputDirectory 'THIRD-PARTY-NOTICES.md'
if (Test-Path -LiteralPath $noticesSource -PathType Leaf) {
    Copy-Item -LiteralPath $noticesSource -Destination $noticesDest -Force
}

Write-Host "Published self-contained build to: $OutputDirectory"

$releaseRoot = Join-Path $repoRoot 'artifacts\release'
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

$zipPath = Join-Path $releaseRoot "CodexUsageWidget-$Version-$RuntimeIdentifier.zip"
Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force
Write-Host "Created portable archive: $zipPath"

Write-ReleaseChecksums -ReleaseRoot $releaseRoot
