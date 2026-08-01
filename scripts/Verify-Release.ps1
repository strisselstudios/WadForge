[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ReleaseDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ReleaseDirectory)) {
    throw "Release directory not found: $ReleaseDirectory"
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$ZipFiles = @(
    Get-ChildItem -LiteralPath $ReleaseDirectory -File -Filter "*.zip"
)

$SuiteZip = $ZipFiles |
    Where-Object {
        $_.Name -like "WadForge-TrenchBroom-Suite-v*.zip"
    } |
    Select-Object -First 1

$SourceZip = $ZipFiles |
    Where-Object {
        $_.Name -like "WadForge-TrenchBroom-Source-v*.zip"
    } |
    Select-Object -First 1

if (-not $SuiteZip) {
    throw "The suite ZIP was not found."
}

if (-not $SourceZip) {
    throw "The source ZIP was not found."
}

$CommonForbiddenDirectories = @(
    "backups",
    "TransferPackages",
    "TestResults",
    ".vs",
    "bin",
    "obj",
    "build",
    "vcpkg_installed"
)

foreach ($ZipFile in @($SuiteZip, $SourceZip)) {
    $Archive = [System.IO.Compression.ZipFile]::OpenRead($ZipFile.FullName)

    try {
        if ($Archive.Entries.Count -eq 0) {
            throw "ZIP contains no entries: $($ZipFile.FullName)"
        }

        foreach ($Entry in $Archive.Entries) {
            $NormalizedName = $Entry.FullName.Replace("\", "/")
            $Segments = @(
                $NormalizedName.Split(
                    [char[]]@("/"),
                    [System.StringSplitOptions]::RemoveEmptyEntries
                )
            )

            foreach ($ForbiddenDirectory in $CommonForbiddenDirectories) {
                if ($Segments -contains $ForbiddenDirectory) {
                    throw "Forbidden release directory found: $NormalizedName"
                }
            }

            $Extension = [System.IO.Path]::GetExtension($NormalizedName).ToLowerInvariant()

            if ($ZipFile.FullName -eq $SuiteZip.FullName) {
                if ($Extension -in @(
                        ".wad",
                        ".map",
                        ".bsp",
                        ".pdb",
                        ".ilk",
                        ".obj",
                        ".lib",
                        ".exp"
                    )) {
                    throw "Forbidden suite entry found: $NormalizedName"
                }
            }
            else {
                if ($Extension -in @(
                        ".pdb",
                        ".ilk",
                        ".obj",
                        ".lib",
                        ".exp"
                    )) {
                    throw "Compiled build artifact found in source ZIP: $NormalizedName"
                }
            }
        }
    }
    finally {
        $Archive.Dispose()
    }
}

$ChecksumPath = Join-Path $ReleaseDirectory "SHA256SUMS.txt"

if (-not (Test-Path -LiteralPath $ChecksumPath)) {
    throw "SHA256SUMS.txt is missing."
}

$ChecksumText = [System.IO.File]::ReadAllText($ChecksumPath)

foreach ($ZipFile in @($SuiteZip, $SourceZip)) {
    $Hash = (Get-FileHash -LiteralPath $ZipFile.FullName -Algorithm SHA256).Hash

    if (-not $ChecksumText.Contains($Hash)) {
        throw "Checksum file does not contain the hash for: $($ZipFile.Name)"
    }
}

Write-Host ""
Write-Host "RELEASE VERIFICATION PASSED" -ForegroundColor Green
Write-Host ""
Write-Host $ReleaseDirectory
