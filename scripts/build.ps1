[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$repoDotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
$solution = Join-Path $repoRoot 'CodexUsageWidget.sln'

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

$arguments = @(
    'test'
    $solution
    '--configuration'
    $Configuration
    '--nologo'
    '--blame-hang'
    '--blame-hang-timeout'
    '60s'
)

if ($NoRestore) {
    $arguments += '--no-restore'
}

& $dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
