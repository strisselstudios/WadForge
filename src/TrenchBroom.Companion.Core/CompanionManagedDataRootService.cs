using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TrenchBroom.Companion.Core;

public static class CompanionManagedDataRootService
{
    public const string DataDirectoryName =
        "TrenchBroom-Companion-Data";

    public const string ToolsDirectoryName =
        "Tools";

    public const string CompilersDirectoryName =
        "Compilers";

    public const string GameResourcesDirectoryName =
        "GameResources";

    public static bool TryInitializeFromExistingWorkspace(
        CompanionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(
            settings);

        if (TryGetConfiguredRoot(
                settings,
                out _))
        {
            return false;
        }

        List<string> candidates =
            new();

        foreach (DriveInfo drive in
                 DriveInfo.GetDrives()
                     .Where(
                         drive =>
                             drive.IsReady &&
                             drive.DriveType is
                                 DriveType.Fixed or
                                 DriveType.Removable)
                     .OrderBy(
                         drive =>
                             drive.Name,
                         StringComparer.OrdinalIgnoreCase))
        {
            string workspaceRoot =
                Path.Combine(
                    drive.RootDirectory.FullName,
                    CompanionProjectLayout.WorkspaceDirectoryName);

            if (!Directory.Exists(
                    workspaceRoot))
            {
                continue;
            }

            candidates.Add(
                GetDataRootForDrive(
                    drive.RootDirectory.FullName));
        }

        if (candidates.Count != 1)
        {
            return false;
        }

        settings.ManagedDataRootPath =
            candidates[0];

        EnsureLayout(
            candidates[0]);

        TryCopyLegacyManagedAssets(
            candidates[0]);

        return true;
    }

    public static bool EnsureConfiguredForProject(
        CompanionSettings settings,
        string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(
            settings);

        if (TryGetConfiguredRoot(
                settings,
                out _))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(
                projectDirectory))
        {
            throw new ArgumentException(
                "A project directory is required.",
                nameof(projectDirectory));
        }

        string projectPath =
            Path.GetFullPath(
                projectDirectory);

        string? driveRoot =
            Path.GetPathRoot(
                projectPath);

        if (string.IsNullOrWhiteSpace(
                driveRoot))
        {
            throw new InvalidDataException(
                $"Could not determine a drive for '{projectDirectory}'.");
        }

        string managedRoot =
            GetDataRootForDrive(
                driveRoot);

        settings.ManagedDataRootPath =
            managedRoot;

        EnsureLayout(
            managedRoot);

        TryCopyLegacyManagedAssets(
            managedRoot);

