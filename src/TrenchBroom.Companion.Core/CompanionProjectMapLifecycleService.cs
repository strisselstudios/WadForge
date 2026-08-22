using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionProjectMapLifecycleService
{
    public CompanionProjectMapReconcileResult ReconcileMissingMaps(
        CompanionProjectSession session)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        List<CompanionProjectMap> previousMaps =
            CloneMaps(
                session.Project.Maps);

        string? previousActiveMap =
            session.Project.ActiveMapPath;

        List<CompanionProjectMap> survivingMaps =
            new();

        List<string> removedDisplayNames =
            new();

        foreach (CompanionProjectMap map in
                 session.Project.Maps)
        {
            string fullPath;

            try
            {
                fullPath =
                    CompanionProjectStore.ResolveMapPath(
                        session.ProjectFilePath,
                        map.Path);
            }
            catch
            {
                removedDisplayNames.Add(
                    GetDisplayName(
                        map));

                continue;
            }

            if (!File.Exists(
                    fullPath))
            {
                removedDisplayNames.Add(
                    GetDisplayName(
                        map));

                continue;
            }

            survivingMaps.Add(
                CloneMap(
                    map));
        }

        if (removedDisplayNames.Count == 0)
        {
            return new CompanionProjectMapReconcileResult(
                0,
                Array.Empty<string>(),
                session.Project.ActiveMapPath);
        }

        session.Project.Maps =
            survivingMaps;

        RepairActiveMap(
            session.Project);

        try
        {
            session.Save();
        }
        catch
        {
            session.Project.Maps =
                previousMaps;

            session.Project.ActiveMapPath =
                previousActiveMap;

            throw;
        }

        return new CompanionProjectMapReconcileResult(
            removedDisplayNames.Count,
            removedDisplayNames,
            session.Project.ActiveMapPath);
    }

    public CompanionProjectMapRemovalResult RemoveFromProject(
        CompanionProjectSession session,
        string mapFilePath)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        CompanionProjectMap selectedMap =
            GetRegisteredMap(
                session,
                mapFilePath);

        string resolvedPath =
            CompanionProjectStore.ResolveMapPath(
                session.ProjectFilePath,
                selectedMap.Path);

        List<CompanionProjectMap> previousMaps =
            CloneMaps(
                session.Project.Maps);

        string? previousActiveMap =
            session.Project.ActiveMapPath;

        RemoveRegistration(
            session.Project,
            selectedMap.Path);

        try
        {
            session.Save();
        }
        catch
        {
            session.Project.Maps =
                previousMaps;

            session.Project.ActiveMapPath =
                previousActiveMap;

            throw;
        }

        return new CompanionProjectMapRemovalResult(
            GetDisplayName(
                selectedMap),
            resolvedPath,
            null,
            false);
    }

    public CompanionProjectMapRemovalResult DeleteMapSafely(
        CompanionProjectSession session,
        string mapFilePath)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        CompanionProjectMap selectedMap =
            GetRegisteredMap(
                session,
                mapFilePath);

        string resolvedPath =
            CompanionProjectStore.ResolveMapPath(
                session.ProjectFilePath,
                selectedMap.Path);

        if (!File.Exists(
                resolvedPath))
        {
            CompanionProjectMapRemovalResult removed =
                RemoveFromProject(
                    session,
                    resolvedPath);

            return new CompanionProjectMapRemovalResult(
                removed.DisplayName,
                removed.OriginalPath,
                null,
                false);
        }

        string backupPath =
            BuildBackupPath(
                session,
                resolvedPath);

        List<CompanionProjectMap> previousMaps =
            CloneMaps(
                session.Project.Maps);

        string? previousActiveMap =
            session.Project.ActiveMapPath;

        Directory.CreateDirectory(
            Path.GetDirectoryName(
                backupPath) ??
            throw new InvalidDataException(
                "Backup path must have a parent directory."));

        bool moved =
            false;

        try
        {
            File.Move(
                resolvedPath,
                backupPath,
                overwrite: false);

            moved =
                true;

            RemoveRegistration(
                session.Project,
                selectedMap.Path);

            session.Save();
        }
        catch
        {
            session.Project.Maps =
                previousMaps;

            session.Project.ActiveMapPath =
                previousActiveMap;

            if (moved &&
                File.Exists(
                    backupPath) &&
                !File.Exists(
                    resolvedPath))
            {
                try
                {
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(
                            resolvedPath) ??
                        session.ProjectDirectory);

                    File.Move(
                        backupPath,
                        resolvedPath,
                        overwrite: false);
                }
                catch
                {
                    throw new IOException(
                        "Deleting the map failed and Companion could not restore the map from its backup. " +
                        $"The backup remains at '{backupPath}'.");
                }
            }

            throw;
        }

        return new CompanionProjectMapRemovalResult(
            GetDisplayName(
                selectedMap),
            resolvedPath,
            backupPath,
            true);
    }

    private static CompanionProjectMap GetRegisteredMap(
        CompanionProjectSession session,
        string mapFilePath)
    {
        if (string.IsNullOrWhiteSpace(
                mapFilePath))
        {
            throw new ArgumentException(
                "Map path cannot be empty.",
                nameof(mapFilePath));
        }

        string relativePath =
            CompanionProjectStore.MakeRelativeMapPath(
                session.ProjectFilePath,
                mapFilePath);

        CompanionProjectMap? map =
            session.Project.Maps
                .FirstOrDefault(
                    candidate =>
                        string.Equals(
                            candidate.Path,
                            relativePath,
                            StringComparison.OrdinalIgnoreCase));

        return map ??
            throw new InvalidOperationException(
                $"Map '{relativePath}' is not registered in this Companion project.");
    }

    private static void RemoveRegistration(
        CompanionProjectManifest project,
        string relativeMapPath)
    {
        int removed =
            project.Maps.RemoveAll(
                map =>
                    string.Equals(
                        map.Path,
                        relativeMapPath,
                        StringComparison.OrdinalIgnoreCase));

        if (removed != 1)
        {
            throw new InvalidOperationException(
                $"Expected one project map registration for '{relativeMapPath}', but removed {removed}.");
        }

        RepairActiveMap(
            project);
    }

    private static void RepairActiveMap(
        CompanionProjectManifest project)
    {
        bool activeStillRegistered =
            !string.IsNullOrWhiteSpace(
                project.ActiveMapPath) &&
            project.Maps.Any(
                map =>
                    string.Equals(
                        map.Path,
                        project.ActiveMapPath,
                        StringComparison.OrdinalIgnoreCase));

        if (activeStillRegistered)
        {
            return;
        }

        project.ActiveMapPath =
            project.Maps.Count > 0
                ? project.Maps[0].Path
                : null;
    }

    private static string BuildBackupPath(
        CompanionProjectSession session,
        string sourceMapPath)
    {
        string backupRoot =
            Path.Combine(
                session.ProjectDirectory,
                CompanionProjectLayout.BackupsDirectoryName,
                "Deleted Maps");

        string timestamp =
            DateTime.Now.ToString(
                "yyyy-MM-dd_HHmmssfff");

        string backupDirectory =
            Path.Combine(
                backupRoot,
                timestamp);

        int suffix =
            1;

        while (Directory.Exists(
                   backupDirectory))
        {
            backupDirectory =
                Path.Combine(
                    backupRoot,
                    timestamp +
                    "-" +
                    suffix.ToString());

            suffix++;
        }

        string backupPath =
            Path.GetFullPath(
                Path.Combine(
                    backupDirectory,
                    Path.GetFileName(
                        sourceMapPath)));

        string projectRoot =
            Path.GetFullPath(
                session.ProjectDirectory);

        string requiredPrefix =
            projectRoot.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? projectRoot
                : projectRoot +
                    Path.DirectorySeparatorChar;

        if (!backupPath.StartsWith(
                requiredPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Map backup path escaped the Companion project directory.");
        }

        return backupPath;
    }

    private static List<CompanionProjectMap> CloneMaps(
        IEnumerable<CompanionProjectMap> maps)
    {
        return maps
            .Select(
                CloneMap)
            .ToList();
    }

    private static CompanionProjectMap CloneMap(
        CompanionProjectMap map)
    {
        return new CompanionProjectMap
        {
            Path =
                map.Path,

            DisplayName =
                map.DisplayName
        };
    }

    private static string GetDisplayName(
        CompanionProjectMap map)
    {
        return string.IsNullOrWhiteSpace(
                map.DisplayName)
            ? Path.GetFileNameWithoutExtension(
                map.Path)
            : map.DisplayName;
    }
}

public sealed record CompanionProjectMapReconcileResult(
    int RemovedCount,
    IReadOnlyList<string> RemovedDisplayNames,
    string? ActiveMapPath);

public sealed record CompanionProjectMapRemovalResult(
    string DisplayName,
    string OriginalPath,
    string? BackupPath,
    bool FileMovedToBackup);
