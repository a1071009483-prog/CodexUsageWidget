[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [string] $RuntimeIdentifier = 'win-x64',

    [switch] $RequireInstaller,

    [switch] $RequireSignatures
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $repoRoot 'artifacts\release'
$zipPath = Join-Path $releaseRoot "CodexUsageWidget-$Version-$RuntimeIdentifier.zip"
$setupPath = Join-Path $releaseRoot "CodexUsageWidget-Setup-$Version.exe"
$checksumPath = Join-Path $releaseRoot 'SHA256SUMS.txt'

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)]
        [bool] $Condition,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    if (-not $Condition) {
        throw "Release verification failed: $Message"
    }
}

Assert-Condition (Test-Path -LiteralPath $zipPath -PathType Leaf) "portable ZIP not found: $zipPath"
Assert-Condition (Test-Path -LiteralPath $checksumPath -PathType Leaf) "checksum file not found: $checksumPath"

if ($RequireInstaller) {
    Assert-Condition (Test-Path -LiteralPath $setupPath -PathType Leaf) "installer not found: $setupPath"
}

$extractRoot = Join-Path ([IO.Path]::GetTempPath()) "codex-widget-verify-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null

try {
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force

    $exePath = Join-Path $extractRoot 'CodexUsageWidget.exe'
    $noticesPath = Join-Path $extractRoot 'THIRD-PARTY-NOTICES.md'

    Assert-Condition (Test-Path -LiteralPath $exePath -PathType Leaf) "CodexUsageWidget.exe missing from portable ZIP"
    Assert-Condition (Test-Path -LiteralPath $noticesPath -PathType Leaf) "THIRD-PARTY-NOTICES.md missing from portable ZIP"

    # The extracted application must report the exact release version headlessly.
    $stdoutPath = Join-Path $extractRoot 'version.stdout.txt'
    $stderrPath = Join-Path $extractRoot 'version.stderr.txt'
    $versionProcess = Start-Process -FilePath $exePath `
        -ArgumentList '--version' `
        -Wait -PassThru -NoNewWindow `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath
    Assert-Condition ($versionProcess.ExitCode -eq 0) "--version exited with code $($versionProcess.ExitCode)"
    $reportedVersion = (Get-Content -LiteralPath $stdoutPath -Raw).Trim()
    Assert-Condition ($reportedVersion -eq $Version) "--version reported '$reportedVersion', expected '$Version'"

    # No source, test, intermediate, or debug artifacts may ship in the archive.
    $payloadFiles = @(Get-ChildItem -LiteralPath $extractRoot -Recurse -File |
        Where-Object { $_.Name -notin @('version.stdout.txt', 'version.stderr.txt') })
    $forbidden = @($payloadFiles | Where-Object {
        $normalized = $_.FullName.Replace('/', '\')
        $_.Extension -in @('.cs', '.csproj', '.sln', '.pdb') -or
        $normalized -match '\\obj\\' -or
        $normalized -match '\\tests\\'
    })
    Assert-Condition ($forbidden.Count -eq 0) (
        "forbidden files in archive: " + (($forbidden | Select-Object -First 5 -ExpandProperty Name) -join ', '))

    # Every checksum line must match the produced artifact.
    $checksumLines = @(Get-Content -LiteralPath $checksumPath |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-Condition ($checksumLines.Count -ge 1) "SHA256SUMS.txt contains no entries"

    foreach ($line in $checksumLines) {
        $parts = $line -split '\s+', 2
        Assert-Condition ($parts.Count -eq 2) "malformed checksum line: $line"
        $expectedHash = $parts[0].ToLowerInvariant()
        $fileName = $parts[1].TrimStart('*', ' ')
        $artifactPath = Join-Path $releaseRoot $fileName
        Assert-Condition (Test-Path -LiteralPath $artifactPath -PathType Leaf) "checksum entry missing artifact: $fileName"
        $actualHash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-Condition ($actualHash -eq $expectedHash) "checksum mismatch for $fileName"
    }

    if ($RequireInstaller) {
        $installerName = Split-Path -Leaf $setupPath
        $installerEntries = @($checksumLines | Where-Object { $_ -match [regex]::Escape($installerName) })
        Assert-Condition ($installerEntries.Count -ge 1) "checksum file does not cover the installer"
    }

    if ($RequireSignatures) {
        $targets = @($exePath)
        if ($RequireInstaller) {
            $targets += $setupPath
        }

        foreach ($target in $targets) {
            $signature = Get-AuthenticodeSignature -FilePath $target
            Assert-Condition ($signature.Status -eq 'Valid') (
                "Authenticode signature is '$($signature.Status)' for $(Split-Path -Leaf $target)")
        }
    }
}
finally {
    Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Release verification passed for version $Version ($RuntimeIdentifier)."
