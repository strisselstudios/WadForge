using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TrenchBroom.Companion.Core;

public sealed record CompanionDuskAuthoringResourceStatus(
    bool IsReady,
    string ManagedGameConfigDirectory,
    string ManagedId1Directory,
    string? Problem);

public sealed record CompanionDuskAuthoringResourceImportResult(
    string SourceDirectory,
    string EntityDefinitionPath,
    string PakPath,
    string PalettePath);

public static class CompanionDuskAuthoringResourceService
{
    private const string ManagedAuthoringDirectoryName =
        "DUSK-Authoring";

    private const string ManagedGameDirectoryName =
        "id1";

    private const string ManagedEntityDefinitionFileName =
        "DUSK.fgd";

    private const string SourceEntityDefinitionFileName =
        "dusk4.fgd";

    private const string SourcePakFileName =
        "dusk.pak";

    private const string SourcePaletteFileName =
        "palette.lmp";

    private const string GameConfigFileName =
        "GameConfig.cfg";

    private const string ExpectedEntityDefinitionReference =
        "DUSK.fgd";

    private static readonly Regex EntityDefinitionsRegex =
        new(
            """(?s)(?<prefix>"definitions"\s*:\s*\[)[^\]]*(?<suffix>\])""",
            RegexOptions.CultureInvariant);

    public static CompanionDuskAuthoringResourceStatus GetStatus(
        string trenchBroomExecutablePath)
    {
        Paths paths =
            ResolvePaths(
                trenchBroomExecutablePath);

        if (!File.Exists(
                paths.ManagedMarkerPath))
        {
            return new CompanionDuskAuthoringResourceStatus(
                false,
                paths.GameConfigDirectory,
                paths.Id1Directory,
                "The DUSK TrenchBroom configuration is not managed by Companion.");
        }

        if (!File.Exists(
                paths.EntityDefinitionPath))
        {
            return Missing(
                paths,
                "DUSK.fgd has not been provisioned.");
        }

        if (!File.Exists(
                paths.PakPath))
        {
            return Missing(
                paths,
                "dusk.pak has not been provisioned.");
        }

        if (!File.Exists(
                paths.PalettePath))
        {
            return Missing(
                paths,
                "palette.lmp has not been provisioned.");
        }

        if (!File.Exists(
                paths.GameConfigPath))
        {
            return Missing(
                paths,
                "The managed DUSK GameConfig.cfg file is missing.");
        }

        try
        {
            ValidateEntityDefinition(
                paths.EntityDefinitionPath);

            ValidatePak(
                paths.PakPath);

            ValidatePalette(
                paths.PalettePath);

            string gameConfigText =
                File.ReadAllText(
                    paths.GameConfigPath,
                    Encoding.UTF8);

            if (!ContainsDuskEntityDefinitionReference(
                    gameConfigText))
            {
                PatchGameConfigEntityDefinitions(
                    paths.GameConfigPath);

                gameConfigText =
                    File.ReadAllText(
                        paths.GameConfigPath,
                        Encoding.UTF8);

                if (!ContainsDuskEntityDefinitionReference(
                        gameConfigText))
                {
                    return Missing(
                        paths,
                        "The managed DUSK game configuration could not be repaired to use the DUSK entity definition.");
                }
            }
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  InvalidDataException)
        {
            return Missing(
                paths,
                exception.Message);
        }

        return new CompanionDuskAuthoringResourceStatus(
            true,
            paths.GameConfigDirectory,
            paths.Id1Directory,
            null);
    }

