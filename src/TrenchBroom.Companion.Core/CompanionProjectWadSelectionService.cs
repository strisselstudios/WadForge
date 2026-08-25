using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionProjectWadSelectionService
{
    public IReadOnlyList<CompanionWadLibraryAsset> GetSelectableAssets(
        CompanionProjectManifest project,
        string managedDataRoot,
        CompanionWadLibraryService libraryService,
        IEnumerable<string>? alwaysIncludeAssetIds = null)
    {
        ArgumentNullException.ThrowIfNull(
            project);

        ArgumentNullException.ThrowIfNull(
            libraryService);

        return libraryService
            .GetAssets(
                managedDataRoot)
            .Where(
                asset =>
                    ProjectSupportsAsset(
                        project,
                        asset))
            .OrderBy(
                asset =>
                    asset.WadFormat,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                asset =>
                    asset.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public CompanionProjectMap GetMap(
        CompanionProjectSession session,
        string mapPath)
    {
        ArgumentNullException.ThrowIfNull(
            session);

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

    public string GetPreferredWadFormat(
        CompanionProjectManifest project)
    {
        ArgumentNullException.ThrowIfNull(
            project);

        string format =
            string.IsNullOrWhiteSpace(
                project.PreferredTextureArchiveFormat)
                ? CompanionGameProfiles
                    .GetRequired(
                        project.GameId)
                    .DefaultTextureArchiveFormat
                : CompanionTextureArchiveFormats.Normalize(
                    project.PreferredTextureArchiveFormat);

        return string.Equals(
            format,
            CompanionTextureArchiveFormats.Wad3,
            StringComparison.OrdinalIgnoreCase)
            ? "WAD3"
            : "WAD2";
    }

    public void ApplyProjectDefaultsToMap(
        CompanionProjectSession session,
        string mapPath)
    {
        CompanionProjectMap map =
            GetMap(
                session,
                mapPath);

        if (map.WadAssetIds.Count >
            0)
        {
            return;
        }

        map.WadAssetIds =
            session.Project
                .DefaultWadAssetIds
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        session.Save();
    }

    public CompanionProjectWadSelectionResult SetMapSelection(
        CompanionProjectSession session,
        string mapPath,
        IEnumerable<string> selectedAssetIds,
        bool useAsProjectDefault,
        string managedDataRoot,
        CompanionWadLibraryService libraryService,
        CompanionProjectWadService projectWadService)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        ArgumentNullException.ThrowIfNull(
            selectedAssetIds);

        ArgumentNullException.ThrowIfNull(
            libraryService);

        ArgumentNullException.ThrowIfNull(
            projectWadService);

        CompanionProjectMap map =
            GetMap(
                session,
                mapPath);

        IReadOnlyList<CompanionWadLibraryAsset> library =
            libraryService.GetAssets(
                managedDataRoot);

        Dictionary<string, CompanionWadLibraryAsset> byId =
            library.ToDictionary(
                asset =>
                    asset.AssetId,
                StringComparer.OrdinalIgnoreCase);

        List<string> normalizedIds =
            new();

        List<string> selectedPaths =
            new();

        foreach (string rawAssetId in
                 selectedAssetIds)
        {
            string assetId =
                NormalizeAssetId(
                    rawAssetId);

            if (normalizedIds.Contains(
                    assetId,
                    StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!byId.TryGetValue(
                    assetId,
                    out CompanionWadLibraryAsset? asset) ||
                asset is null)
            {
                throw new InvalidOperationException(
                    $"Selected WAD asset '{assetId}' is no longer in the Companion library.");
            }

            if (!ProjectSupportsAsset(
                    session.Project,
                    asset))
            {
                throw new InvalidOperationException(
                    $"{asset.DisplayName} ({asset.WadFormat}) is not supported by this project's game profile.");
            }

            normalizedIds.Add(
                assetId);

            selectedPaths.Add(
                asset.WadPath);
        }

        map.WadAssetIds =
            normalizedIds;

        if (useAsProjectDefault)
        {
            session.Project.DefaultWadAssetIds =
                normalizedIds.ToList();
        }

        session.Save();

        CompanionProjectWadSyncResult sync =
            projectWadService.SynchronizeMapWorldspawnWads(
                mapPath,
                selectedPaths);

        return new CompanionProjectWadSelectionResult(
            normalizedIds.Count,
            useAsProjectDefault,
            sync.Changed,
            selectedPaths);
    }

    public CompanionProjectWadSelectionMigrationResult MigrateLegacyProjectSelections(
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

        if (session.Project.WadSelectionMigrationCompleted)
        {
            return new CompanionProjectWadSelectionMigrationResult(
                0,
                0,
                false,
                Array.Empty<string>());
        }

        session.Project.DefaultWadAssetIds ??=
            new List<string>();

        session.Project.Maps ??=
            new List<CompanionProjectMap>();

        foreach (CompanionProjectMap map in
                 session.Project.Maps)
        {
            map.WadAssetIds ??=
                new List<string>();
        }

        bool alreadyHasB2ASelections =
            session.Project.DefaultWadAssetIds.Count >
                0 ||
            session.Project.Maps.Any(
                map =>
                    map.WadAssetIds.Count >
                    0);

        if (alreadyHasB2ASelections)
        {
            session.Project.WadSelectionMigrationCompleted =
                true;

            session.Save();

            return new CompanionProjectWadSelectionMigrationResult(
                0,
                0,
                true,
                Array.Empty<string>());
        }

        bool changed =
            false;

        int importedToLibrary =
            0;

        int mapsInitialized =
            0;

        List<string> issues =
            new();

        IReadOnlyList<string> legacyProjectWads =
            projectWadService.GetProjectWadPaths(
                session);

        foreach (string legacyWad in
                 legacyProjectWads)
        {
            try
            {
                CompanionWadLibraryImportResult import =
                    libraryService.Import(
                        managedDataRoot,
                        legacyWad);

                if (import.CopiedIntoLibrary)
                {
                    importedToLibrary++;
                }
            }
            catch (Exception exception)
            {
                issues.Add(
                    $"{Path.GetFileName(legacyWad)}: {exception.Message}");
            }
        }

        IReadOnlyList<CompanionWadLibraryAsset> libraryAssets =
            libraryService.GetAssets(
                managedDataRoot);

        Dictionary<string, List<CompanionWadLibraryAsset>> assetsByName =
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

        Dictionary<string, CompanionWadLibraryAsset> assetsById =
            libraryAssets.ToDictionary(
                asset =>
                    asset.AssetId,
                StringComparer.OrdinalIgnoreCase);

        foreach (CompanionProjectMap map in
                 session.Project.Maps)
        {
            if (map.WadAssetIds.Count >
                0)
            {
                continue;
            }

            string fullMapPath =
                CompanionProjectStore.ResolveMapPath(
                    session.ProjectFilePath,
                    map.Path);

            if (!File.Exists(
                    fullMapPath))
            {
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
                    $"{map.DisplayName}: {exception.Message}");

                continue;
            }

            List<string> selected =
                new();

            foreach (string reference in
                     references)
            {
                CompanionWadLibraryAsset? asset =
                    TryResolveReference(
                        reference,
                        fullMapPath,
                        legacyProjectWads,
                        assetsByName,
                        assetsById,
                        managedDataRoot,
                        libraryService,
                        out bool imported);

                if (imported)
                {
                    importedToLibrary++;
                }

                if (asset is null)
                {
                    issues.Add(
                        $"{map.DisplayName}: could not match '{reference}' to the central WAD library.");

                    continue;
                }

                if (!ProjectSupportsAsset(
                        session.Project,
                        asset))
                {
                    issues.Add(
                        $"{map.DisplayName}: {asset.DisplayName} ({asset.WadFormat}) is not supported by {session.Project.GameId}.");

                    continue;
                }

                if (!selected.Contains(
                        asset.AssetId,
                        StringComparer.OrdinalIgnoreCase))
                {
                    selected.Add(
                        asset.AssetId);
                }
            }

            if (selected.Count ==
                0)
            {
                continue;
            }

            map.WadAssetIds =
                selected;

            mapsInitialized++;
            changed =
                true;
        }

        if (session.Project.DefaultWadAssetIds.Count ==
            0)
        {
            CompanionProjectMap? defaultSource =
                null;

            if (!string.IsNullOrWhiteSpace(
                    session.Project.ActiveMapPath))
            {
                defaultSource =
                    session.Project.Maps.FirstOrDefault(
                        map =>
                            string.Equals(
                                map.Path,
                                session.Project.ActiveMapPath,
                                StringComparison.OrdinalIgnoreCase));
            }

            defaultSource ??=
                session.Project.Maps.FirstOrDefault(
                    map =>
                        map.WadAssetIds.Count >
                        0);

            if (defaultSource is not null &&
                defaultSource.WadAssetIds.Count >
                0)
            {
                session.Project.DefaultWadAssetIds =
                    defaultSource.WadAssetIds.ToList();

                changed =
                    true;
            }
        }

        session.Project.WadSelectionMigrationCompleted =
            true;

        changed =
            true;

        session.Save();

        return new CompanionProjectWadSelectionMigrationResult(
            importedToLibrary,
            mapsInitialized,
            changed,
            issues);
    }

    private static CompanionWadLibraryAsset? TryResolveReference(
        string reference,
        string mapPath,
        IReadOnlyList<string> legacyProjectWads,
        IReadOnlyDictionary<string, List<CompanionWadLibraryAsset>> assetsByName,
        IDictionary<string, CompanionWadLibraryAsset> assetsById,
        string managedDataRoot,
        CompanionWadLibraryService libraryService,
        out bool imported)
    {
        imported =
            false;

        string platformReference =
            reference
                .Replace(
                    '/',
                    Path.DirectorySeparatorChar)
                .Replace(
                    '\\',
                    Path.DirectorySeparatorChar);

        List<string> candidates =
            new();

        try
        {
            if (Path.IsPathRooted(
                    platformReference))
            {
                candidates.Add(
                    Path.GetFullPath(
                        platformReference));
            }
            else
            {
                string? mapDirectory =
                    Path.GetDirectoryName(
                        mapPath);

                if (!string.IsNullOrWhiteSpace(
                        mapDirectory))
                {
                    candidates.Add(
                        Path.GetFullPath(
                            Path.Combine(
                                mapDirectory,
                                platformReference)));
                }
            }
        }
        catch
        {
        }

        string fileName =
            Path.GetFileName(
                platformReference);

        if (!string.IsNullOrWhiteSpace(
                fileName))
        {
            foreach (string legacyPath in
                     legacyProjectWads.Where(
                         path =>
                             string.Equals(
                                 Path.GetFileName(
                                     path),
                                 fileName,
                                 StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(
                    legacyPath);
            }
        }

        foreach (string candidate in
                 candidates
                 .Where(
                     File.Exists)
                 .Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                CompanionWadLibraryImportResult result =
                    libraryService.Import(
                        managedDataRoot,
                        candidate);

                imported |=
                    result.CopiedIntoLibrary;

                if (assetsById.TryGetValue(
                        result.Sha256,
                        out CompanionWadLibraryAsset? knownAsset) &&
                    knownAsset is not null)
                {
                    return knownAsset;
                }

                CompanionWadLibraryAsset? asset =
                    libraryService.FindAsset(
                        managedDataRoot,
                        result.Sha256);

                if (asset is not null)
                {
                    assetsById[
                        asset.AssetId] =
                        asset;

                    return asset;
                }
            }
            catch
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(
                fileName) &&
            assetsByName.TryGetValue(
                fileName,
                out List<CompanionWadLibraryAsset>? matches) &&
            matches is not null &&
            matches.Count ==
            1)
        {
            return matches[0];
        }

        return null;
    }

    private static bool ProjectSupportsAsset(
        CompanionProjectManifest project,
        CompanionWadLibraryAsset asset)
    {
        if (!CompanionGameProfiles.TryGet(
                project.GameId,
                out CompanionGameProfile? profile) ||
            profile is null)
        {
            return false;
        }

        string format =
            string.Equals(
                asset.WadFormat,
                "WAD3",
                StringComparison.OrdinalIgnoreCase)
                ? CompanionTextureArchiveFormats.Wad3
                : CompanionTextureArchiveFormats.Wad2;

        return profile.SupportsTextureArchiveFormat(
            format);
    }

    private static string NormalizeAssetId(
        string assetId)
    {
        if (string.IsNullOrWhiteSpace(
                assetId))
        {
            throw new InvalidDataException(
                "WAD asset ID cannot be empty.");
        }

        string normalized =
            assetId.Trim().ToUpperInvariant();

        if (normalized.Length !=
                64 ||
            normalized.Any(
                character =>
                    !Uri.IsHexDigit(
                        character)))
        {
            throw new InvalidDataException(
                $"Invalid WAD asset ID '{assetId}'.");
        }

        return normalized;
    }
}

public sealed record CompanionProjectWadSelectionResult(
    int SelectedWadCount,
    bool UpdatedProjectDefault,
    bool MapChanged,
    IReadOnlyList<string> WadPaths);

public sealed record CompanionProjectWadSelectionMigrationResult(
    int ImportedToLibraryCount,
    int InitializedMapCount,
    bool ProjectChanged,
    IReadOnlyList<string> Issues);
