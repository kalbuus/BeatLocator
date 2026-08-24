[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$NoRestore
)

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$buildVersionScript = Join-Path $PSScriptRoot 'build-version.ps1'
$versions = @('1.40.2', '1.40.3', '1.40.4', '1.40.5', '1.40.6', '1.40.7', '1.40.8')
$manifestVersion = $versions[0]

foreach ($version in $versions) {
    Write-Host "Validating Beat Saber $version..."

    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $buildVersionScript,
        '-GameVersion', $version,
        '-Configuration', $Configuration,
        '-DisableZipRelease'
    )
    if ($NoRestore) {
        $arguments += '-NoRestore'
    }

    & powershell.exe @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Compatibility validation failed for Beat Saber $version."
    }
}

# The 1.40.2 build is the distributable baseline. Its embedded manifest is
# deliberately retained, while the same source has been validated against every
# version through 1.40.8 above.
$sourceDirectory = Join-Path $repositoryRoot "BeatLocator\bin\$Configuration\$manifestVersion\net472"
$sourceArtifactDirectory = Join-Path $sourceDirectory 'Artifact'
$destinationDirectory = Join-Path $repositoryRoot "BeatLocator\bin\$Configuration\1.40.2-1.40.8"
$destinationArtifactDirectory = Join-Path $destinationDirectory 'Artifact'

if (-not (Test-Path -LiteralPath $sourceArtifactDirectory)) {
    throw "Expected build artifact was not found: $sourceArtifactDirectory"
}

$resolvedRepositoryPrefix = [IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\') + '\'
$resolvedDestinationArtifactDirectory = [IO.Path]::GetFullPath($destinationArtifactDirectory)
if (-not $resolvedDestinationArtifactDirectory.StartsWith(
        $resolvedRepositoryPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean an artifact directory outside the repository: " +
          $resolvedDestinationArtifactDirectory
}
if (Test-Path -LiteralPath $resolvedDestinationArtifactDirectory) {
    Remove-Item -LiteralPath $resolvedDestinationArtifactDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $destinationArtifactDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $sourceArtifactDirectory '*') -Destination $destinationArtifactDirectory -Recurse -Force
Copy-Item -LiteralPath (Join-Path $sourceDirectory 'BeatLocator.manifest') -Destination $destinationDirectory -Force

$binaryPath = Join-Path $destinationArtifactDirectory 'Plugins\BeatLocator.dll'
$dllFiles = @(Get-ChildItem -LiteralPath $destinationArtifactDirectory -Filter '*.dll' -File -Recurse)
$expectedBinaryPath = [IO.Path]::GetFullPath($binaryPath)
if ($dllFiles.Count -ne 1 -or
    [IO.Path]::GetFullPath($dllFiles[0].FullName) -ne $expectedBinaryPath) {
    $dllSummary = ($dllFiles | ForEach-Object FullName) -join ', '
    throw "Release artifact must contain only Plugins\BeatLocator.dll; found: $dllSummary"
}

$zipPath = Join-Path $destinationDirectory 'BeatLocator-1.40.2-1.40.8.zip'
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $destinationArtifactDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $binaryPath
Write-Host "Unified artifact: $binaryPath"
Write-Host "Unified ZIP: $zipPath"
Write-Host "Embedded manifest gameVersion: $manifestVersion"
Write-Host "SHA-256: $($hash.Hash)"
