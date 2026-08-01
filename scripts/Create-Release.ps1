[CmdletBinding()]
param(
    [string]$Version = "1.0.0",

    [string]$WadForgePublishPath,

    [string]$CompanionPublishPath,

    [string]$TrenchBroomInstallPath,

    [Parameter(Mandatory)]
    [string]$PatchedTrenchBroomSourcePath,

    [string]$OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..")
)

if (-not $WadForgePublishPath) {
    $WadForgePublishPath = Join-Path $RepositoryRoot "artifacts\publish\WadForge"
}

if (-not $CompanionPublishPath) {
    $CompanionPublishPath = Join-Path $RepositoryRoot "artifacts\publish\TrenchBroom-Companion"
}

if (-not $TrenchBroomInstallPath) {
    $TrenchBroomInstallPath = Join-Path $RepositoryRoot "artifacts\trenchbroom-install"
}

if (-not $OutputRoot) {
    $OutputRoot = Join-Path $RepositoryRoot "artifacts\releases\v$Version"
}

$SuiteName = "WadForge-TrenchBroom-Suite-v$Version"
$SourceName = "WadForge-TrenchBroom-Source-v$Version"
$SuiteStage = Join-Path $OutputRoot $SuiteName
$SourceStage = Join-Path $OutputRoot $SourceName
$SuiteZip = Join-Path $OutputRoot "$SuiteName.zip"
$SourceZip = Join-Path $OutputRoot "$SourceName.zip"
$Checksums = Join-Path $OutputRoot "SHA256SUMS.txt"
$ReleaseNotesSource = Join-Path $RepositoryRoot "docs\RELEASE-NOTES-v$Version.md"
$ReleaseNotesDestination = Join-Path $OutputRoot "RELEASE-NOTES-v$Version.md"

$RequiredPaths = @(
    $WadForgePublishPath,
    $CompanionPublishPath,
    $TrenchBroomInstallPath,
    $PatchedTrenchBroomSourcePath,
    (Join-Path $RepositoryRoot "LICENSE"),
    (Join-Path $RepositoryRoot "THIRD-PARTY-NOTICES.md"),
    (Join-Path $RepositoryRoot "patches\TrenchBroom-v2026.1-WadForge.patch"),
    $ReleaseNotesSource
)

foreach ($RequiredPath in $RequiredPaths) {
    if (-not (Test-Path -LiteralPath $RequiredPath)) {
        throw "Required release input not found: $RequiredPath"
    }
}

$TrenchBroomExecutable = Get-ChildItem `
    -LiteralPath $TrenchBroomInstallPath `
    -File `
    -Recurse `
    -Filter "TrenchBroom.exe" |
    Select-Object -First 1

if (-not $TrenchBroomExecutable) {
    throw "TrenchBroom.exe was not found under: $TrenchBroomInstallPath"
}

$CompatibilityMarker = Join-Path `
    $TrenchBroomExecutable.DirectoryName `
    "wadforge-companion-build.json"

if (-not (Test-Path -LiteralPath $CompatibilityMarker)) {
    throw "The TrenchBroom compatibility marker is missing: $CompatibilityMarker"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $SuiteStage -Force | Out-Null
New-Item -ItemType Directory -Path $SourceStage -Force | Out-Null

Copy-Item -LiteralPath $WadForgePublishPath -Destination (Join-Path $SuiteStage "WadForge") -Recurse -Force
Copy-Item -LiteralPath $CompanionPublishPath -Destination (Join-Path $SuiteStage "TrenchBroom-Companion") -Recurse -Force
Copy-Item -LiteralPath $TrenchBroomInstallPath -Destination (Join-Path $SuiteStage "TrenchBroom") -Recurse -Force

New-Item -ItemType Directory -Path (Join-Path $SuiteStage "licenses") -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $RepositoryRoot "LICENSE") -Destination (Join-Path $SuiteStage "licenses\GPL-3.0.txt") -Force
Copy-Item -LiteralPath (Join-Path $RepositoryRoot "licenses\TRENCHBROOM-LICENSE.txt") -Destination (Join-Path $SuiteStage "licenses\TRENCHBROOM-LICENSE.txt") -Force
Copy-Item -LiteralPath (Join-Path $RepositoryRoot "THIRD-PARTY-NOTICES.md") -Destination (Join-Path $SuiteStage "licenses\THIRD-PARTY-NOTICES.md") -Force
Copy-Item -LiteralPath (Join-Path $RepositoryRoot "patches\TrenchBroom-v2026.1-WadForge.patch") -Destination (Join-Path $SuiteStage "TrenchBroom\WadForge-TrenchBroom-v2026.1.patch") -Force
Copy-Item -LiteralPath (Join-Path $RepositoryRoot "README.md") -Destination (Join-Path $SuiteStage "README.md") -Force