    public static CompanionDuskAuthoringResourceImportResult Import(
        string trenchBroomExecutablePath,
        string sourceDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                sourceDirectory))
        {
            throw new ArgumentException(
                "A DUSK mapping resource folder is required.",
                nameof(sourceDirectory));
        }

        string normalizedSourceDirectory =
            Path.GetFullPath(
                sourceDirectory);

        if (!Directory.Exists(
                normalizedSourceDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The selected DUSK mapping resource folder does not exist: {normalizedSourceDirectory}");
        }

        Paths paths =
            ResolvePaths(
                trenchBroomExecutablePath);

        if (!File.Exists(
                paths.ManagedMarkerPath))
        {
            throw new InvalidOperationException(
                "Companion will only install DUSK authoring resources into a Companion-managed DUSK TrenchBroom configuration.");
        }

        string sourceFgd =
            FindRequiredFile(
                normalizedSourceDirectory,
                SourceEntityDefinitionFileName);

        string sourcePak =
            FindRequiredFile(
                normalizedSourceDirectory,
                SourcePakFileName);

        string sourcePalette =
            FindRequiredFile(
                normalizedSourceDirectory,
                SourcePaletteFileName);

        ValidateEntityDefinition(
            sourceFgd);

        ValidatePak(
            sourcePak);

        ValidatePalette(
            sourcePalette);

        Directory.CreateDirectory(
            paths.GameConfigDirectory);

        Directory.CreateDirectory(
            paths.Id1Directory);

        string paletteDirectory =
            Path.GetDirectoryName(
                paths.PalettePath) ??
            throw new InvalidOperationException(
                "Could not determine the managed DUSK palette directory.");

        Directory.CreateDirectory(
            paletteDirectory);

        CopyFileAtomically(
            sourceFgd,
            paths.EntityDefinitionPath);

        CopyFileAtomically(
            sourcePak,
            paths.PakPath);

        CopyFileAtomically(
            sourcePalette,
            paths.PalettePath);

        CopyOptionalTextureDirectory(
            sourcePalette,
            paths.Id1Directory);

        PatchGameConfigEntityDefinitions(
            paths.GameConfigPath);

        CompanionDuskAuthoringResourceStatus status =
            GetStatus(
                trenchBroomExecutablePath);

        if (!status.IsReady)
        {
            throw new InvalidDataException(
                "The DUSK authoring resource import did not pass final validation. " +
                (status.Problem ??
                 "The reason could not be determined."));
        }

        return new CompanionDuskAuthoringResourceImportResult(
            normalizedSourceDirectory,
            paths.EntityDefinitionPath,
            paths.PakPath,
            paths.PalettePath);
    }

    private static CompanionDuskAuthoringResourceStatus Missing(
        Paths paths,
        string problem)
    {
        return new CompanionDuskAuthoringResourceStatus(
            false,
            paths.GameConfigDirectory,
            paths.Id1Directory,
            problem);
    }

    private static Paths ResolvePaths(
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
                "The Companion-managed TrenchBroom executable does not exist.",
                executablePath);
        }

        string trenchBroomDirectory =
            Path.GetDirectoryName(
                executablePath) ??
            throw new InvalidOperationException(
                "Could not determine the Companion-managed TrenchBroom directory.");

        string suiteDirectory =
            Path.GetDirectoryName(
                trenchBroomDirectory) ??
            throw new InvalidOperationException(
                "Could not determine the Companion-managed TrenchBroom suite directory.");

        string gameConfigDirectory =
            Path.Combine(
                trenchBroomDirectory,
                "games",
                "DUSK");

        string authoringDirectory =
            Path.Combine(
                suiteDirectory,
                ManagedAuthoringDirectoryName);

        string id1Directory =
            Path.Combine(
                authoringDirectory,
                ManagedGameDirectoryName);

        return new Paths(
            gameConfigDirectory,
            Path.Combine(
                gameConfigDirectory,
                GameConfigFileName),
            Path.Combine(
                gameConfigDirectory,
                CompanionTrenchBroomGameConfigService.CompanionManagedMarkerFileName),
            id1Directory,
            Path.Combine(
                gameConfigDirectory,
                ManagedEntityDefinitionFileName),
            Path.Combine(
                id1Directory,
                SourcePakFileName),
            Path.Combine(
                id1Directory,
                "gfx",
                SourcePaletteFileName));
    }

    private static string FindRequiredFile(
        string sourceDirectory,
        string fileName)
    {
        EnumerationOptions options =
            new()
            {
                RecurseSubdirectories =
                    true,

                IgnoreInaccessible =
                    true,

                ReturnSpecialDirectories =
                    false,

                AttributesToSkip =
                    FileAttributes.ReparsePoint
            };

        List<string> matches =
            Directory.EnumerateFiles(
                    sourceDirectory,
                    "*",
                    options)
                .Where(
                    path =>
                        string.Equals(
                            Path.GetFileName(
                                path),
                            fileName,
                            StringComparison.OrdinalIgnoreCase))
                .OrderBy(
                    path =>
                        path.Length)
                .ThenBy(
                    path =>
                        path,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (matches.Count == 0)
        {
            throw new InvalidDataException(
                $"The selected folder does not contain the required DUSK mapping resource '{fileName}'.");
        }

        if (matches.Count > 1)
        {
            throw new InvalidDataException(
                $"The selected folder contains more than one '{fileName}'. Select the specific DUSK mapping resource bundle folder instead.");
        }

        return Path.GetFullPath(
            matches[0]);
    }

    private static void ValidateEntityDefinition(
        string path)
    {
        FileInfo info =
            new(
                path);

        if (!info.Exists ||
            info.Length == 0)
        {
            throw new InvalidDataException(
                "The DUSK FGD file is empty or missing.");
        }

        string text =
            File.ReadAllText(
                path,
                Encoding.UTF8);

        if (!text.Contains(
                "@PointClass",
                StringComparison.OrdinalIgnoreCase) &&
            !text.Contains(
                "@SolidClass",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The selected dusk4.fgd does not look like a valid TrenchBroom entity definition file.");
        }
    }

    private static void ValidatePak(
        string path)
    {
        using FileStream stream =
            File.OpenRead(
                path);

        if (stream.Length < 12)
        {
            throw new InvalidDataException(
                "The selected dusk.pak is too small to be a valid Quake PAK file.");
        }

        Span<byte> signature =
            stackalloc byte[4];

        int read =
            stream.Read(
                signature);

        if (read != 4 ||
            signature[0] != (byte)'P' ||
            signature[1] != (byte)'A' ||
            signature[2] != (byte)'C' ||
            signature[3] != (byte)'K')
        {
            throw new InvalidDataException(
                "The selected dusk.pak does not have a valid PACK header.");
        }
    }

    private static void ValidatePalette(
        string path)
    {
        FileInfo info =
            new(
                path);

        if (!info.Exists ||
            info.Length != 768)
        {
            throw new InvalidDataException(
                "The selected palette.lmp is not a 768-byte Quake palette.");
        }
    }

    private static void PatchGameConfigEntityDefinitions(
        string gameConfigPath)
    {
        if (!File.Exists(
                gameConfigPath))
        {
            throw new FileNotFoundException(
                "The managed DUSK GameConfig.cfg file does not exist.",
                gameConfigPath);
        }

        string text =
            File.ReadAllText(
                gameConfigPath,
                Encoding.UTF8);

        Match match =
            EntityDefinitionsRegex.Match(
                text);

        if (!match.Success)
        {
            throw new InvalidDataException(
                "The managed DUSK GameConfig.cfg does not contain an entity definitions list that Companion can update safely.");
        }

        string replacement =
            match.Groups["prefix"].Value +
            " \"" +
            ExpectedEntityDefinitionReference +
            "\" " +
            match.Groups["suffix"].Value;

        string patchedText =
            EntityDefinitionsRegex.Replace(
                text,
                replacement,
                count:
                    1);

        if (!ContainsDuskEntityDefinitionReference(
                patchedText))
        {
            throw new InvalidDataException(
                "The managed DUSK GameConfig.cfg entity definition update failed validation.");
        }

        WriteTextAtomically(
            gameConfigPath,
            patchedText);
    }

    private static bool ContainsDuskEntityDefinitionReference(
        string gameConfigText)
    {
        Match match =
            EntityDefinitionsRegex.Match(
                gameConfigText);

        if (!match.Success)
        {
            return false;
        }

        MatchCollection references =
            Regex.Matches(
                match.Value,
                "\"(?<path>[^\"]+)\"",
                RegexOptions.CultureInvariant);

        foreach (Match reference in references)
        {
            if (string.Equals(
                    reference.Groups["path"].Value,
                    ExpectedEntityDefinitionReference,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void CopyOptionalTextureDirectory(
        string sourcePalettePath,
        string id1Directory)
    {
        string? sourcePaletteDirectory =
            Path.GetDirectoryName(
                sourcePalettePath);

        if (string.IsNullOrWhiteSpace(
                sourcePaletteDirectory) ||
            !string.Equals(
                Path.GetFileName(
                    sourcePaletteDirectory),
                "textures",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string destinationTextureDirectory =
            Path.Combine(
                id1Directory,
                "textures");

        CopyDirectoryContents(
            sourcePaletteDirectory,
            destinationTextureDirectory);
    }

    private static void CopyDirectoryContents(
        string sourceDirectory,
        string destinationDirectory)
    {
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

            CopyFileAtomically(
                sourceFile,
                destinationFile);
        }
    }

    private static void CopyFileAtomically(
        string sourcePath,
        string destinationPath)
    {
        string? destinationDirectory =
            Path.GetDirectoryName(
                destinationPath);

        if (string.IsNullOrWhiteSpace(
                destinationDirectory))
        {
            throw new InvalidOperationException(
                "Could not determine a destination directory for a DUSK authoring resource.");
        }

        Directory.CreateDirectory(
            destinationDirectory);

        string temporaryPath =
            Path.Combine(
                destinationDirectory,
                $".{Path.GetFileName(destinationPath)}.companion-{Guid.NewGuid():N}.tmp");

        try
        {
            File.Copy(
                sourcePath,
                temporaryPath,
                overwrite:
                    true);

            File.Move(
                temporaryPath,
                destinationPath,
                overwrite:
                    true);
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

    private static void WriteTextAtomically(
        string destinationPath,
        string text)
    {
        string? destinationDirectory =
            Path.GetDirectoryName(
                destinationPath);

        if (string.IsNullOrWhiteSpace(
                destinationDirectory))
        {
            throw new InvalidOperationException(
                "Could not determine the managed DUSK game configuration directory.");
        }

        string temporaryPath =
            Path.Combine(
                destinationDirectory,
                $".{Path.GetFileName(destinationPath)}.companion-{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(
                temporaryPath,
                text,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        false));

            File.Move(
                temporaryPath,
                destinationPath,
                overwrite:
                    true);
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

    private sealed record Paths(
        string GameConfigDirectory,
        string GameConfigPath,
        string ManagedMarkerPath,
        string Id1Directory,
        string EntityDefinitionPath,
        string PakPath,
        string PalettePath);
}
