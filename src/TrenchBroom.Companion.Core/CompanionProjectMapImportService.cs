using System.IO;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionProjectMapImportService
{
    public IReadOnlyList<string> ImportMaps(
        CompanionProjectSession session,
        IEnumerable<string> sourceMapPaths)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        ArgumentNullException.ThrowIfNull(
            sourceMapPaths);

        List<string> sources =
            sourceMapPaths
                .Where(
                    path =>
                        !string.IsNullOrWhiteSpace(
                            path))
                .Select(
                    path =>
                        Path.GetFullPath(
                            path.Trim()))
                .ToList();

        if (sources.Count == 0)
        {
            throw new ArgumentException(
                "At least one map file must be selected.",
                nameof(sourceMapPaths));
        }

        string mapsDirectory =
            Path.Combine(
                session.ProjectDirectory,
                CompanionProjectLayout.MapsDirectoryName);

        Directory.CreateDirectory(
            mapsDirectory);

        List<ImportPlan> plans =
            BuildImportPlans(
                session,
                mapsDirectory,
                sources);

        List<CompanionProjectMap> previousMaps =
            session.Project.Maps
                .Select(
                    map =>
                        new CompanionProjectMap
                        {
                            Path =
                                map.Path,

                            DisplayName =
                                map.DisplayName
                        })
                .ToList();

        string? previousActiveMap =
            session.Project.ActiveMapPath;

        List<string> createdDestinationFiles =
            new();

        try
        {
            foreach (ImportPlan plan in plans)
            {
                if (!plan.CopyRequired)
                {
                    continue;
                }

                string temporaryPath =
                    plan.DestinationPath +
                    ".tbcompanion-import-" +
                    Guid.NewGuid().ToString("N");

                try
                {
                    File.Copy(
                        plan.SourcePath,
                        temporaryPath,
                        overwrite: false);

                    File.Move(
                        temporaryPath,
                        plan.DestinationPath,
                        overwrite: false);

                    createdDestinationFiles.Add(
                        plan.DestinationPath);
                }
                finally
                {
                    if (File.Exists(
                            temporaryPath))
                    {
                        File.Delete(
                            temporaryPath);
                    }
                }
            }

            foreach (ImportPlan plan in plans)
            {
                session.AddMap(
                    plan.DestinationPath,
                    makeActive: false);
            }

            session.SetActiveMap(
                plans[^1].DestinationPath);

            session.Save();

            return plans
                .Select(
                    plan =>
                        plan.DestinationPath)
                .ToArray();
        }
        catch
        {
            session.Project.Maps =
                previousMaps;

            session.Project.ActiveMapPath =
                previousActiveMap;

            foreach (string createdFile in
                     createdDestinationFiles)
            {
                try
                {
                    if (File.Exists(
                            createdFile))
                    {
                        File.Delete(
                            createdFile);
                    }
                }
                catch
                {
                    // Preserve the original import failure.
                }
            }

            throw;
        }
    }

    private static List<ImportPlan> BuildImportPlans(
        CompanionProjectSession session,
        string mapsDirectory,
        IReadOnlyList<string> sources)
    {
        HashSet<string> sourcePaths =
            new(
                StringComparer.OrdinalIgnoreCase);

        HashSet<string> destinationNames =
            new(
                StringComparer.OrdinalIgnoreCase);

        List<ImportPlan> plans =
            new();

        foreach (string sourcePath in
                 sources)
        {
            if (!File.Exists(
                    sourcePath))
            {
                throw new FileNotFoundException(
                    "Selected map file was not found.",
                    sourcePath);
            }

            if (!string.Equals(
                    Path.GetExtension(
                        sourcePath),
                    ".map",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Only .map files can be imported: '{sourcePath}'.");
            }

            if (!sourcePaths.Add(
                    sourcePath))
            {
                throw new InvalidDataException(
                    $"The same map was selected more than once: '{sourcePath}'.");
            }

            string fileName =
                Path.GetFileName(
                    sourcePath);

            if (!destinationNames.Add(
                    fileName))
            {
                throw new IOException(
                    $"Two selected maps use the same file name '{fileName}'.");
            }

            string destinationPath =
                Path.GetFullPath(
                    Path.Combine(
                        mapsDirectory,
                        fileName));

            bool samePath =
                string.Equals(
                    sourcePath,
                    destinationPath,
                    StringComparison.OrdinalIgnoreCase);

            bool alreadyRegistered =
                session.Project.Maps
                    .Any(
                        map =>
                        {
                            string existingPath =
                                CompanionProjectStore.ResolveMapPath(
                                    session.ProjectFilePath,
                                    map.Path);

                            return string.Equals(
                                existingPath,
                                destinationPath,
                                StringComparison.OrdinalIgnoreCase);
                        });

            if (File.Exists(
                    destinationPath) &&
                !samePath &&
                !alreadyRegistered)
            {
                throw new IOException(
                    $"A file named '{fileName}' already exists in this project's maps folder.");
            }

            if (File.Exists(
                    destinationPath) &&
                !samePath &&
                alreadyRegistered)
            {
                throw new IOException(
                    $"Map '{fileName}' is already part of this project.");
            }

            plans.Add(
                new ImportPlan(
                    sourcePath,
                    destinationPath,
                    copyRequired:
                        !samePath));
        }

        return plans;
    }

    private sealed class ImportPlan
    {
        public ImportPlan(
            string sourcePath,
            string destinationPath,
            bool copyRequired)
        {
            SourcePath =
                sourcePath;

            DestinationPath =
                destinationPath;

            CopyRequired =
                copyRequired;
        }

        public string SourcePath { get; }

        public string DestinationPath { get; }

        public bool CopyRequired { get; }
    }
}
