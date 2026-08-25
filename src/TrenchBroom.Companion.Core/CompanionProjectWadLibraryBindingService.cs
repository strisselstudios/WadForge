using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
