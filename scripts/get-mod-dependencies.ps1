[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('1.39.1', '1.40.2-1.40.8')]
    [string]$Profile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# BeatMods requires TLS 1.2, while Windows PowerShell 5.1 may otherwise choose
# an older protocol on some machines.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$dependencyDirectory = Join-Path $repositoryRoot "dependencies\$Profile"
$cacheDirectory = Join-Path $repositoryRoot "artifacts\mod-dependencies\$Profile"

$bsipa = @{
    ArchiveName = 'BSIPA-4.3.6.zip'
    Url = 'https://beatmods.com/cdn/mod/22ddb9499bf8b308ea30d2b555172baf.zip'
    ArchiveSha256 = 'FBD6EAFB662D63032B06DC964B6CF84E8B5DD5566527131D8C0B8EA63280043A'
    SourceRelativePath = $null
    DestinationName = 'BSIPA v4.3.6.zip'
    FileSha256 = 'FBD6EAFB662D63032B06DC964B6CF84E8B5DD5566527131D8C0B8EA63280043A'
}

$profileDependencies = switch ($Profile) {
    '1.39.1' {
        @(
            @{
                ArchiveName = 'BSML-1.12.4.zip'
                Url = 'https://beatmods.com/cdn/mod/3370d61ad7cfd48962d9be19bc3d1e5e.zip'
                ArchiveSha256 = 'DBA1725252A1A8DDB87D307B1F6D3450A510F9D3F89F111DFBDFB50ED1036CDE'
                SourceRelativePath = 'Plugins\BSML.dll'
                DestinationName = 'BSML.dll'
                FileSha256 = 'AE522D523054B426488A8F11BB44B732C3C3A493B0C6E3B1C3E18E478717F827'
            },
            @{
                ArchiveName = 'SiraUtil-3.1.14.zip'
                Url = 'https://beatmods.com/cdn/mod/7def22a781c08d0b3c5f2b4e77fdec35.zip'
                ArchiveSha256 = 'A5E7BADB410BD2F384364AEBFD69E666F38D9902F5A822FFAA2423A0359CD8A9'
                SourceRelativePath = 'Plugins\SiraUtil.dll'
                DestinationName = 'SiraUtil.dll'
                FileSha256 = '815328AD805A6552466CD43D729F74E195FA82CCC5D627D37123B48FC8E2A507'
            },
            @{
                ArchiveName = 'SongCore-3.14.15.zip'
                Url = 'https://beatmods.com/cdn/mod/d6d96830b7755e1a1f44d7faf328ddc5.zip'
                ArchiveSha256 = 'CF8EBB057F20DE9352566FD44DC546E0D65A46397B8EF0A5ADCBD9EC9FB20CB5'
                SourceRelativePath = 'Plugins\SongCore.dll'
                DestinationName = 'SongCore.dll'
                FileSha256 = 'CF6FDC49573E0079CBCDC04EF87044EC315CB2524852D84CD855AD2D4D8D514E'
            }
        )
    }
    '1.40.2-1.40.8' {
        @(
            @{
                ArchiveName = 'BSML-1.12.5.zip'
                Url = 'https://beatmods.com/cdn/mod/ce406862c55fdc1b572f99f8e4dbd1eb.zip'
                ArchiveSha256 = '2E34A76E7B5750CE617C0DF5ADC995745CB9AB0D71DDE9E0076B2EA592EBB7BD'
                SourceRelativePath = 'Plugins\BSML.dll'
                DestinationName = 'BSML.dll'
                FileSha256 = '77E8E4466ED33C9DCAACBA628415A9A96A3534780FCDC2632C018C7A0385ADA0'
            },
            @{
                ArchiveName = 'SiraUtil-3.2.1.zip'
                Url = 'https://beatmods.com/cdn/mod/5b90d82d3a9c095747c0ec8cde199d4c.zip'
                ArchiveSha256 = 'B9C296D1A9CE6DF2100F48510573DB30242DCAC6F58077FF359730C337C39858'
                SourceRelativePath = 'Plugins\SiraUtil.dll'
                DestinationName = 'SiraUtil.dll'
                FileSha256 = 'B4C0242EFC5E7DA7C7B18D9FC09298B2B8A0D099393A55E9D235A93A7F0ED02B'
            },
            @{
                ArchiveName = 'SongCore-3.15.3.zip'
                Url = 'https://beatmods.com/cdn/mod/146b940124681179768738b8ab336103.zip'
                ArchiveSha256 = '50225A4AD33D17952DB5B05B30FFE365B3B0C165F265D2CC904865BFBC8A4C3F'
                SourceRelativePath = 'Plugins\SongCore.dll'
                DestinationName = 'SongCore.dll'
                FileSha256 = 'C1C502BA5C205A4490DC2EFCB697A75BBCE7342172A98273A84F0E91EE2CF699'
            }
        )
    }
}

function Test-Sha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedHash
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    return $actualHash -eq $ExpectedHash
}

function Get-VerifiedArchive {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Dependency
    )

    $archivePath = Join-Path $cacheDirectory $Dependency.ArchiveName
    if (Test-Sha256 -Path $archivePath -ExpectedHash $Dependency.ArchiveSha256) {
        return $archivePath
    }

    $temporaryPath = "$archivePath.download"
    Write-Host "Downloading $($Dependency.ArchiveName)..."
    Invoke-WebRequest -Uri $Dependency.Url -OutFile $temporaryPath -UseBasicParsing

    if (-not (Test-Sha256 -Path $temporaryPath -ExpectedHash $Dependency.ArchiveSha256)) {
        throw "SHA-256 verification failed for $($Dependency.ArchiveName)."
    }

    Move-Item -LiteralPath $temporaryPath -Destination $archivePath -Force
    return $archivePath
}

New-Item -ItemType Directory -Path $dependencyDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null

$dependencies = @($bsipa) + @($profileDependencies)
foreach ($dependency in $dependencies) {
    $archivePath = Get-VerifiedArchive -Dependency $dependency
    $destinationPath = Join-Path $dependencyDirectory $dependency.DestinationName

    if ($null -eq $dependency.SourceRelativePath) {
        Copy-Item -LiteralPath $archivePath -Destination $destinationPath -Force
    }
    else {
        $archiveBaseName = [IO.Path]::GetFileNameWithoutExtension($dependency.ArchiveName)
        $extractionDirectory = Join-Path $cacheDirectory "expanded\$archiveBaseName"
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractionDirectory -Force

        $sourcePath = Join-Path $extractionDirectory $dependency.SourceRelativePath
        if (-not (Test-Sha256 -Path $sourcePath -ExpectedHash $dependency.FileSha256)) {
            throw "SHA-256 verification failed for $($dependency.SourceRelativePath)."
        }

        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
    }

    if (-not (Test-Sha256 -Path $destinationPath -ExpectedHash $dependency.FileSha256)) {
        throw "SHA-256 verification failed for $destinationPath."
    }

    Write-Host "Prepared $($dependency.DestinationName) for profile $Profile."
}
