# Shared release checksum generation for package.ps1 and build-installer.ps1.
# Writes SHA256SUMS.txt covering the primary release artifacts present in the
# release directory (portable ZIP and, once built, the Setup executable).

function Write-ReleaseChecksums {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $ReleaseRoot
    )

    Set-StrictMode -Version Latest
    $ErrorActionPreference = 'Stop'

    $artifacts = @(Get-ChildItem -LiteralPath $ReleaseRoot -File |
        Where-Object {
            $_.Name -like 'CodexUsageWidget-*.zip' -or
            $_.Name -like 'CodexUsageWidget-Setup-*.exe'
        } |
        Sort-Object -Property Name)

    if ($artifacts.Count -eq 0) {
        throw "No release artifacts found in '$ReleaseRoot'."
    }

    $lines = foreach ($artifact in $artifacts) {
        $hash = (Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($artifact.Name)"
    }

    $checksumPath = Join-Path $ReleaseRoot 'SHA256SUMS.txt'
    Set-Content -LiteralPath $checksumPath -Value $lines -Encoding ascii
    Write-Host "Wrote checksums to: $checksumPath"
}
