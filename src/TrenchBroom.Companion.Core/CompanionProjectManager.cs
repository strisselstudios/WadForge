using System.IO;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionProjectManager
{
    public CompanionProjectSession Create(
        string projectDirectory,
        string projectName,
        string gameId,
        string? modName = null)
    {
        string directory =
            NormalizeDirectory(projectDirectory);

        Directory.CreateDirectory(directory);

        string projectFilePath =
            Path.Combine(
                directory,
                BuildProjectFileName(projectName));

        if (File.Exists(projectFilePath))
        {
            throw new IOException(
                $"A Companion project already exists at '{projectFilePath}'.");
        }

        CompanionProjectManifest project =
            CompanionProjectStore.Create(
                projectName,
                gameId,
                modName);

        CompanionProjectStore.Save(
            projectFilePath,
            project);

        return new CompanionProjectSession(
            projectFilePath,
            project);
    }

    public CompanionProjectSession CreateForExistingMap(
        string mapFilePath,
        string projectName,
        string gameId,
        string? modName = null)
    {
        string fullMapPath =
            ValidateExistingMapPath(mapFilePath);

        string? projectDirectory =
            Path.GetDirectoryName(fullMapPath);

        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            throw new InvalidDataException(
                "Existing map must have a parent directory.");
        }

        string projectFilePath =
            Path.Combine(
                projectDirectory,
                BuildProjectFileName(projectName));

        if (File.Exists(projectFilePath))
        {
            throw new IOException(
                $"A Companion project already exists at '{projectFilePath}'.");
        }

        CompanionProjectManifest project =
            CompanionProjectStore.Create(
                projectName,
                gameId,
                modName);

        CompanionProjectStore.AddMap(
            project,
            projectFilePath,
            fullMapPath,
            makeActive: true);

        CompanionProjectStore.Save(
            projectFilePath,
            project);

        return new CompanionProjectSession(
            projectFilePath,
            project);
    }

    public CompanionProjectSession Open(
        string projectFilePath)
    {
        string fullProjectPath =
            Path.GetFullPath(
                projectFilePath ??
                throw new ArgumentNullException(nameof(projectFilePath)));

        CompanionProjectManifest project =
            CompanionProjectStore.Load(
                fullProjectPath);

        return new CompanionProjectSession(
            fullProjectPath,
            project);
    }

    public static string BuildProjectFileName(
        string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new ArgumentException(
                "Project name cannot be empty.",
                nameof(projectName));
        }

        HashSet<char> invalidCharacters =
            new(Path.GetInvalidFileNameChars());

        string sanitized =
            new(
                projectName
                    .Trim()
                    .Select(character =>
                        invalidCharacters.Contains(character)
                            ? '_'
                            : character)
                    .ToArray());

        sanitized =
            sanitized.Trim()
                .TrimEnd('.', ' ');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Project";
        }

        if (IsReservedWindowsFileName(sanitized))
        {
            sanitized += "_Project";
        }

        return sanitized +
            CompanionProjectStore.ProjectExtension;
    }

    private static string NormalizeDirectory(
        string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            throw new ArgumentException(
                "Project directory cannot be empty.",
                nameof(projectDirectory));
        }

        return Path.GetFullPath(
            projectDirectory.Trim());
    }

    private static string ValidateExistingMapPath(
        string mapFilePath)
    {
        if (string.IsNullOrWhiteSpace(mapFilePath))
        {
            throw new ArgumentException(
                "Map file path cannot be empty.",
                nameof(mapFilePath));
        }

        string fullMapPath =
            Path.GetFullPath(mapFilePath);

        if (!string.Equals(
                Path.GetExtension(fullMapPath),
                ".map",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Existing map must use the .map extension.",
                nameof(mapFilePath));
        }

        if (!File.Exists(fullMapPath))
        {
            throw new FileNotFoundException(
                "Existing map file was not found.",
                fullMapPath);
        }

        return fullMapPath;
    }

    private static bool IsReservedWindowsFileName(
        string fileName)
    {
        string baseName =
            Path.GetFileNameWithoutExtension(fileName)
                .TrimEnd('.', ' ')
                .ToUpperInvariant();

        if (baseName is
            "CON" or
            "PRN" or
            "AUX" or
            "NUL")
        {
            return true;
        }

        if (baseName.Length == 4)
        {
            string prefix =
                baseName[..3];

            char suffix =
                baseName[3];

            if ((prefix == "COM" || prefix == "LPT") &&
                suffix is >= '1' and <= '9')
            {
                return true;
            }
        }

        return false;
    }
}
