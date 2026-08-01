# Building WadForge

## Supported publication target

The supplied build scripts target Windows x64.

## Required tools

- Windows 10 or Windows 11 x64.
- Visual Studio 2022 with Desktop development with C++.
- .NET SDK 9.0.316, as pinned by `global.json`.
- Git.
- CMake.
- Pandoc.
- Qt 6.9 for MSVC 2022 x64 when building TrenchBroom.

TrenchBroom v2026.1 uses Git submodules and vcpkg-managed dependencies. Clone it recursively or initialize all submodules before configuration.

## Build WadForge and Companion

Open Developer PowerShell for Visual Studio 2022 in the repository root and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-WadForge.ps1
```

Published applications are written to:

```text
artifacts\publish\WadForge
artifacts\publish\TrenchBroom-Companion
```

The .NET applications are published as Windows x64 self-contained single-file executables.

## Build the compatible TrenchBroom

Pass the Qt MSVC CMake package path. Example:

```powershell
powershell.exe `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File .\scripts\Build-TrenchBroom.ps1 `
    -QtCMakePrefixPath "C:\Qt\6.9.2\msvc2022_64"
```

The script:

1. Clones the official TrenchBroom repository recursively.
2. Checks out commit `b8c14a93c6945a389c56ff7bf77e869c16f24895`.
3. Applies `patches\TrenchBroom-v2026.1-WadForge.patch`.
4. Configures Visual Studio 2022 x64.
5. Builds `MinSizeRel`.
6. Builds the `TrenchBroom` target with one parallel job.
7. Installs the result under `artifacts\trenchbroom-install`.

The single-job build is intentional because Windows linker program-database output was memory and disk sensitive during development.

## Build provenance

Do not replace the pinned TrenchBroom commit without:

- Regenerating and validating the patch.
- Rebuilding the compatible executable.
- Updating notices and documentation.
- Creating new release checksums.
- Publishing the exact corresponding source.
