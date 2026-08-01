[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..")
)

$SolutionPath = Join-Path $RepositoryRoot "WadForge.sln"
$ArtifactsRoot = Join-Path $RepositoryRoot "artifacts\publish"
$WadForgeProject = Join-Path $RepositoryRoot "src\WadForge.App\WadForge.App.csproj"
$CompanionProject = Join-Path $RepositoryRoot "src\TrenchBroom.Companion.App\TrenchBroom.Companion.App.csproj"
$WadForgeOutput = Join-Path $ArtifactsRoot "WadForge"
$CompanionOutput = Join-Path $ArtifactsRoot "TrenchBroom-Companion"

if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {
    throw "dotnet.exe was not found."
}

foreach ($RequiredPath in @(
        $SolutionPath,
        $WadForgeProject,
        $CompanionProject
    )) {
    if (-not (Test-Path -LiteralPath $RequiredPath)) {
        throw "Required build input not found: $RequiredPath"
    }
}

if (Test-Path -LiteralPath $ArtifactsRoot) {
    Remove-Item -LiteralPath $ArtifactsRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $WadForgeOutput -Force | Out-Null
New-Item -ItemType Directory -Path $CompanionOutput -Force | Out-Null

& dotnet.exe restore $SolutionPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

& dotnet.exe build `
    $SolutionPath `
    --configuration $Configuration `
    --no-restore

if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

$CommonPublishArguments = @(
    "--configuration",
    $Configuration,
    "--runtime",
    $RuntimeIdentifier,
    "--self-contained",
    "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

& dotnet.exe publish `
    $WadForgeProject `
    @CommonPublishArguments `
    "--output" `
    $WadForgeOutput

if ($LASTEXITCODE -ne 0) {
    throw "WadForge publish failed with exit code $LASTEXITCODE."
}

& dotnet.exe publish `
    $CompanionProject `
    @CommonPublishArguments `
    "--output" `
    $CompanionOutput

if ($LASTEXITCODE -ne 0) {
    throw "TrenchBroom Companion publish failed with exit code $LASTEXITCODE."
}

$RequiredExecutables = @(
    (Join-Path $WadForgeOutput "WadForge.exe"),
    (Join-Path $CompanionOutput "TrenchBroom-Companion.exe")
)

foreach ($RequiredExecutable in $RequiredExecutables) {
    if (-not (Test-Path -LiteralPath $RequiredExecutable)) {
        throw "Required published executable is missing: $RequiredExecutable"
    }
}

Write-Host ""
Write-Host "WADFORGE AND COMPANION BUILD PASSED" -ForegroundColor Green
Write-Host ""
Write-Host $WadForgeOutput
Write-Host $CompanionOutput
