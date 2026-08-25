using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionWadLibraryService
{
    private readonly Dictionary<string, CachedWadAsset>
        _assetCache =
            new(
                StringComparer.OrdinalIgnoreCase);

    public CompanionWadLibraryImportResult Import(
        string managedDataRoot,
        string sourceWadPath)
    {
        if (string.IsNullOrWhiteSpace(
                managedDataRoot))
        {
            throw new ArgumentException(
                "A Companion managed data root is required.",
                nameof(managedDataRoot));
        }

        if (string.IsNullOrWhiteSpace(
                sourceWadPath))
        {
            throw new ArgumentException(
                "A WAD path is required.",
                nameof(sourceWadPath));
        }

        string sourcePath =
            Path.GetFullPath(
                sourceWadPath);

        if (!File.Exists(
                sourcePath))
        {
            throw new FileNotFoundException(
                "The WAD archive could not be found.",
                sourcePath);
        }

        WadRegistrationResult inspection =
            WadRegistrationService.Inspect(
                sourcePath);

        if (!inspection.WadIsValid)
        {
            throw new InvalidDataException(
                $"'{Path.GetFileName(sourcePath)}' is not a valid WAD2 or WAD3 archive. " +
                inspection.Validation);
        }

        string libraryDirectory =
            CompanionManagedDataRootService
                .GetWadLibraryDirectory(
                    managedDataRoot);

        Directory.CreateDirectory(
            libraryDirectory);

        string sourceHash =
            ComputeSha256(
                sourcePath);

        string? existingByHash =
            FindExistingByHash(
                managedDataRoot,
                sourceHash);

        if (!string.IsNullOrWhiteSpace(
                existingByHash))
        {
            CopyAliasManifestIfPresent(
                sourcePath,
                existingByHash);

            CompanionWadLibraryAsset? existingAsset =
                GetAsset(
                    existingByHash);

            if (existingAsset is null)
            {
                WadRegistrationResult existingInspection =
                    WadRegistrationService.Inspect(
                        existingByHash);

                return new CompanionWadLibraryImportResult(
                    existingByHash,
                    sourceHash,
                    existingInspection.WadFormat,
                    existingInspection.TextureCount,
                    false,
                    true);
            }

            return new CompanionWadLibraryImportResult(
                existingAsset.WadPath,
                existingAsset.AssetId,
                existingAsset.WadFormat,
                existingAsset.TextureCount,
                false,
                true);
        }

        string destinationPath =
            Path.Combine(
                libraryDirectory,
                Path.GetFileName(
                    sourcePath));

        if (File.Exists(
                destinationPath))
        {
            destinationPath =
                GetCollisionSafeDestination(
                    libraryDirectory,
                    Path.GetFileName(
                        sourcePath),
                    sourceHash);
        }

        File.Copy(
            sourcePath,
            destinationPath,
            overwrite: false);

        try
        {
            CopyAliasManifestIfPresent(
                sourcePath,
                destinationPath);

            WadRegistrationResult destinationInspection =
                WadRegistrationService.Inspect(
                    destinationPath);

            if (!destinationInspection.WadIsValid)
            {
                throw new InvalidDataException(
                    $"The managed library copy of '{Path.GetFileName(sourcePath)}' failed validation.");
            }

            CacheAsset(
                destinationPath,
                sourceHash,
                destinationInspection);

            return new CompanionWadLibraryImportResult(
                destinationPath,
                sourceHash,
                destinationInspection.WadFormat,
                destinationInspection.TextureCount,
                true,
                false);
        }
        catch
        {
            _assetCache.Remove(
                Path.GetFullPath(
                    destinationPath));

            string destinationManifest =
                destinationPath +
                ".wadforge.json";

            if (File.Exists(
                    destinationManifest))
            {
                File.Delete(
                    destinationManifest);
            }

            if (File.Exists(
                    destinationPath))
            {
                File.Delete(
                    destinationPath);
            }

            throw;
        }
    }

    public IReadOnlyList<string> GetWadPaths(
        string managedDataRoot)
    {
        string libraryDirectory =
            CompanionManagedDataRootService
                .GetWadLibraryDirectory(
                    managedDataRoot);

        if (!Directory.Exists(
                libraryDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(
                libraryDirectory,
                "*.wad",
                SearchOption.TopDirectoryOnly)
            .Select(
                Path.GetFullPath)
            .OrderBy(
                path =>
                    Path.GetFileName(
                        path),
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                path =>
                    path,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<CompanionWadLibraryAsset> GetAssets(
        string managedDataRoot)
    {
        IReadOnlyList<string> wadPaths =
            GetWadPaths(
                managedDataRoot);

        HashSet<string> existingPaths =
            wadPaths.ToHashSet(
                StringComparer.OrdinalIgnoreCase);

        string libraryDirectory =
            Path.GetFullPath(
                CompanionManagedDataRootService
                    .GetWadLibraryDirectory(
                        managedDataRoot))
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        foreach (string cachedPath in
                 _assetCache.Keys.ToArray())
        {
            if (cachedPath.StartsWith(
                    libraryDirectory,
                    StringComparison.OrdinalIgnoreCase) &&
                !existingPaths.Contains(
                    cachedPath))
            {
                _assetCache.Remove(
                    cachedPath);
            }
        }

        List<CompanionWadLibraryAsset> assets =
            new();

        foreach (string wadPath in
                 wadPaths)
        {
            CompanionWadLibraryAsset? asset =
                GetAsset(
                    wadPath);

            if (asset is not null)
            {
                assets.Add(
                    asset);
            }
        }

        return assets
            .OrderBy(
                asset =>
                    asset.WadFormat,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                asset =>
                    asset.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                asset =>
                    asset.WadPath,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public CompanionWadLibraryAsset? FindAsset(
        string managedDataRoot,
        string assetId)
    {
        if (string.IsNullOrWhiteSpace(
                assetId))
        {
            return null;
        }

        string normalized =
            assetId.Trim().ToUpperInvariant();

        return GetAssets(
                managedDataRoot)
            .FirstOrDefault(
                asset =>
                    string.Equals(
                        asset.AssetId,
                        normalized,
                        StringComparison.OrdinalIgnoreCase));
    }

    public bool IsLibraryWad(
        string managedDataRoot,
        string wadPath)
    {
        if (string.IsNullOrWhiteSpace(
                managedDataRoot) ||
            string.IsNullOrWhiteSpace(
                wadPath))
        {
            return false;
        }

        try
        {
            string libraryDirectory =
                Path.GetFullPath(
                    CompanionManagedDataRootService
                        .GetWadLibraryDirectory(
                            managedDataRoot))
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            string fullWadPath =
                Path.GetFullPath(
                    wadPath);

            return fullWadPath.StartsWith(
                libraryDirectory,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void Remove(
        string managedDataRoot,
        string wadPath)
    {
        if (!IsLibraryWad(
                managedDataRoot,
                wadPath))
        {
            throw new InvalidOperationException(
                "Only a WAD stored in the Companion library can be deleted by the library service.");
        }

        string fullPath =
            Path.GetFullPath(
                wadPath);

        string sidecar =
            fullPath +
            ".wadforge.json";

        if (File.Exists(
                sidecar))
        {
            File.Delete(
                sidecar);
        }

        if (File.Exists(
                fullPath))
        {
            File.Delete(
                fullPath);
        }

        _assetCache.Remove(
            fullPath);
    }

    private CompanionWadLibraryAsset? GetAsset(
        string wadPath)
    {
        string fullPath =
            Path.GetFullPath(
                wadPath);

        if (!File.Exists(
                fullPath))
        {
            _assetCache.Remove(
                fullPath);

            return null;
        }

        FileInfo info =
            new(
                fullPath);

        if (_assetCache.TryGetValue(
                fullPath,
                out CachedWadAsset? cached) &&
            cached is not null &&
            cached.Length ==
                info.Length &&
            cached.LastWriteUtcTicks ==
                info.LastWriteTimeUtc.Ticks)
        {
            return cached.Asset;
        }

        WadRegistrationResult inspection =
            WadRegistrationService.Inspect(
                fullPath);

        if (!inspection.WadIsValid)
        {
            _assetCache.Remove(
                fullPath);

            return null;
        }

        string sha256 =
            ComputeSha256(
                fullPath);

        CompanionWadLibraryAsset asset =
            new(
                sha256,
                fullPath,
                inspection.WadFormat,
                inspection.TextureCount,
                Path.GetFileName(
                    fullPath));

        _assetCache[fullPath] =
            new CachedWadAsset(
                info.Length,
                info.LastWriteTimeUtc.Ticks,
                asset);

        return asset;
    }

    private void CacheAsset(
        string wadPath,
        string sha256,
        WadRegistrationResult inspection)
    {
        string fullPath =
            Path.GetFullPath(
                wadPath);

        FileInfo info =
            new(
                fullPath);

        CompanionWadLibraryAsset asset =
            new(
                sha256,
                fullPath,
                inspection.WadFormat,
                inspection.TextureCount,
                Path.GetFileName(
                    fullPath));

        _assetCache[fullPath] =
            new CachedWadAsset(
                info.Length,
                info.LastWriteTimeUtc.Ticks,
                asset);
    }

    private string? FindExistingByHash(
        string managedDataRoot,
        string sourceHash)
    {
        CompanionWadLibraryAsset? existing =
            GetAssets(
                managedDataRoot)
            .FirstOrDefault(
                asset =>
                    string.Equals(
                        asset.AssetId,
                        sourceHash,
                        StringComparison.OrdinalIgnoreCase));

        return existing?.WadPath;
    }

    private static string GetCollisionSafeDestination(
        string libraryDirectory,
        string sourceFileName,
        string sourceHash)
    {
        string baseName =
            Path.GetFileNameWithoutExtension(
                sourceFileName);

        string extension =
            Path.GetExtension(
                sourceFileName);

        string shortHash =
            sourceHash[..8]
                .ToLowerInvariant();

        string candidate =
            Path.Combine(
                libraryDirectory,
                $"{baseName}-{shortHash}{extension}");

        if (!File.Exists(
                candidate))
        {
            return candidate;
        }

        if (string.Equals(
                ComputeSha256(
                    candidate),
                sourceHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        throw new IOException(
            $"Companion could not create a unique managed library name for '{sourceFileName}'.");
    }

    private static void CopyAliasManifestIfPresent(
        string sourceWadPath,
        string destinationWadPath)
    {
        string sourceManifest =
            sourceWadPath +
            ".wadforge.json";

        if (!File.Exists(
                sourceManifest))
        {
            return;
        }

        string destinationManifest =
            destinationWadPath +
            ".wadforge.json";

        File.Copy(
            sourceManifest,
            destinationManifest,
            overwrite: true);
    }

    private static string ComputeSha256(
        string filePath)
    {
        using FileStream stream =
            File.OpenRead(
                filePath);

        return Convert.ToHexString(
            SHA256.HashData(
                stream));
    }

    private sealed record CachedWadAsset(
        long Length,
        long LastWriteUtcTicks,
        CompanionWadLibraryAsset Asset);
}

public sealed record CompanionWadLibraryAsset(
    string AssetId,
    string WadPath,
    string WadFormat,
    int TextureCount,
    string DisplayName);

public sealed record CompanionWadLibraryImportResult(
    string WadPath,
    string Sha256,
    string WadFormat,
    int TextureCount,
    bool CopiedIntoLibrary,
    bool AlreadyPresent);
