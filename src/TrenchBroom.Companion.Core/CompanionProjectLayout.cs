using System.IO;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionProjectLayout
{
    public const string WorkspaceDirectoryName =
        "TrenchBroom-Companion-Projects";

    public const string MapsDirectoryName =
        "maps";

    public const string WadsDirectoryName =
        "wads";

    public const string SkyboxesDirectoryName =
        "skyboxes";

    public const string BuildDirectoryName =
        "build";

    public const string BackupsDirectoryName =
        "backups";

    public string GetWorkspaceRootForDrive(
        string selectedDrivePath)
    {
        if (string.IsNullOrWhiteSpace(
                selectedDrivePath))
        {
            throw new ArgumentException(
                "A drive must be selected.",
                nameof(selectedDrivePath));
        }

        string fullPath =
            Path.GetFullPath(
                selectedDrivePath.Trim());

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
                WorkspaceDirectoryName));
    }

    public string GetProjectDirectory(
        string workspaceRoot,
        string projectName)
    {
        if (string.IsNullOrWhiteSpace(
                workspaceRoot))
        {
            throw new ArgumentException(
                "Workspace root cannot be empty.",
                nameof(workspaceRoot));
        }

        return Path.GetFullPath(
            Path.Combine(
                workspaceRoot,
                SanitizeProjectDirectoryName(
                    projectName)));
    }

    public string GetRuntimeModDirectory(
        CompanionGameProfile profile,
        string gameInstallationDirectory,
        string projectName)
    {
        ArgumentNullException.ThrowIfNull(
            profile);

        return Path.GetFullPath(
            Path.Combine(
                profile.GetRuntimeModsRoot(
                    gameInstallationDirectory),
                SanitizeProjectDirectoryName(
                    projectName)));
    }

    public static string SanitizeProjectDirectoryName(
        string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new ArgumentException(
                "Project name cannot be empty.",
                nameof(projectName));
        }

        HashSet<char> invalidCharacters =
            new(
                Path.GetInvalidFileNameChars());

        char[] characters =
            projectName
                .Trim()
                .Select(
                    character =>
                        invalidCharacters.Contains(character)
                            ? '_'
                            : character)
                .ToArray();

        string sanitized =
            new string(characters)
                .Trim()
                .TrimEnd(
                    '.',
                    ' ');

        if (string.IsNullOrWhiteSpace(
                sanitized))
        {
            sanitized =
                "Project";
        }

        if (IsReservedWindowsName(
                sanitized))
        {
            sanitized +=
                "_Project";
        }

        return sanitized;
    }

    private static bool IsReservedWindowsName(
        string value)
    {
        string name =
            Path.GetFileNameWithoutExtension(
                value)
                .TrimEnd(
                    '.',
                    ' ')
                .ToUpperInvariant();

        if (name is
            "CON" or
            "PRN" or
            "AUX" or
            "NUL")
        {
            return true;
        }

        if (name.Length != 4)
        {
            return false;
        }

        string prefix =
            name[..3];

        char suffix =
            name[3];

        return
            (prefix == "COM" ||
             prefix == "LPT") &&
            suffix is >= '1' and <= '9';
    }
}