$FullSuiteStage = [System.IO.Path]::GetFullPath($SuiteStage).TrimEnd("\")
$SuiteChecksumPath = Join-Path $SuiteStage "SHA256SUMS.txt"
$SuiteChecksumLines = New-Object System.Collections.Generic.List[string]

$SuiteFiles = @(
    Get-ChildItem -LiteralPath $SuiteStage -File -Recurse |
        Where-Object {
            -not $_.FullName.Equals(
                $SuiteChecksumPath,
                [System.StringComparison]::OrdinalIgnoreCase
            )
        } |
        Sort-Object FullName
)

foreach ($SuiteFile in $SuiteFiles) {
    $RelativePath = $SuiteFile.FullName.Substring(
        $FullSuiteStage.Length
    ).TrimStart("\")

    $FileHash = (
        Get-FileHash `
            -LiteralPath $SuiteFile.FullName `
            -Algorithm SHA256
    ).Hash

    $SuiteChecksumLines.Add(
        "$FileHash *$RelativePath"
    )
}

[System.IO.File]::WriteAllText(
    $SuiteChecksumPath,
    (($SuiteChecksumLines -join [Environment]::NewLine) + [Environment]::NewLine),
    (New-Object System.Text.UTF8Encoding($false))
)

$RepositorySourceDestination = Join-Path $SourceStage "WadForge"
$TrenchBroomSourceDestination = Join-Path $SourceStage "TrenchBroom-v2026.1-WadForge"

& robocopy.exe `
    $RepositoryRoot `
    $RepositorySourceDestination `
    /E `
    /COPY:DAT `
    /DCOPY:DAT `
    /R:2 `
    /W:2 `
    /XJ `
    /XD ".git" "artifacts" "bin" "obj" ".vs" `
    /XF "*.user" "*.suo" "*.pdb" "*.exe" "*.dll"

if ($LASTEXITCODE -gt 7) {
    throw "Copying WadForge source failed with robocopy exit code $LASTEXITCODE."
}

& robocopy.exe `
    $PatchedTrenchBroomSourcePath `
    $TrenchBroomSourceDestination `
    /E `
    /COPY:DAT `
    /DCOPY:DAT `
    /R:2 `
    /W:2 `
    /XJ `
    /XD ".git" "build" "cmakebuild" "vcpkg_installed" `
    /XF "*.pdb" "*.obj" "*.ilk" "*.exe" "*.dll"

if ($LASTEXITCODE -gt 7) {
    throw "Copying patched TrenchBroom source failed with robocopy exit code $LASTEXITCODE."
}

Copy-Item `
    -LiteralPath (Join-Path $RepositoryRoot "docs\WADFORGE-MODIFICATIONS.md") `
    -Destination (Join-Path $TrenchBroomSourceDestination "WADFORGE-MODIFICATIONS.md") `
    -Force

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $SuiteStage,
    $SuiteZip,
    [System.IO.Compression.CompressionLevel]::Fastest,
    $true
)

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $SourceStage,
    $SourceZip,
    [System.IO.Compression.CompressionLevel]::Fastest,
    $true
)

$ChecksumLines = @(
    "$((Get-FileHash -LiteralPath $SuiteZip -Algorithm SHA256).Hash) *$([System.IO.Path]::GetFileName($SuiteZip))",
    "$((Get-FileHash -LiteralPath $SourceZip -Algorithm SHA256).Hash) *$([System.IO.Path]::GetFileName($SourceZip))"
)

[System.IO.File]::WriteAllText(
    $Checksums,
    (($ChecksumLines -join [Environment]::NewLine) + [Environment]::NewLine),
    (New-Object System.Text.UTF8Encoding($false))
)

Copy-Item `
    -LiteralPath $ReleaseNotesSource `
    -Destination $ReleaseNotesDestination `
    -Force

Write-Host ""
Write-Host "RELEASE PACKAGE CREATION PASSED" -ForegroundColor Green
Write-Host ""
Write-Host $SuiteZip
Write-Host $SourceZip
Write-Host $Checksums
Write-Host $ReleaseNotesDestination
