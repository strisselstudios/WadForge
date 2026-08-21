using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionProjectMapCreationService
{
    private const string EmptyWorldspawnContents =
        "{\r\n" +
        "\"classname\" \"worldspawn\"\r\n" +
        "}\r\n";

    public string CreateMap(
        CompanionProjectSession session,
        string mapName)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        string fileName =
            BuildMapFileName(
                mapName);

        string mapsDirectory =
            Path.Combine(
                session.ProjectDirectory,
                CompanionProjectLayout.MapsDirectoryName);

        Directory.CreateDirectory(
            mapsDirectory);

        string destinationPath =
            Path.GetFullPath(
                Path.Combine(
                    mapsDirectory,
                    fileName));

        EnsureInsideMapsDirectory(
            mapsDirectory,
            destinationPath);

        if (File.Exists(
                destinationPath))
        {
            throw new IOException(
                $"A map named '{fileName}' already exists in this project.");
        }

        bool alreadyRegistered =
            session.Project.Maps
                .Any(
                    map =>
                    {
                        string registeredPath =
                            CompanionProjectStore.ResolveMapPath(
                                session.ProjectFilePath,
                                map.Path);

                        return string.Equals(
                            registeredPath,
                            destinationPath,
                            StringComparison.OrdinalIgnoreCase);
                    });

        if (alreadyRegistered)
        {
            throw new IOException(
                $"Map '{fileName}' is already registered in this project.");
        }

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

        string temporaryPath =
            destinationPath +
            ".tbcompanion-new-" +
            Guid.NewGuid().ToString("N");

        bool destinationCreated =
            false;

        try
        {
            string initialMapContents =
                CompanionTrenchBroomMapIdentityService.BuildHeader(
                    session.Project.GameId) +
                EmptyWorldspawnContents;

            File.WriteAllText(
                temporaryPath,
                initialMapContents,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            File.Move(
                temporaryPath,
                destinationPath,
                overwrite: false);

            destinationCreated =
                true;

            session.AddMap(
                destinationPath,
                makeActive: true);

            session.Save();

            return destinationPath;
        }
        catch
        {
            session.Project.Maps =
                previousMaps;

            session.Project.ActiveMapPath =
                previousActiveMap;

            if (destinationCreated)
            {
                TryDeleteFile(
                    destinationPath);
            }

            throw;
        }
        finally
        {
            TryDeleteFile(
                temporaryPath);
        }
    }

    public static string BuildMapFileName(
        string mapName)
    {
        if (string.IsNullOrWhiteSpace(
                mapName))
        {
            throw new ArgumentException(
                "Map name cannot be empty.",
                nameof(mapName));
        }

        string trimmed =
            mapName.Trim();

        if (trimmed.Contains(
                Path.DirectorySeparatorChar) ||
            trimmed.Contains(
                Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException(
                "Map name cannot contain folders.",
                nameof(mapName));
        }

        if (string.Equals(
                Path.GetExtension(
                    trimmed),
                ".map",
                StringComparison.OrdinalIgnoreCase))
        {
            trimmed =
                Path.GetFileNameWithoutExtension(
                    trimmed);
        }

        trimmed =
            trimmed.Trim()
                .TrimEnd(
                    '.',
                    ' ');

        if (string.IsNullOrWhiteSpace(
                trimmed))
        {
            throw new ArgumentException(
                "Map name cannot be empty.",
                nameof(mapName));
        }

        HashSet<char> invalidCharacters =
            new(
                Path.GetInvalidFileNameChars());

        StringBuilder safeName =
            new();

        bool lastWasUnderscore =
            false;

        foreach (char character in
                 trimmed)
        {
            if (invalidCharacters.Contains(
                    character))
            {
                throw new ArgumentException(
                    $"Map name contains an invalid character: '{character}'.",
                    nameof(mapName));
            }

            if (char.IsWhiteSpace(
                    character))
            {
                if (!lastWasUnderscore)
                {
                    safeName.Append(
                        '_');

                    lastWasUnderscore =
                        true;
                }

                continue;
            }

            safeName.Append(
                character);

            lastWasUnderscore =
                character == '_';
        }

        string normalized =
            safeName.ToString()
                .Trim(
                    '_',
                    '.',
                    ' ');

        if (string.IsNullOrWhiteSpace(
                normalized))
        {
            throw new ArgumentException(
                "Map name must contain at least one usable character.",
                nameof(mapName));
        }

        if (IsReservedWindowsFileName(
                normalized))
        {
            normalized +=
                "_map";
        }

        return normalized +
            ".map";
    }

    private static void EnsureInsideMapsDirectory(
        string mapsDirectory,
        string destinationPath)
    {
        string mapsRoot =
            Path.GetFullPath(
                mapsDirectory);

        string requiredPrefix =
            mapsRoot.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? mapsRoot
                : mapsRoot +
                    Path.DirectorySeparatorChar;

        if (!destinationPath.StartsWith(
                requiredPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "New map path escaped the project maps directory.");
        }
    }

    private static bool IsReservedWindowsFileName(
        string fileName)
    {
        string baseName =
            Path.GetFileNameWithoutExtension(
                fileName)
                .TrimEnd(
                    '.',
                    ' ')
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

            if ((prefix == "COM" ||
                 prefix == "LPT") &&
                suffix is >= '1' and <= '9')
            {
                return true;
            }
        }

        return false;
    }

    private static void TryDeleteFile(
        string filePath)
    {
        try
        {
            if (File.Exists(
                    filePath))
            {
                File.Delete(
                    filePath);
            }
        }
        catch
        {
            // Preserve the original map-creation failure.
        }
    }
}
