# Build profiles

| Game version | Dependency directory | BSIPA | BeatLeader | BetterSongSearch | SiraUtil | SongCore |
| --- | --- | --- | --- | --- | --- | --- |
| 1.39.1 | `dependencies/1.39.1` | 4.3.6 | 0.9.34 | 0.8.2 | 3.1.14 | 3.14.15 |
| 1.40.2 | `dependencies/1.40.2-1.40.8` | 4.3.6 | 0.10.0 | 0.8.2 | 3.2.1 | 3.15.3 |
| 1.40.8 | `dependencies/1.40.2-1.40.8` | 4.3.6 | 0.10.0 | 0.8.2 | 3.2.1 | 3.15.3 |

`scripts/build-version.ps1` downloads the matching stripped game references from
`beat-forge/beatsaber-stripped` when `-BeatSaberDir` is omitted. The clone is
cached under the ignored `artifacts/game-references` directory, so game binaries
are never committed to this repository. The script also extracts
`Hive.Versioning.dll` from the matching BSIPA archive to `artifacts/bsipa`. It
does not install the build into a game unless `-Install` is specified. Stripped
builds use the installed .NET Framework 4.7.2 reference assemblies for the base
class library because the stripped game's `mscorlib` is intentionally incomplete.

To compile with automatically downloaded references:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-version.ps1 -GameVersion 1.40.8 -NoRestore
```

## Unified 1.40.2-1.40.8 artifact

`scripts/build-1.40.2-1.40.8.ps1` validates the shared source against all seven
stripped game versions, then copies the 1.40.2 build as the one distributable
artifact. Its embedded manifest remains `gameVersion: 1.40.2`; this is the
baseline recognised by this compatibility family.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-1.40.2-1.40.8.ps1 -Configuration Release -NoRestore
```

The distributable DLL and ZIP are written to
`BeatLocator/bin/Release/1.40.2-1.40.8/`.

To instead build against a local 1.40.8 installation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-version.ps1 -GameVersion 1.40.8 -BeatSaberDir 'C:\Path\To\Beat Saber' -NoRestore
```

The project reads BSML, SiraUtil, and SongCore from the versioned dependency
directory. BeatLeader and BetterSongSearch remain there for API validation and
runtime installation; the current source resolves both dynamically through
BSIPA reflection.

The build-only dependencies can be downloaded from BeatMods with pinned
SHA-256 checksums instead of committing their binaries:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\get-mod-dependencies.ps1 -Profile 1.39.1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\get-mod-dependencies.ps1 -Profile 1.40.2-1.40.8
```

GitHub Actions runs these downloads automatically before building both profiles.

## Adding a future profile

When a future Beat Saber version gains a usable dependency set, add
`dependencies/<game-version>/` with BSML, SiraUtil, SongCore, BeatLeader,
BetterSongSearch, and the matching BSIPA ZIP. The generic scripts accept any
three-part game version and will download `version/<game-version>` from the
stripped-reference repository automatically. If several game versions share one
dependency directory, add a `DependencyProfile` mapping in
`BeatLocator/Directory.Build.props`. Override the four `*DependencyVersion`
properties there when their manifest requirements differ from the defaults.