        return true;
    }

    public static bool TryGetConfiguredRoot(
        CompanionSettings settings,
        out string managedDataRoot)
    {
        ArgumentNullException.ThrowIfNull(
            settings);

        managedDataRoot =
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                settings.ManagedDataRootPath))
        {
            return false;
        }

        try
        {
            string root =
                Path.GetFullPath(
                    settings.ManagedDataRootPath);

            EnsureLayout(
                root);

            managedDataRoot =
                root;

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string GetRequiredRoot(
        CompanionSettings settings)
    {
        if (!TryGetConfiguredRoot(
                settings,
                out string managedDataRoot))
        {
            throw new InvalidOperationException(
                "Companion managed storage has not been configured yet. " +
                "Create or open a Companion project first so its drive can be used.");
        }

        return managedDataRoot;
    }

    public static string GetDataRootForDrive(
        string selectedDrivePath)
    {
        if (string.IsNullOrWhiteSpace(
                selectedDrivePath))
        {
            throw new ArgumentException(
                "A drive path is required.",
                nameof(selectedDrivePath));
        }

        string fullPath =
            Path.GetFullPath(
                selectedDrivePath);

        string? driveRoot =
            Path.GetPathRoot(
                fullPath);

        if (string.IsNullOrWhiteSpace(
                driveRoot))
        {
            throw new InvalidDataException(
                $"Could not determine the drive root for '{selectedDrivePath}'.");
        }

        return Path.GetFullPath(
            Path.Combine(
                driveRoot,
                DataDirectoryName));
    }

    public static string GetTrenchBroomDirectory(
        string managedDataRoot)
    {
        return Path.Combine(
            Path.GetFullPath(
                managedDataRoot),
            ToolsDirectoryName,
            "TrenchBroom");
    }

    public static string GetTrenchBroomExecutablePath(
        string managedDataRoot)
    {
        return Path.Combine(
            GetTrenchBroomDirectory(
                managedDataRoot),
            "TrenchBroom.exe");
    }

    public static string GetCompilersDirectory(
        string managedDataRoot)
    {
        return Path.Combine(
            Path.GetFullPath(
                managedDataRoot),
            CompilersDirectoryName);
    }

    public static string GetDuskAuthoringDirectory(
        string managedDataRoot)
    {
        return Path.Combine(
            Path.GetFullPath(
                managedDataRoot),
            GameResourcesDirectoryName,
            "DUSK",
            "Authoring");
    }

    public static string GetRootFromManagedTrenchBroomExecutable(
        string trenchBroomExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(
                trenchBroomExecutablePath))
        {
            throw new ArgumentException(
                "A TrenchBroom executable path is required.",
                nameof(trenchBroomExecutablePath));
        }

        string executablePath =
            Path.GetFullPath(
                trenchBroomExecutablePath);

        string trenchBroomDirectory =
            Path.GetDirectoryName(
                executablePath) ??
            throw new InvalidDataException(
                "Could not determine the managed TrenchBroom directory.");

        string toolsDirectory =
            Path.GetDirectoryName(
                trenchBroomDirectory) ??
            throw new InvalidDataException(
                "Could not determine the managed TrenchBroom tools directory.");

        string managedDataRoot =
            Path.GetDirectoryName(
                toolsDirectory) ??
            throw new InvalidDataException(
                "Could not determine the Companion managed data root.");

        string expectedDirectory =
            GetTrenchBroomDirectory(
                managedDataRoot);

        if (!string.Equals(
                Path.GetFullPath(
                    trenchBroomDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                Path.GetFullPath(
                    expectedDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The TrenchBroom executable is not inside Companion's managed Tools\\TrenchBroom directory.");
        }

        return Path.GetFullPath(
            managedDataRoot);
    }

    private static void TryCopyLegacyManagedAssets(
        string managedDataRoot)
    {
        string legacyRoot =
            CompanionSettingsStore.SettingsDirectory;

        TryCopyLegacyDirectory(
            Path.Combine(
                legacyRoot,
                "TrenchBroom"),
            GetTrenchBroomDirectory(
                managedDataRoot));

        TryCopyLegacyDirectory(
            Path.Combine(
                legacyRoot,
                "DUSK-Authoring"),
            GetDuskAuthoringDirectory(
                managedDataRoot));
    }

    private static void TryCopyLegacyDirectory(
        string sourceDirectory,
        string destinationDirectory)
    {
        if (!Directory.Exists(
                sourceDirectory) ||
            Directory.Exists(
                destinationDirectory))
        {
            return;
        }

        try
        {
            CopyDirectory(
                sourceDirectory,
                destinationDirectory);
        }
        catch
        {
            // Migration is best-effort. The legacy development copy is
            // intentionally left untouched and normal provisioning/import
            // can rebuild the managed destination if needed.
        }
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string destinationDirectory)
    {
        Directory.CreateDirectory(
            destinationDirectory);

        EnumerationOptions options =
            new()
            {
                RecurseSubdirectories =
                    true,

                IgnoreInaccessible =
                    false,

                ReturnSpecialDirectories =
                    false,

                AttributesToSkip =
                    FileAttributes.ReparsePoint
            };

        foreach (string sourceFile in
                 Directory.EnumerateFiles(
                     sourceDirectory,
                     "*",
                     options))
        {
            string relativePath =
                Path.GetRelativePath(
                    sourceDirectory,
                    sourceFile);

            string destinationFile =
                Path.Combine(
                    destinationDirectory,
                    relativePath);

            string? destinationParent =
                Path.GetDirectoryName(
                    destinationFile);

            if (!string.IsNullOrWhiteSpace(
                    destinationParent))
            {
                Directory.CreateDirectory(
                    destinationParent);
            }

            File.Copy(
                sourceFile,
                destinationFile,
                overwrite:
                    true);
        }
    }

    private static void EnsureLayout(
        string managedDataRoot)
    {
        string root =
            Path.GetFullPath(
                managedDataRoot);

        Directory.CreateDirectory(
            root);

        Directory.CreateDirectory(
            Path.Combine(
                root,
                ToolsDirectoryName));

        Directory.CreateDirectory(
            Path.Combine(
                root,
                CompilersDirectoryName));

        Directory.CreateDirectory(
            Path.Combine(
                root,
                GameResourcesDirectoryName));
    }
}
