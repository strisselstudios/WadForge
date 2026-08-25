using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionProjectWadLibraryBindingService
{
    public IReadOnlyList<string> ResolveSelectedWadPaths(
        CompanionProjectSession session,
        string mapPath,
        string managedDataRoot,
        CompanionWadLibraryService libraryService)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        ArgumentNullException.ThrowIfNull(
            libraryService);

        CompanionProjectMap map =
            GetMap(
                session,
                mapPath);

        IReadOnlyList<CompanionWadLibraryAsset> assets =
            libraryService.GetAssets(
                managedDataRoot);

        Dictionary<string, CompanionWadLibraryAsset> byId =
            assets.ToDictionary(
                asset =>
                    asset.AssetId,
                StringComparer.OrdinalIgnoreCase);

        List<string> paths =
            new();

        foreach (string assetId in
                 map.WadAssetIds)
        {
            if (!byId.TryGetValue(
                    assetId,
                    out CompanionWadLibraryAsset? asset) ||
                asset is null)
            {
                throw new InvalidOperationException(
                    $"Map '{map.DisplayName}' references WAD asset '{assetId}' but that asset is no longer in the central library.");
            }

            string fullPath =
                Path.GetFullPath(
                    asset.WadPath);

            if (!File.Exists(
                    fullPath))
            {
                throw new FileNotFoundException(
                    $"Selected library WAD '{asset.DisplayName}' is missing.",
                    fullPath);
            }

            if (!paths.Contains(
                    fullPath,
                    StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(
                    fullPath);
            }
        }

        return paths;
    }

    public CompanionProjectWadLibraryReconciliationResult ReconcileMapReferencesToLibrary(
        CompanionProjectSession session,
        string mapPath,
        string managedDataRoot,
        CompanionWadLibraryService libraryService,
        CompanionProjectWadService projectWadService)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        ArgumentNullException.ThrowIfNull(
            libraryService);

        ArgumentNullException.ThrowIfNull(
            projectWadService);

        string fullMapPath =
            Path.GetFullPath(
                mapPath);

        CompanionProjectMap map =
            GetMap(
                session,
                fullMapPath);

        IReadOnlyList<string> references =
            projectWadService.GetMapWadReferences(
                fullMapPath);

        IReadOnlyList<CompanionWadLibraryAsset> libraryAssets =
            libraryService.GetAssets(
                managedDataRoot);

        Dictionary<string, CompanionWadLibraryAsset> byPath =
            libraryAssets.ToDictionary(
                asset =>
                    Path.GetFullPath(
                        asset.WadPath),
                StringComparer.OrdinalIgnoreCase);

        Dictionary<string, List<CompanionWadLibraryAsset>> byFileName =
            libraryAssets
                .GroupBy(
                    asset =>
                        Path.GetFileName(
                            asset.WadPath),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        group.ToList(),
                    StringComparer.OrdinalIgnoreCase);

        List<string> selectedAssetIds =
            new();

        List<string> canonicalPaths =
            new();

        List<string> issues =
            new();

        int importedToLibrary =
            0;

        foreach (string reference in
                 references)
        {
            try
            {
                CompanionWadLibraryAsset? asset =
                    ResolveReferenceAsset(
                        session,
                        fullMapPath,
                        reference,
                        managedDataRoot,
                        libraryService,
                        byPath,
                        byFileName,
                        out bool copiedIntoLibrary);

                if (asset is null)
                {
                    issues.Add(
                        $"WAD reference could not be resolved: {reference}");

                    continue;
                }

                if (copiedIntoLibrary)
                {
                    importedToLibrary++;
                }

                if (!selectedAssetIds.Contains(
                        asset.AssetId,
                        StringComparer.OrdinalIgnoreCase))
                {
                    selectedAssetIds.Add(
                        asset.AssetId);

                    canonicalPaths.Add(
                        Path.GetFullPath(
                            asset.WadPath));
                }
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    InvalidDataException or
                    InvalidOperationException)
            {
                issues.Add(
                    $"{reference}: {exception.Message}");
            }
        }

        if (issues.Count >
            0)
        {
            return new CompanionProjectWadLibraryReconciliationResult(
                importedToLibrary,
                false,
                false,
                issues);
        }

        CompanionProjectWadSyncResult sync =
            projectWadService.SynchronizeMapWorldspawnWads(
                fullMapPath,
                canonicalPaths);

        bool selectionChanged =
            !map.WadAssetIds.SequenceEqual(
                selectedAssetIds,
                StringComparer.OrdinalIgnoreCase);

        if (selectionChanged)
        {
            map.WadAssetIds =
                selectedAssetIds;

            session.Save();
        }

        return new CompanionProjectWadLibraryReconciliationResult(
            importedToLibrary,
            selectionChanged,
            sync.Changed,
            issues);
    }

    public CompanionLegacyProjectWadCleanupResult CleanupLegacyProjectWads(
        CompanionProjectSession session,
        string managedDataRoot,
        CompanionWadLibraryService libraryService,
        CompanionProjectWadService projectWadService)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        ArgumentNullException.ThrowIfNull(
            libraryService);

        ArgumentNullException.ThrowIfNull(
            projectWadService);

        List<string> issues =
            new();

        if (!session.Project.WadSelectionMigrationCompleted)
        {
            issues.Add(
                "Project WAD selection migration has not completed.");

            return new CompanionLegacyProjectWadCleanupResult(
                0,
                false,
                issues);
        }

        IReadOnlyList<string> legacyWads =
            projectWadService.GetProjectWadPaths(
                session);

        string legacyDirectory =
            Path.Combine(
                session.ProjectDirectory,
                CompanionProjectLayout.WadsDirectoryName);

        foreach (CompanionProjectMap map in
                 session.Project.Maps)
        {
            string fullMapPath =
                CompanionProjectStore.ResolveMapPath(
                    session.ProjectFilePath,
                    map.Path);

            if (!File.Exists(
                    fullMapPath))
            {
                issues.Add(
                    $"{map.DisplayName}: map file is missing.");

                continue;
            }

            IReadOnlyList<string> references;

            try
            {
                references =
                    projectWadService.GetMapWadReferences(
                        fullMapPath);
            }
            catch (Exception exception)
            {
                issues.Add(
                    $"{map.DisplayName}: could not read map WAD references: {exception.Message}");

                continue;
            }

            if (map.WadAssetIds.Count ==
                0)
            {
                if (references.Count >
                    0)
                {
                    issues.Add(
                        $"{map.DisplayName}: map still has WAD references but has no central-library WAD selection.");
                }

                continue;
            }

            try
            {
                IReadOnlyList<string> selectedPaths =
                    ResolveSelectedWadPaths(
                        session,
                        fullMapPath,
                        managedDataRoot,
                        libraryService);

                if (!ReferencesMatchSelectedLibraryPaths(
                        references,
                        selectedPaths,
                        managedDataRoot,
                        libraryService))
                {
                    issues.Add(
                        $"{map.DisplayName}: map WAD references do not yet match its selected central-library WAD paths.");
                }
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    InvalidDataException or
                    InvalidOperationException)
            {
                issues.Add(
                    $"{map.DisplayName}: {exception.Message}");
            }
        }

        if (issues.Count >
            0)
        {
            return new CompanionLegacyProjectWadCleanupResult(
                0,
                false,
                issues);
        }

        List<LegacyDeletionCandidate> candidates =
            new();

        foreach (string legacyWad in
                 legacyWads)
        {
            try
            {
                string legacyHash =
                    ComputeSha256(
                        legacyWad);

                CompanionWadLibraryAsset? libraryAsset =
                    libraryService.FindAsset(
                        managedDataRoot,
                        legacyHash);

                if (libraryAsset is null)
                {
                    throw new InvalidOperationException(
                        "No byte-identical central-library asset was found.");
                }

                string canonicalPath =
                    Path.GetFullPath(
                        libraryAsset.WadPath);

                if (!File.Exists(
                        canonicalPath) ||
                    !libraryService.IsLibraryWad(
                        managedDataRoot,
                        canonicalPath))
                {
                    throw new InvalidOperationException(
                        "The matching central-library WAD could not be verified.");
                }

                string canonicalHash =
                    ComputeSha256(
                        canonicalPath);

                if (!string.Equals(
                        legacyHash,
                        canonicalHash,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        legacyHash,
                        libraryAsset.AssetId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "The central-library WAD did not match the project WAD byte-for-byte.");
                }

                string legacySidecar =
                    legacyWad +
                    ".wadforge.json";

                string canonicalSidecar =
                    canonicalPath +
                    ".wadforge.json";

                string? sidecarToDelete =
                    null;

                if (File.Exists(
                        legacySidecar))
                {
                    if (!File.Exists(
                            canonicalSidecar) ||
                        !FilesMatch(
                            legacySidecar,
                            canonicalSidecar))
                    {
                        throw new InvalidDataException(
                            "The project WAD sidecar did not match the central-library sidecar byte-for-byte.");
                    }

                    sidecarToDelete =
                        legacySidecar;
                }

                candidates.Add(
                    new LegacyDeletionCandidate(
                        Path.GetFullPath(
                            legacyWad),
                        sidecarToDelete));
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    InvalidDataException or
                    InvalidOperationException)
            {
                issues.Add(
                    $"{Path.GetFileName(legacyWad)}: {exception.Message}");
            }
        }

        if (issues.Count >
            0)
        {
            return new CompanionLegacyProjectWadCleanupResult(
                0,
                false,
                issues);
        }

        int deletedWadCount =
            0;

        foreach (LegacyDeletionCandidate candidate in
                 candidates)
        {
            try
            {
                if (File.Exists(
                        candidate.WadPath))
                {
                    File.Delete(
                        candidate.WadPath);

                    deletedWadCount++;
                }

                if (!string.IsNullOrWhiteSpace(
                        candidate.SidecarPath) &&
                    File.Exists(
                        candidate.SidecarPath))
                {
                    File.Delete(
                        candidate.SidecarPath);
                }
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    UnauthorizedAccessException)
            {
                issues.Add(
                    $"{Path.GetFileName(candidate.WadPath)}: verified redundant copy could not be removed: {exception.Message}");
            }
        }

        bool removedLegacyDirectory =
            TryRemoveEmptyDirectory(
                legacyDirectory);

        return new CompanionLegacyProjectWadCleanupResult(
            deletedWadCount,
            removedLegacyDirectory,
            issues);
    }

    private static bool ReferencesMatchSelectedLibraryPaths(
        IReadOnlyList<string> references,
        IReadOnlyList<string> selectedPaths,
        string managedDataRoot,
        CompanionWadLibraryService libraryService)
    {
        if (references.Count !=
            selectedPaths.Count)
        {
            return false;
        }

        for (int index = 0;
             index < references.Count;
             index++)
        {
            string platformReference =
                references[index]
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar)
                    .Replace(
                        '\\',
                        Path.DirectorySeparatorChar);

            if (!Path.IsPathRooted(
                    platformReference))
            {
                return false;
            }

            string fullReference;

            try
            {
                fullReference =
                    Path.GetFullPath(
                        platformReference);
            }
            catch
            {
                return false;
            }

            if (!libraryService.IsLibraryWad(
                    managedDataRoot,
                    fullReference) ||
                !string.Equals(
                    fullReference,
                    Path.GetFullPath(
                        selectedPaths[index]),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool FilesMatch(
        string firstPath,
        string secondPath)
    {
        FileInfo first =
            new(
                firstPath);

        FileInfo second =
            new(
                secondPath);

        return first.Length ==
                second.Length &&
            string.Equals(
                ComputeSha256(
                    firstPath),
                ComputeSha256(
                    secondPath),
                StringComparison.OrdinalIgnoreCase);
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

    private static bool TryRemoveEmptyDirectory(
        string directory)
    {
        if (!Directory.Exists(
                directory))
        {
            return false;
        }

        try
        {
            if (Directory.EnumerateFileSystemEntries(
                    directory)
                .Any())
            {
                return false;
            }

            Directory.Delete(
                directory,
                recursive: false);

            return true;
        }
        catch (Exception exception)
            when (exception is
                IOException or
                UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record LegacyDeletionCandidate(
        string WadPath,
        string? SidecarPath);

    private static CompanionWadLibraryAsset? ResolveReferenceAsset(
        CompanionProjectSession session,
        string fullMapPath,
        string reference,
        string managedDataRoot,
        CompanionWadLibraryService libraryService,
        IDictionary<string, CompanionWadLibraryAsset> byPath,
        IDictionary<string, List<CompanionWadLibraryAsset>> byFileName,
        out bool copiedIntoLibrary)
    {
        copiedIntoLibrary =
            false;

        string? existingPath =
            TryResolveExistingPath(
                fullMapPath,
                reference);

        if (!string.IsNullOrWhiteSpace(
                existingPath))
        {
            string fullExistingPath =
                Path.GetFullPath(
                    existingPath);

            if (libraryService.IsLibraryWad(
                    managedDataRoot,
                    fullExistingPath) &&
                byPath.TryGetValue(
                    fullExistingPath,
                    out CompanionWadLibraryAsset? existingAsset) &&
                existingAsset is not null)
            {
                return existingAsset;
            }

            CompanionWadLibraryImportResult import =
                libraryService.Import(
                    managedDataRoot,
                    fullExistingPath);

            copiedIntoLibrary =
                import.CopiedIntoLibrary;

            CompanionWadLibraryAsset? importedAsset =
                libraryService.FindAsset(
                    managedDataRoot,
                    import.Sha256);

            if (importedAsset is null)
            {
                throw new InvalidDataException(
                    $"Companion imported '{Path.GetFileName(fullExistingPath)}' but could not resolve the canonical library asset.");
            }

            byPath[
                Path.GetFullPath(
                    importedAsset.WadPath)] =
                importedAsset;

            return importedAsset;
        }

        string fileName =
            Path.GetFileName(
                reference
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar)
                    .Replace(
                        '\\',
                        Path.DirectorySeparatorChar));

        if (!string.IsNullOrWhiteSpace(
                fileName) &&
            byFileName.TryGetValue(
                fileName,
                out List<CompanionWadLibraryAsset>? matches) &&
            matches is not null &&
            matches.Count ==
            1)
        {
            return matches[0];
        }

        if (!string.IsNullOrWhiteSpace(
                fileName))
        {
            string legacyCandidate =
                Path.Combine(
                    session.ProjectDirectory,
                    CompanionProjectLayout.WadsDirectoryName,
                    fileName);

            if (File.Exists(
                    legacyCandidate))
            {
                CompanionWadLibraryImportResult import =
                    libraryService.Import(
                        managedDataRoot,
                        legacyCandidate);

                copiedIntoLibrary =
                    import.CopiedIntoLibrary;

                return libraryService.FindAsset(
                    managedDataRoot,
                    import.Sha256);
            }
        }

        return null;
    }

    private static string? TryResolveExistingPath(
        string fullMapPath,
        string reference)
    {
        string platformReference =
            reference
                .Replace(
                    '/',
                    Path.DirectorySeparatorChar)
                .Replace(
                    '\\',
                    Path.DirectorySeparatorChar);

        try
        {
            if (Path.IsPathRooted(
                    platformReference))
            {
                string fullPath =
                    Path.GetFullPath(
                        platformReference);

                return File.Exists(
                        fullPath)
                    ? fullPath
                    : null;
            }

            string? mapDirectory =
                Path.GetDirectoryName(
                    fullMapPath);

            if (string.IsNullOrWhiteSpace(
                    mapDirectory))
            {
                return null;
            }

            string candidate =
                Path.GetFullPath(
                    Path.Combine(
                        mapDirectory,
                        platformReference));

            return File.Exists(
                    candidate)
                ? candidate
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static CompanionProjectMap GetMap(
        CompanionProjectSession session,
        string mapPath)
    {
        string relativePath =
            CompanionProjectStore.MakeRelativeMapPath(
                session.ProjectFilePath,
                mapPath);

        CompanionProjectMap? map =
            session.Project.Maps.FirstOrDefault(
                candidate =>
                    string.Equals(
                        candidate.Path,
                        relativePath,
                        StringComparison.OrdinalIgnoreCase));

        return map ??
            throw new InvalidOperationException(
                $"Map '{relativePath}' is not registered in this Companion project.");
    }
}

public sealed record CompanionLegacyProjectWadCleanupResult(
    int DeletedWadCount,
    bool RemovedLegacyDirectory,
    IReadOnlyList<string> Issues)
{
    public bool HasIssues =>
        Issues.Count >
        0;
}

public sealed record CompanionProjectWadLibraryReconciliationResult(
    int ImportedToLibraryCount,
    bool SelectionChanged,
    bool MapChanged,
    IReadOnlyList<string> Issues)
{
    public bool Changed =>
        SelectionChanged ||
        MapChanged;

    public bool HasIssues =>
        Issues.Count >
        0;
}
