[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
$solution = Join-Path $repoRoot 'CodexUsageWidget.sln'

if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw "The repository-local .NET SDK was not found at '$dotnet'."
}

$arguments = @(
    'test'
    $solution
    '--configuration'
    $Configuration
    '--nologo'
)

if ($NoRestore) {
    $arguments += '--no-restore'
}

& $dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
