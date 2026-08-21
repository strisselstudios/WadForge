using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TrenchBroom.Companion.Core;

public sealed record CompanionTrenchBroomGameConfigResult(
    string ConfigurationDirectory,
    bool CreatedByCompanion,
    bool UsesExistingConfiguration);

public static partial class CompanionTrenchBroomGameConfigService
{
    public const string CompanionManagedMarkerFileName =
        ".trenchbroom-companion-managed";

    private const string ConfigurationFileName =
        "GameConfig.cfg";

    public static CompanionTrenchBroomGameConfigResult EnsureDuskGameConfig(
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

        if (!File.Exists(
                executablePath))
        {
            throw new FileNotFoundException(
                "The TrenchBroom executable does not exist.",
                executablePath);
        }

        string installationDirectory =
            Path.GetDirectoryName(
                executablePath) ??
            throw new InvalidDataException(
                "Could not determine the TrenchBroom installation directory.");

        string gamesDirectory =
            Path.Combine(
                installationDirectory,
                "games");

        string sourceDirectory =
            Path.Combine(
                gamesDirectory,
                "Quake");

        string destinationDirectory =
            Path.Combine(
                gamesDirectory,
                "DUSK");

        string sourceConfigPath =
            Path.Combine(
                sourceDirectory,
                ConfigurationFileName);

        string destinationConfigPath =
            Path.Combine(
                destinationDirectory,
                ConfigurationFileName);

        string markerPath =
            Path.Combine(
                destinationDirectory,
                CompanionManagedMarkerFileName);

        if (!File.Exists(
                sourceConfigPath))
        {
            throw new InvalidDataException(
                "The managed TrenchBroom installation does not contain its Quake game configuration. " +
                "Companion cannot provision the DUSK configuration safely.");
        }

        if (Directory.Exists(
                destinationDirectory) &&
            !File.Exists(
                markerPath))
        {
            if (File.Exists(
                    destinationConfigPath) &&
                IsGameConfigNamed(
                    destinationConfigPath,
                    "DUSK"))
            {
                return new CompanionTrenchBroomGameConfigResult(
                    destinationDirectory,
                    CreatedByCompanion: false,
                    UsesExistingConfiguration: true);
            }

            throw new InvalidDataException(
                "A TrenchBroom game configuration folder named 'DUSK' already exists, " +
                "but it is not managed by Companion and does not identify itself as DUSK. " +
                "Companion left that folder untouched.");
        }

        if (Directory.Exists(
                destinationDirectory) &&
            File.Exists(
                markerPath) &&
            File.Exists(
                destinationConfigPath) &&
            IsGameConfigNamed(
                destinationConfigPath,
                "DUSK"))
        {
            return new CompanionTrenchBroomGameConfigResult(
                destinationDirectory,
                CreatedByCompanion: false,
                UsesExistingConfiguration: false);
        }

        ProvisionCompanionManagedDuskConfig(
            sourceDirectory,
            destinationDirectory);

        return new CompanionTrenchBroomGameConfigResult(
            destinationDirectory,
            CreatedByCompanion: true,
            UsesExistingConfiguration: false);
    }

    private static void ProvisionCompanionManagedDuskConfig(
        string sourceDirectory,
        string destinationDirectory)
    {
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

            string stagedConfigPath =
                Path.Combine(
                    stagingDirectory,
                    ConfigurationFileName);

            string configurationText =
                File.ReadAllText(
                    stagedConfigPath);

            Match quakeName =
                GameNameRegex()
                    .Match(
                        configurationText);

            if (!quakeName.Success ||
                !string.Equals(
                    quakeName.Groups["name"].Value,
                    "Quake",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The installed Quake GameConfig.cfg did not contain the expected game name. " +
                    "Companion did not create a DUSK configuration from an unknown template.");
            }

            string patchedText =
                GameNameRegex()
                    .Replace(
                        configurationText,
                        match =>
                            match.Groups["prefix"].Value +
                            "DUSK" +
                            match.Groups["suffix"].Value,
                        count: 1);

            File.WriteAllText(
                stagedConfigPath,
                patchedText,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            File.WriteAllText(
                Path.Combine(
                    stagingDirectory,
                    CompanionManagedMarkerFileName),
                "Managed by TrenchBroom Companion.\r\n",
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            if (!IsGameConfigNamed(
                    stagedConfigPath,
                    "DUSK"))
            {
                throw new InvalidDataException(
                    "The staged DUSK TrenchBroom game configuration failed validation.");
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

            string finalConfigPath =
                Path.Combine(
                    destinationDirectory,
                    ConfigurationFileName);

            if (!File.Exists(
                    finalConfigPath) ||
                !IsGameConfigNamed(
                    finalConfigPath,
                    "DUSK"))
            {
                throw new InvalidDataException(
                    "The installed DUSK TrenchBroom game configuration failed final validation.");
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

    private static bool IsGameConfigNamed(
        string configPath,
        string expectedName)
    {
        string text =
            File.ReadAllText(
                configPath);

        Match match =
            GameNameRegex()
                .Match(
                    text);

        return match.Success &&
            string.Equals(
                match.Groups["name"].Value,
                expectedName,
                StringComparison.OrdinalIgnoreCase);
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
            // Cleanup is best-effort. Preserve the original provisioning result.
        }
    }

    [GeneratedRegex(
        """(?m)(?<prefix>"name"\s*:\s*")(?<name>[^"]+)(?<suffix>")""",
        RegexOptions.CultureInvariant)]
    private static partial Regex GameNameRegex();
}
