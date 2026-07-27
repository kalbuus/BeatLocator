[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$GameVersion,

    [string]$DestinationRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\game-references')
)

$destination = Join-Path $DestinationRoot $GameVersion
$mainAssembly = Join-Path $destination 'Beat Saber_Data\Managed\Main.dll'
if (Test-Path -LiteralPath $mainAssembly) {
    Write-Output (Resolve-Path -LiteralPath $destination).Path
    return
}

if (Test-Path -LiteralPath $destination) {
    throw "Reference directory exists but is incomplete: $destination. Remove it manually, then run again."
}

if ($null -eq (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is required to download the stripped Beat Saber references.'
}

New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
$branch = "version/$GameVersion"
$repositoryUrl = 'https://github.com/beat-forge/beatsaber-stripped.git'

& git clone --depth 1 --branch $branch $repositoryUrl $destination
if ($LASTEXITCODE -ne 0) {
    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    throw "Could not download stripped references for Beat Saber $GameVersion."
}

if (-not (Test-Path -LiteralPath $mainAssembly)) {
    throw "Downloaded repository did not contain Main.dll for Beat Saber $GameVersion."
}

Write-Output (Resolve-Path -LiteralPath $destination).Path
