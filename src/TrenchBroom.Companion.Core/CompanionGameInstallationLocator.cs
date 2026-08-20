using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionGameInstallationLocator
{
    private static readonly Regex SteamLibraryPathPattern =
        new(
            "\"path\"\\s+\"(?<path>[^\"]+)\"",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant);

    public string? FindInstallation(
        CompanionGameProfile gameProfile)
    {
        ArgumentNullException.ThrowIfNull(
            gameProfile);

        return FindInstallations(
                gameProfile)
            .FirstOrDefault();
    }

    public IReadOnlyList<string> FindInstallations(
        CompanionGameProfile gameProfile)
    {
        ArgumentNullException.ThrowIfNull(
            gameProfile);

        return FindInstallations(
            gameProfile,
            EnumerateDefaultSteamRoots());
    }

    public IReadOnlyList<string> FindInstallations(
        CompanionGameProfile gameProfile,
        IEnumerable<string> steamRoots)
    {
        ArgumentNullException.ThrowIfNull(
            gameProfile);

        ArgumentNullException.ThrowIfNull(
            steamRoots);

        HashSet<string> libraryRoots =
            new(
                StringComparer.OrdinalIgnoreCase);

        foreach (string candidateRoot in steamRoots)
        {
            if (string.IsNullOrWhiteSpace(
                    candidateRoot))
            {
                continue;
            }

            string steamRoot;

            try
            {
                steamRoot =
                    Path.GetFullPath(
                        candidateRoot.Trim());
            }
            catch
            {
                continue;
            }

            if (!Directory.Exists(
                    steamRoot))
            {
                continue;
            }

            libraryRoots.Add(
                steamRoot);

            AddLibrariesFromVdf(
                steamRoot,
                libraryRoots);
        }

        List<string> installations =
            new();

        HashSet<string> seenInstallations =
            new(
                StringComparer.OrdinalIgnoreCase);

        foreach (string libraryRoot in
                 libraryRoots)
        {
            string installation =
                Path.GetFullPath(
                    Path.Combine(
                        libraryRoot,
                        "steamapps",
                        "common",
                        gameProfile.SteamCommonDirectoryName));

            if (!Directory.Exists(
                    installation))
            {
                continue;
            }

            if (seenInstallations.Add(
                    installation))
            {
                installations.Add(
                    installation);
            }
        }

        return installations;
    }

    public bool IsInstallationDirectory(
        CompanionGameProfile gameProfile,
        string installationDirectory)
    {
        ArgumentNullException.ThrowIfNull(
            gameProfile);

        if (string.IsNullOrWhiteSpace(
                installationDirectory))
        {
            return false;
        }

        string fullPath;

        try
        {
            fullPath =
                Path.GetFullPath(
                    installationDirectory.Trim());
        }
        catch
        {
            return false;
        }

        if (!Directory.Exists(
                fullPath))
        {
            return false;
        }

        string directoryName =
            new DirectoryInfo(
                fullPath)
                .Name;

        return string.Equals(
            directoryName,
            gameProfile.SteamCommonDirectoryName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void AddLibrariesFromVdf(
        string steamRoot,
        HashSet<string> libraryRoots)
    {
        string libraryFile =
            Path.Combine(
                steamRoot,
                "steamapps",
                "libraryfolders.vdf");

        if (!File.Exists(
                libraryFile))
        {
            return;
        }

        string contents;

        try
        {
            contents =
                File.ReadAllText(
                    libraryFile);
        }
        catch
        {
            return;
        }

        foreach (Match match in
                 SteamLibraryPathPattern.Matches(
                     contents))
        {
            string encodedPath =
                match.Groups["path"].Value;

            string decodedPath =
                encodedPath.Replace(
                    @"\\",
                    @"\");

            if (string.IsNullOrWhiteSpace(
                    decodedPath))
            {
                continue;
            }

            string fullLibraryPath;

            try
            {
                fullLibraryPath =
                    Path.GetFullPath(
                        decodedPath);
            }
            catch
            {
                continue;
            }

            if (Directory.Exists(
                    fullLibraryPath))
            {
                libraryRoots.Add(
                    fullLibraryPath);
            }
        }
    }

    private static IEnumerable<string>
        EnumerateDefaultSteamRoots()
    {
        HashSet<string> roots =
            new(
                StringComparer.OrdinalIgnoreCase);

        AddRegistrySteamRoot(
            roots,
            Registry.CurrentUser,
            @"Software\Valve\Steam",
            "SteamPath");

        AddRegistrySteamRoot(
            roots,
            Registry.LocalMachine,
            @"SOFTWARE\WOW6432Node\Valve\Steam",
            "InstallPath");

        AddRegistrySteamRoot(
            roots,
            Registry.LocalMachine,
            @"SOFTWARE\Valve\Steam",
            "InstallPath");

        string? programFilesX86 =
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86);

        if (!string.IsNullOrWhiteSpace(
                programFilesX86))
        {
            AddIfDirectoryExists(
                roots,
                Path.Combine(
                    programFilesX86,
                    "Steam"));
        }

        string? programFiles =
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles);

        if (!string.IsNullOrWhiteSpace(
                programFiles))
        {
            AddIfDirectoryExists(
                roots,
                Path.Combine(
                    programFiles,
                    "Steam"));
        }

        foreach (DriveInfo drive in
                 DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }

            if (drive.DriveType is not
                DriveType.Fixed and not
                DriveType.Removable)
            {
                continue;
            }

            AddIfDirectoryExists(
                roots,
                Path.Combine(
                    drive.RootDirectory.FullName,
                    "Steam"));

            AddIfDirectoryExists(
                roots,
                Path.Combine(
                    drive.RootDirectory.FullName,
                    "SteamLibrary"));
        }

        return roots;
    }

    private static void AddRegistrySteamRoot(
        HashSet<string> roots,
        RegistryKey baseKey,
        string subKeyPath,
        string valueName)
    {
        try
        {
            using RegistryKey? key =
                baseKey.OpenSubKey(
                    subKeyPath);

            string? value =
                key?.GetValue(
                    valueName)
                as string;

            if (!string.IsNullOrWhiteSpace(
                    value))
            {
                AddIfDirectoryExists(
                    roots,
                    value);
            }
        }
        catch
        {
            // Missing/inaccessible registry entries are not fatal.
        }
    }

    private static void AddIfDirectoryExists(
        HashSet<string> roots,
        string path)
    {
        try
        {
            string fullPath =
                Path.GetFullPath(
                    path);

            if (Directory.Exists(
                    fullPath))
            {
                roots.Add(
                    fullPath);
            }
        }
        catch
        {
            // Invalid candidate paths are simply ignored.
        }
    }
}
