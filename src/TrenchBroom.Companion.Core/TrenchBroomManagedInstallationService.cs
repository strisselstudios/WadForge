using System.IO;

namespace TrenchBroom.Companion.Core;

public sealed record TrenchBroomManagedInstallationResult(
    TrenchBroomInstallationInfo Installation,
    string SourceDirectory,
    string ManagedDirectory);

public static class TrenchBroomManagedInstallationService
{
    public static string DefaultManagedExecutablePath =>
        TrenchBroomInstallationResolver
            .GetDefaultManagedExecutablePath();

    public static string DefaultManagedDirectory =>
        Path.GetDirectoryName(
            DefaultManagedExecutablePath) ??
        throw new InvalidOperationException(
            "Could not determine the managed TrenchBroom directory.");

    public static TrenchBroomManagedInstallationResult Provision(
        string sourceExecutablePath)
    {
        return Provision(
            sourceExecutablePath,
            DefaultManagedExecutablePath);
    }

    public static TrenchBroomManagedInstallationResult Provision(
        string sourceExecutablePath,
        string managedExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(
                sourceExecutablePath))
        {
            throw new ArgumentException(
                "A compatible TrenchBroom source executable is required.",
                nameof(sourceExecutablePath));
        }

        if (string.IsNullOrWhiteSpace(
                managedExecutablePath))
        {
            throw new ArgumentException(
                "A managed TrenchBroom destination is required.",
                nameof(managedExecutablePath));
        }

        TrenchBroomInstallationInfo sourceInstallation =
            TrenchBroomInstallationService.Inspect(
                sourceExecutablePath);

        if (!sourceInstallation.IsValid)
        {
            throw new InvalidDataException(
                "The source TrenchBroom installation is invalid: " +
                sourceInstallation.Status);
        }

        if (!sourceInstallation.IsWadForgeCompatible)
        {
            throw new InvalidDataException(
                "The source TrenchBroom installation is not Companion-compatible. " +
                "The existing standard TrenchBroom installation was not modified.");
        }

        string sourceExecutable =
            Path.GetFullPath(
                sourceInstallation.ExecutablePath);

        string sourceDirectory =
            Path.GetDirectoryName(
                sourceExecutable) ??
            throw new InvalidDataException(
                "Could not determine the source TrenchBroom directory.");

        string destinationExecutable =
            Path.GetFullPath(
                managedExecutablePath);

        string destinationDirectory =
            Path.GetDirectoryName(
                destinationExecutable) ??
            throw new InvalidDataException(
                "Could not determine the managed TrenchBroom directory.");

        if (PathsEqual(
                sourceExecutable,
                destinationExecutable))
        {
            return new TrenchBroomManagedInstallationResult(
                sourceInstallation,
                sourceDirectory,
                destinationDirectory);
        }

        EnsureDestinationIsOutsideSource(
            sourceDirectory,
            destinationDirectory);

        string destinationParent =
            Path.GetDirectoryName(
                destinationDirectory) ??
            throw new InvalidDataException(
                "Could not determine the managed TrenchBroom parent directory.");

        Directory.CreateDirectory(
            destinationParent);

        string stagingDirectory =
            destinationDirectory +
            ".staging-" +
            Guid.NewGuid().ToString("N");

        string backupDirectory =
            destinationDirectory +
            ".backup-" +
            Guid.NewGuid().ToString("N");

        bool destinationBackedUp =
            false;

        try
        {
            CopyDirectory(
                sourceDirectory,
                stagingDirectory);

            string sourceFileName =
                Path.GetFileName(
                    sourceExecutable);

            string stagedSourceExecutable =
                Path.Combine(
                    stagingDirectory,
                    sourceFileName);

            string stagedManagedExecutable =
                Path.Combine(
                    stagingDirectory,
                    Path.GetFileName(
                        destinationExecutable));

            if (!PathsEqual(
                    stagedSourceExecutable,
                    stagedManagedExecutable))
            {
                File.Copy(
                    stagedSourceExecutable,
                    stagedManagedExecutable,
                    overwrite: true);
            }

            TrenchBroomInstallationInfo stagedInstallation =
                TrenchBroomInstallationService.Inspect(
                    stagedManagedExecutable);

            if (!stagedInstallation.IsValid ||
                !stagedInstallation.IsWadForgeCompatible)
            {
                throw new InvalidDataException(
                    "The staged managed TrenchBroom installation failed compatibility validation.");
            }

            if (Directory.Exists(
                    destinationDirectory))
            {
                Directory.Move(
                    destinationDirectory,
                    backupDirectory);

                destinationBackedUp =
                    true;
            }

            Directory.Move(
                stagingDirectory,
                destinationDirectory);

            TrenchBroomInstallationInfo installed =
                TrenchBroomInstallationService.Inspect(
                    destinationExecutable);

            if (!installed.IsValid ||
                !installed.IsWadForgeCompatible)
            {
                throw new InvalidDataException(
                    "The managed TrenchBroom installation failed final validation.");
            }

            if (destinationBackedUp &&
                Directory.Exists(
                    backupDirectory))
            {
                Directory.Delete(
                    backupDirectory,
                    recursive: true);

                destinationBackedUp =
                    false;
            }

            return new TrenchBroomManagedInstallationResult(
                installed,
                sourceDirectory,
                destinationDirectory);
        }
        catch
        {
            TryDeleteDirectory(
                stagingDirectory);

            if (destinationBackedUp)
            {
                TryDeleteDirectory(
                    destinationDirectory);

                if (Directory.Exists(
                        backupDirectory))
                {
                    Directory.Move(
                        backupDirectory,
                        destinationDirectory);
                }
            }

            throw;
        }
        finally
        {
            TryDeleteDirectory(
                stagingDirectory);

            if (!destinationBackedUp)
            {
                TryDeleteDirectory(
                    backupDirectory);
            }
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
                overwrite: false);
        }
    }

    private static void EnsureDestinationIsOutsideSource(
        string sourceDirectory,
        string destinationDirectory)
    {
        string normalizedSource =
            EnsureTrailingSeparator(
                Path.GetFullPath(
                    sourceDirectory));

        string normalizedDestination =
            EnsureTrailingSeparator(
                Path.GetFullPath(
                    destinationDirectory));

        if (normalizedDestination.StartsWith(
                normalizedSource,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The managed TrenchBroom destination cannot be inside the source installation directory.");
        }
    }

    private static string EnsureTrailingSeparator(
        string path)
    {
        if (path.EndsWith(
                Path.DirectorySeparatorChar) ||
            path.EndsWith(
                Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path +
            Path.DirectorySeparatorChar;
    }

    private static bool PathsEqual(
        string first,
        string second)
    {
        return string.Equals(
            Path.GetFullPath(
                first)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
            Path.GetFullPath(
                second)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectory(
        string directory)
    {
        try
        {
            if (Directory.Exists(
                    directory))
            {
                Directory.Delete(
                    directory,
                    recursive: true);
            }
        }
        catch
        {
            // Cleanup is best-effort. The original provisioning result wins.
        }
    }
}
