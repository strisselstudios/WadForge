[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$QtCMakePrefixPath,

    [string]$Configuration = "MinSizeRel",

    [string]$WorkRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..")
)

if (-not $WorkRoot) {
    $WorkRoot = Join-Path $RepositoryRoot "artifacts\trenchbroom-work"
}

$SourceRoot = Join-Path $WorkRoot "source"
$BuildRoot = Join-Path $WorkRoot "build"
$InstallRoot = Join-Path $RepositoryRoot "artifacts\trenchbroom-install"
$PatchPath = Join-Path $RepositoryRoot "patches\TrenchBroom-v2026.1-WadForge.patch"

$UpstreamRepository = "https://github.com/TrenchBroom/TrenchBroom.git"
$PinnedVersion = "v2026.1"
$PinnedCommit = "b8c14a93c6945a389c56ff7bf77e869c16f24895"

foreach ($CommandName in @(
        "git.exe",
        "cmake.exe",
        "cl.exe"
    )) {
    if (-not (Get-Command $CommandName -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $CommandName. Run from Developer PowerShell for VS 2022."
    }
}

if (-not (Get-Command pandoc.exe -ErrorAction SilentlyContinue)) {
    throw "pandoc.exe was not found."
}

if (-not (Test-Path -LiteralPath $QtCMakePrefixPath)) {
    throw "Qt CMake prefix path not found: $QtCMakePrefixPath"
}

if (-not (Test-Path -LiteralPath $PatchPath)) {
    throw "Patch not found: $PatchPath"
}

if (Test-Path -LiteralPath $WorkRoot) {
    Remove-Item -LiteralPath $WorkRoot -Recurse -Force
}

if (Test-Path -LiteralPath $InstallRoot) {
    Remove-Item -LiteralPath $InstallRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $WorkRoot -Force | Out-Null

& git.exe clone `
    --recursive `
    $UpstreamRepository `
    $SourceRoot

if ($LASTEXITCODE -ne 0) {
    throw "TrenchBroom clone failed with exit code $LASTEXITCODE."
}

& git.exe -C $SourceRoot checkout --detach $PinnedCommit

if ($LASTEXITCODE -ne 0) {
    throw "TrenchBroom checkout failed with exit code $LASTEXITCODE."
}

& git.exe -C $SourceRoot submodule update --init --recursive

if ($LASTEXITCODE -ne 0) {
    throw "TrenchBroom submodule initialization failed with exit code $LASTEXITCODE."
}

& git.exe -C $SourceRoot apply --check $PatchPath

if ($LASTEXITCODE -ne 0) {
    throw "The WadForge TrenchBroom patch does not apply cleanly."
}

& git.exe -C $SourceRoot apply $PatchPath

if ($LASTEXITCODE -ne 0) {
    throw "Applying the WadForge TrenchBroom patch failed."
}

& cmake.exe `
    -S $SourceRoot `
    -B $BuildRoot `
    -G "Visual Studio 17 2022" `
    -T v143 `
    -A x64 `
    "-DCMAKE_PREFIX_PATH=$QtCMakePrefixPath"

if ($LASTEXITCODE -ne 0) {
    throw "TrenchBroom CMake configuration failed with exit code $LASTEXITCODE."
}

& cmake.exe `
    --build $BuildRoot `
    --config $Configuration `
    --target TrenchBroom `
    --parallel 1

if ($LASTEXITCODE -ne 0) {
    throw "TrenchBroom build failed with exit code $LASTEXITCODE."
}

& cmake.exe `
    --install $BuildRoot `
    --config $Configuration `
    --prefix $InstallRoot

if ($LASTEXITCODE -ne 0) {
    throw "TrenchBroom installation failed with exit code $LASTEXITCODE."
}

$InstalledExecutable = Join-Path $InstallRoot "TrenchBroom.exe"

if (-not (Test-Path -LiteralPath $InstalledExecutable)) {
    $InstalledExecutable = Get-ChildItem `
        -LiteralPath $InstallRoot `
        -File `
        -Recurse `
        -Filter "TrenchBroom.exe" |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $InstalledExecutable -or -not (Test-Path -LiteralPath $InstalledExecutable)) {
    throw "Installed TrenchBroom.exe was not found under: $InstallRoot"
}

$ExecutableHash = (
    Get-FileHash `
        -LiteralPath $InstalledExecutable `
        -Algorithm SHA256
).Hash

$PatchHash = (
    Get-FileHash `
        -LiteralPath $PatchPath `
        -Algorithm SHA256
).Hash

$MarkerPath = Join-Path `
    (Split-Path -Parent $InstalledExecutable) `
    "wadforge-companion-build.json"

$Marker = [ordered]@{
    schemaVersion = 1
    product = "WadForge-compatible TrenchBroom"
    trenchBroomVersion = $PinnedVersion
    trenchBroomCommit = $PinnedCommit
    configuration = $Configuration
    wadForgeDisplayAliasSupport = $true
    aliasLookupUsesWadEntryFilenameFallback = $true
    executableSha256 = $ExecutableHash
    patchSha256 = $PatchHash
    builtUtc = [DateTime]::UtcNow.ToString("o")
}

$MarkerJson = $Marker | ConvertTo-Json -Depth 10

[System.IO.File]::WriteAllText(
    $MarkerPath,
    $MarkerJson,
    (New-Object System.Text.UTF8Encoding($false))
)

Write-Host ""
Write-Host "PATCHED TRENCHBROOM BUILD PASSED" -ForegroundColor Green
Write-Host ""
Write-Host "Executable:"
Write-Host $InstalledExecutable
Write-Host ""
Write-Host "Compatibility marker:"
Write-Host $MarkerPath
