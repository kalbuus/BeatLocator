[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$GameVersion,

    [ValidateNotNullOrEmpty()]
    [string]$BeatSaberDir,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$NoRestore,

    [switch]$Install,

    [switch]$DisableZipRelease
)

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot 'BeatLocator.sln'
$resolvedBeatSaberDir = $BeatSaberDir
$usingStrippedReferences = [string]::IsNullOrWhiteSpace($resolvedBeatSaberDir)
if ([string]::IsNullOrWhiteSpace($resolvedBeatSaberDir)) {
    $referenceScript = Join-Path $PSScriptRoot 'get-game-references.ps1'
    $resolvedBeatSaberDir = & $referenceScript -GameVersion $GameVersion
}
$resolvedBeatSaberDir = (Resolve-Path -LiteralPath $resolvedBeatSaberDir).Path

if (-not (Test-Path -LiteralPath (Join-Path $resolvedBeatSaberDir 'Beat Saber_Data\Managed\Main.dll'))) {
    throw "BeatSaberDir must contain 'Beat Saber_Data\\Managed\\Main.dll': $resolvedBeatSaberDir"
}

$dependencyProfile = switch ($GameVersion) {
    '1.39.1' { '1.39.1' }
    { $_ -in '1.40.2', '1.40.3', '1.40.4', '1.40.5', '1.40.6', '1.40.7', '1.40.8' } { '1.40.2-1.40.8' }
    default { $GameVersion }
}
$dependencyDirectory = Join-Path $repositoryRoot "dependencies\\$dependencyProfile"
$bsipaArchive = Get-ChildItem -LiteralPath $dependencyDirectory -Filter 'BSIPA v*.zip' | Select-Object -First 1
if ($null -eq $bsipaArchive) {
    throw "No BSIPA archive was found in $dependencyDirectory"
}

$bsipaExtractionDirectory = Join-Path $repositoryRoot "artifacts\\bsipa\\$dependencyProfile"
$hiveVersioningPath = Join-Path $bsipaExtractionDirectory 'IPA\\Libs\\Hive.Versioning.dll'
$harmonyPath = Join-Path $bsipaExtractionDirectory 'IPA\\Libs\\0Harmony.dll'
if (-not (Test-Path -LiteralPath $hiveVersioningPath) -or
    -not (Test-Path -LiteralPath $harmonyPath)) {
    Expand-Archive -LiteralPath $bsipaArchive.FullName -DestinationPath $bsipaExtractionDirectory -Force
}

$frameworkReferenceDirectory = $null
if ($usingStrippedReferences) {
    $frameworkReferenceDirectory = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2'
    if (-not (Test-Path -LiteralPath $frameworkReferenceDirectory)) {
        throw ".NET Framework 4.7.2 reference assemblies are required for stripped-reference builds: $frameworkReferenceDirectory"
    }
}
if (-not (Test-Path -LiteralPath $hiveVersioningPath)) {
    throw "BSIPA archive did not contain Hive.Versioning.dll: $($bsipaArchive.FullName)"
}
if (-not (Test-Path -LiteralPath $harmonyPath)) {
    throw "BSIPA archive did not contain 0Harmony.dll: $($bsipaArchive.FullName)"
}

$arguments = @(
    'build', $solutionPath,
    '--configuration', $Configuration,
    "-p:BeatSaberVersion=$GameVersion",
    "-p:BeatSaberDir=$resolvedBeatSaberDir",
    "-p:HiveVersioningPath=$hiveVersioningPath",
    "-p:HarmonyPath=$harmonyPath"
)

if ($NoRestore) {
    $arguments += '--no-restore'
}

if (-not $Install) {
    $arguments += '-p:DisableCopyToGame=true'
}

if ($DisableZipRelease) {
    $arguments += '-p:DisableZipRelease=true'
}

if ($null -ne $frameworkReferenceDirectory) {
    $arguments += "-p:FrameworkPathOverride=$frameworkReferenceDirectory"
}

$artifactDirectory = Join-Path $repositoryRoot "BeatLocator\bin\$Configuration\$GameVersion\net472\Artifact"
$resolvedRepositoryPrefix = [IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\') + '\'
$resolvedArtifactDirectory = [IO.Path]::GetFullPath($artifactDirectory)
if (-not $resolvedArtifactDirectory.StartsWith(
        $resolvedRepositoryPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean an artifact directory outside the repository: $resolvedArtifactDirectory"
}
if (Test-Path -LiteralPath $resolvedArtifactDirectory) {
    Remove-Item -LiteralPath $resolvedArtifactDirectory -Recurse -Force
}

& dotnet @arguments
exit $LASTEXITCODE
