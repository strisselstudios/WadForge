using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionProjectWadService
{
    private static readonly Regex WorldPropertyPattern =
        new(
            @"(?m)^(?<indent>[ \t]*)""(?<key>[^""]+)""[ \t]+""(?<value>(?:\\.|[^""])*)""[ \t]*(?=\r?$)",
            RegexOptions.CultureInvariant);

    public CompanionProjectWadImportResult ImportIntoProject(
        CompanionProjectSession session,
        string sourceWadPath)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        if (string.IsNullOrWhiteSpace(
                sourceWadPath))
        {
            throw new ArgumentException(
                "A WAD path is required.",
                nameof(sourceWadPath));
        }

        string sourcePath =
            Path.GetFullPath(
                sourceWadPath);

        WadRegistrationResult sourceInspection =
            WadRegistrationService.Inspect(
                sourcePath);

        if (!sourceInspection.WadIsValid)
        {
            throw new InvalidDataException(
                $"'{Path.GetFileName(sourcePath)}' is not a valid WAD2 or WAD3 archive. " +
                sourceInspection.Validation);
        }

        string projectWadsDirectory =
            GetProjectWadsDirectory(
                session);

        Directory.CreateDirectory(
            projectWadsDirectory);

        string destinationPath =
            Path.GetFullPath(
                Path.Combine(
                    projectWadsDirectory,
                    Path.GetFileName(
                        sourcePath)));

        if (!IsInsideDirectory(
                destinationPath,
                projectWadsDirectory))
        {
            throw new InvalidDataException(
                "The project WAD destination escaped the project WAD directory.");
        }

        bool samePath =
            string.Equals(
                sourcePath,
                destinationPath,
                StringComparison.OrdinalIgnoreCase);

        bool copiedWad =
            false;

        bool copiedManifest =
            false;

        string sourceManifestPath =
            sourcePath +
            ".wadforge.json";

        string destinationManifestPath =
            destinationPath +
            ".wadforge.json";

        try
        {
            if (!samePath)
            {
                if (File.Exists(
                        destinationPath))
                {
                    if (!FilesMatch(
                            sourcePath,
                            destinationPath))
                    {
                        throw new IOException(
                            $"A different WAD named '{Path.GetFileName(destinationPath)}' already exists in this project. " +
                            "Rename one of the archives before adding it.");
                    }
                }
                else
                {
                    File.Copy(
                        sourcePath,
                        destinationPath,
                        overwrite: false);

                    copiedWad =
                        true;
                }

                if (File.Exists(
                        sourceManifestPath))
                {
                    if (File.Exists(
                            destinationManifestPath))
                    {
                        if (!FilesMatch(
                                sourceManifestPath,
                                destinationManifestPath))
                        {
                            throw new IOException(
                                $"A different WadForge manifest already exists for '{Path.GetFileName(destinationPath)}'.");
                        }
                    }
                    else
                    {
                        File.Copy(
                            sourceManifestPath,
                            destinationManifestPath,
                            overwrite: false);

                        copiedManifest =
                            true;
                    }
                }
            }

            WadRegistrationResult destinationInspection =
                WadRegistrationService.Inspect(
                    destinationPath);

            if (!destinationInspection.WadIsValid)
            {
                throw new InvalidDataException(
                    $"The project copy of '{Path.GetFileName(destinationPath)}' failed WAD validation. " +
                    destinationInspection.Validation);
            }

            return new CompanionProjectWadImportResult(
                destinationPath,
                destinationInspection.WadFormat,
                destinationInspection.TextureCount,
                copiedWad);
        }
        catch
        {
            if (copiedManifest &&
                File.Exists(
                    destinationManifestPath))
            {
                File.Delete(
                    destinationManifestPath);
            }

            if (copiedWad &&
                File.Exists(
                    destinationPath))
            {
                File.Delete(
                    destinationPath);
            }

            throw;
        }
    }

    public IReadOnlyList<string> GetProjectWadPaths(
        CompanionProjectSession session)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        string projectWadsDirectory =
            GetProjectWadsDirectory(
                session);

        if (!Directory.Exists(
                projectWadsDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(
                projectWadsDirectory,
                "*.wad",
                SearchOption.TopDirectoryOnly)
            .Select(
                Path.GetFullPath)
            .OrderBy(
                path =>
                    Path.GetFileName(path),
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                path =>
                    path,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> GetMapWadReferences(
        string mapPath)
    {
        if (string.IsNullOrWhiteSpace(
                mapPath))
        {
            throw new ArgumentException(
                "A map path is required.",
                nameof(mapPath));
        }

        string fullMapPath =
            Path.GetFullPath(
                mapPath);

        if (!File.Exists(
                fullMapPath))
        {
            throw new FileNotFoundException(
                "The map could not be found.",
                fullMapPath);
        }

        MapTextFile mapFile =
            ReadMapTextFile(
                fullMapPath);

        string? wadProperty =
            GetWorldspawnProperty(
                mapFile.Text,
                "wad");

        if (string.IsNullOrWhiteSpace(
                wadProperty))
        {
            return Array.Empty<string>();
        }

        return UnescapeMapPropertyValue(
                wadProperty)
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(
                value =>
                    value.Length >
                    0)
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
    public CompanionProjectWadReconciliationResult ReconcileReferencedMapWads(
        CompanionProjectSession session,
        string mapPath)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        if (string.IsNullOrWhiteSpace(
                mapPath))
        {
            throw new ArgumentException(
                "A map path is required.",
                nameof(mapPath));
        }

        string fullMapPath =
            Path.GetFullPath(
                mapPath);

        if (!File.Exists(
                fullMapPath))
        {
            throw new FileNotFoundException(
                "The map could not be found.",
                fullMapPath);
        }

        MapTextFile mapFile =
            ReadMapTextFile(
                fullMapPath);

        string? wadProperty =
            GetWorldspawnProperty(
                mapFile.Text,
                "wad");

        if (string.IsNullOrWhiteSpace(
                wadProperty))
        {
            return new CompanionProjectWadReconciliationResult(
                0,
                0,
                false,
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        string[] references =
            UnescapeMapPropertyValue(
                    wadProperty)
                .Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

        if (references.Length ==
            0)
        {
            return new CompanionProjectWadReconciliationResult(
                0,
                0,
                false,
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        string mapDirectory =
            Path.GetDirectoryName(
                fullMapPath) ??
            session.ProjectDirectory;

        List<string> managedWads =
            new();

        List<string> rewrittenReferences =
            new();

        List<string> issues =
            new();

        HashSet<string> seenManagedWads =
            new(
                StringComparer.OrdinalIgnoreCase);

        int importedWadCount =
            0;

        foreach (string reference in
                 references)
        {
            string? resolvedPath =
                ResolveReferencedWadPath(
                    session,
                    mapDirectory,
                    reference);

            if (string.IsNullOrWhiteSpace(
                    resolvedPath))
            {
                issues.Add(
                    $"WAD reference could not be resolved: {reference}");

                rewrittenReferences.Add(
                    reference);

                continue;
            }

            try
            {
                CompanionProjectWadImportResult imported =
                    ImportIntoProject(
                        session,
                        resolvedPath);

                if (imported.CopiedIntoProject)
                {
                    importedWadCount++;
                }

                string managedPath =
                    Path.GetFullPath(
                        imported.WadPath);

                if (seenManagedWads.Add(
                        managedPath))
                {
                    managedWads.Add(
                        managedPath);

                    rewrittenReferences.Add(
                        managedPath.Replace(
                            '\\',
                            '/'));
                }
            }
            catch (Exception exception)
            {
                issues.Add(
                    $"{reference}: {exception.Message}");

                rewrittenReferences.Add(
                    reference);
            }
        }

        string updatedText =
            SetWorldspawnProperty(
                mapFile.Text,
                "wad",
                string.Join(
                    ";",
                    rewrittenReferences));

        bool changed =
            !string.Equals(
                updatedText,
                mapFile.Text,
                StringComparison.Ordinal);

        if (changed)
        {
            WriteMapTextFileAtomically(
                fullMapPath,
                updatedText,
                mapFile.Encoding);
        }

        return new CompanionProjectWadReconciliationResult(
            references.Length,
            importedWadCount,
            changed,
            managedWads,
            issues);
    }

    public CompanionProjectWadSyncResult SynchronizeMapWorldspawnWads(
        CompanionProjectSession session,
        string mapPath)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        if (string.IsNullOrWhiteSpace(
                mapPath))
        {
            throw new ArgumentException(
                "A map path is required.",
                nameof(mapPath));
        }

        string fullMapPath =
            Path.GetFullPath(
                mapPath);

        if (!File.Exists(
                fullMapPath))
        {
            throw new FileNotFoundException(
                "The active map could not be found.",
                fullMapPath);
        }

        IReadOnlyList<string> projectWads =
            GetProjectWadPaths(
                session);

        if (projectWads.Count == 0)
        {
            return new CompanionProjectWadSyncResult(
                0,
                false,
                Array.Empty<string>());
        }

        List<string> validatedWads =
            new();

        foreach (string wadPath in
                 projectWads)
        {
            WadRegistrationResult inspection =
                WadRegistrationService.Inspect(
                    wadPath);

            if (!inspection.WadIsValid)
            {
                throw new InvalidDataException(
                    $"Project WAD '{Path.GetFileName(wadPath)}' is invalid. " +
                    inspection.Validation);
            }

            validatedWads.Add(
                wadPath);
        }

        string wadPropertyValue =
            string.Join(
                ";",
                validatedWads.Select(
                    path =>
                        path.Replace(
                            '\\',
                            '/')));

        MapTextFile mapFile =
            ReadMapTextFile(
                fullMapPath);

        string updatedText =
            SetWorldspawnProperty(
                mapFile.Text,
                "wad",
                wadPropertyValue);

        bool changed =
            !string.Equals(
                updatedText,
                mapFile.Text,
                StringComparison.Ordinal);

        if (changed)
        {
            WriteMapTextFileAtomically(
                fullMapPath,
                updatedText,
                mapFile.Encoding);
        }

        return new CompanionProjectWadSyncResult(
            validatedWads.Count,
            changed,
            validatedWads);
    }

    public CompanionProjectWadSyncResult SynchronizeMapWorldspawnWads(
        string mapPath,
        IReadOnlyList<string> wadPaths)
    {
        if (string.IsNullOrWhiteSpace(
                mapPath))
        {
            throw new ArgumentException(
                "A map path is required.",
                nameof(mapPath));
        }

        ArgumentNullException.ThrowIfNull(
            wadPaths);

        string fullMapPath =
            Path.GetFullPath(
                mapPath);

        if (!File.Exists(
                fullMapPath))
        {
            throw new FileNotFoundException(
                "The map could not be found.",
                fullMapPath);
        }

        List<string> validated =
            new();

        foreach (string wadPath in
                 wadPaths)
        {
            string fullWadPath =
                Path.GetFullPath(
                    wadPath);

            WadRegistrationResult inspection =
                WadRegistrationService.Inspect(
                    fullWadPath);

            if (!inspection.WadIsValid)
            {
                throw new InvalidDataException(
                    $"Selected WAD '{Path.GetFileName(fullWadPath)}' is invalid. " +
                    inspection.Validation);
            }

            if (!validated.Contains(
                    fullWadPath,
                    StringComparer.OrdinalIgnoreCase))
            {
                validated.Add(
                    fullWadPath);
            }
        }

        string wadPropertyValue =
            string.Join(
                ";",
                validated.Select(
                    path =>
                        path.Replace(
                            '\\',
                            '/')));

        MapTextFile mapFile =
            ReadMapTextFile(
                fullMapPath);

        string updatedText =
            SetWorldspawnProperty(
                mapFile.Text,
                "wad",
                wadPropertyValue);

        bool changed =
            !string.Equals(
                updatedText,
                mapFile.Text,
                StringComparison.Ordinal);

        if (changed)
        {
            WriteMapTextFileAtomically(
                fullMapPath,
                updatedText,
                mapFile.Encoding);
        }

        return new CompanionProjectWadSyncResult(
            validated.Count,
            changed,
            validated);
    }
    private static string? GetWorldspawnProperty(
        string mapText,
        string propertyName)
    {
        (int entityOpen, int entityClose) =
            FindFirstEntityBounds(
                mapText);

        int propertyRegionStart =
            entityOpen +
            1;

        int propertyRegionEnd =
            FindFirstNestedOpeningBrace(
                mapText,
                propertyRegionStart,
                entityClose);

        string propertyRegion =
            mapText[
                propertyRegionStart..
                propertyRegionEnd];

        MatchCollection matches =
            WorldPropertyPattern.Matches(
                propertyRegion);

        Match? classnameMatch =
            matches
                .Cast<Match>()
                .FirstOrDefault(
                    match =>
                        string.Equals(
                            match.Groups["key"].Value,
                            "classname",
                            StringComparison.Ordinal));

        if (classnameMatch is null ||
            !string.Equals(
                classnameMatch.Groups["value"].Value,
                "worldspawn",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The first map entity is not a valid worldspawn entity.");
        }

        Match[] propertyMatches =
            matches
                .Cast<Match>()
                .Where(
                    match =>
                        string.Equals(
                            match.Groups["key"].Value,
                            propertyName,
                            StringComparison.Ordinal))
                .ToArray();

        if (propertyMatches.Length >
            1)
        {
            throw new InvalidDataException(
                $"The worldspawn contains more than one '{propertyName}' property.");
        }

        return propertyMatches.Length ==
            1
                ? propertyMatches[0]
                    .Groups["value"]
                    .Value
                : null;
    }

    private static string? ResolveReferencedWadPath(
        CompanionProjectSession session,
        string mapDirectory,
        string reference)
    {
        if (string.IsNullOrWhiteSpace(
                reference))
        {
            return null;
        }

        string platformReference =
            reference
                .Replace(
                    '/',
                    Path.DirectorySeparatorChar)
                .Replace(
                    '\\',
                    Path.DirectorySeparatorChar);

        try
        {
            if (Path.IsPathRooted(
                    platformReference))
            {
                string rooted =
                    Path.GetFullPath(
                        platformReference);

                if (File.Exists(
                        rooted))
                {
                    return rooted;
                }
            }
            else
            {
                string mapRelative =
                    Path.GetFullPath(
                        Path.Combine(
                            mapDirectory,
                            platformReference));

                if (File.Exists(
                        mapRelative))
                {
                    return mapRelative;
                }
            }
        }
        catch
        {
            // Fall through to the managed project filename lookup.
        }

        string fileName =
            Path.GetFileName(
                platformReference);

        if (string.IsNullOrWhiteSpace(
                fileName))
        {
            return null;
        }

        string managedCandidate =
            Path.GetFullPath(
                Path.Combine(
                    GetProjectWadsDirectory(
                        session),
                    fileName));

        return File.Exists(
                managedCandidate)
            ? managedCandidate
            : null;
    }

    private static string UnescapeMapPropertyValue(
        string value)
    {
        return value
            .Replace(
                "\\\\",
                "\\",
                StringComparison.Ordinal)
            .Replace(
                "\\\"",
                "\"",
                StringComparison.Ordinal);
    }

    private static string GetProjectWadsDirectory(
        CompanionProjectSession session)
    {
        return Path.GetFullPath(
            Path.Combine(
                session.ProjectDirectory,
                CompanionProjectLayout.WadsDirectoryName));
    }

    private static bool FilesMatch(
        string firstPath,
        string secondPath)
    {
        FileInfo first =
            new(
                firstPath);

        FileInfo second =
            new(
                secondPath);

        if (first.Length !=
            second.Length)
        {
            return false;
        }

        string firstHash =
            ComputeSha256(
                firstPath);

        string secondHash =
            ComputeSha256(
                secondPath);

        return string.Equals(
            firstHash,
            secondHash,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(
        string path)
    {
        using FileStream stream =
            File.OpenRead(
                path);

        return Convert.ToHexString(
            SHA256.HashData(
                stream));
    }

    private static bool IsInsideDirectory(
        string path,
        string directory)
    {
        string fullPath =
            Path.GetFullPath(
                path);

        string fullDirectory =
            Path.GetFullPath(
                directory);

        string prefix =
            fullDirectory.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? fullDirectory
                : fullDirectory +
                    Path.DirectorySeparatorChar;

        return fullPath.StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase);
    }

    private static MapTextFile ReadMapTextFile(
        string mapPath)
    {
        byte[] bytes =
            File.ReadAllBytes(
                mapPath);

        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF)
        {
            UTF8Encoding encoding =
                new(
                    encoderShouldEmitUTF8Identifier: true,
                    throwOnInvalidBytes: true);

            return new MapTextFile(
                encoding.GetString(
                    bytes,
                    3,
                    bytes.Length - 3),
                encoding);
        }

        if (bytes.Length >= 2 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xFE)
        {
            UnicodeEncoding encoding =
                new(
                    bigEndian: false,
                    byteOrderMark: true,
                    throwOnInvalidBytes: true);

            return new MapTextFile(
                encoding.GetString(
                    bytes,
                    2,
                    bytes.Length - 2),
                encoding);
        }

        if (bytes.Length >= 2 &&
            bytes[0] == 0xFE &&
            bytes[1] == 0xFF)
        {
            UnicodeEncoding encoding =
                new(
                    bigEndian: true,
                    byteOrderMark: true,
                    throwOnInvalidBytes: true);

            return new MapTextFile(
                encoding.GetString(
                    bytes,
                    2,
                    bytes.Length - 2),
                encoding);
        }

        UTF8Encoding utf8 =
            new(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);

        return new MapTextFile(
            utf8.GetString(
                bytes),
            utf8);
    }

    private static void WriteMapTextFileAtomically(
        string mapPath,
        string text,
        Encoding encoding)
    {
        string temporaryPath =
            mapPath +
            ".companion-wadsync-" +
            Guid.NewGuid().ToString("N") +
            ".temporary";

        try
        {
            byte[] preamble =
                encoding.GetPreamble();

            byte[] textBytes =
                encoding.GetBytes(
                    text);

            byte[] output =
                new byte[
                    preamble.Length +
                    textBytes.Length];

            Buffer.BlockCopy(
                preamble,
                0,
                output,
                0,
                preamble.Length);

            Buffer.BlockCopy(
                textBytes,
                0,
                output,
                preamble.Length,
                textBytes.Length);

            File.WriteAllBytes(
                temporaryPath,
                output);

            File.Move(
                temporaryPath,
                mapPath,
                overwrite: true);
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

    private static string SetWorldspawnProperty(
        string mapText,
        string propertyName,
        string propertyValue)
    {
        (int entityOpen, int entityClose) =
            FindFirstEntityBounds(
                mapText);

        int propertyRegionStart =
            entityOpen +
            1;

        int propertyRegionEnd =
            FindFirstNestedOpeningBrace(
                mapText,
                propertyRegionStart,
                entityClose);

        string propertyRegion =
            mapText[
                propertyRegionStart..
                propertyRegionEnd];

        MatchCollection matches =
            WorldPropertyPattern.Matches(
                propertyRegion);

        Match? classnameMatch =
            matches
                .Cast<Match>()
                .FirstOrDefault(
                    match =>
                        string.Equals(
                            match.Groups["key"].Value,
                            "classname",
                            StringComparison.Ordinal));

        if (classnameMatch is null ||
            !string.Equals(
                classnameMatch.Groups["value"].Value,
                "worldspawn",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The first map entity is not a valid worldspawn entity.");
        }

        Match[] propertyMatches =
            matches
                .Cast<Match>()
                .Where(
                    match =>
                        string.Equals(
                            match.Groups["key"].Value,
                            propertyName,
                            StringComparison.Ordinal))
                .ToArray();

        if (propertyMatches.Length > 1)
        {
            throw new InvalidDataException(
                $"The worldspawn contains more than one '{propertyName}' property.");
        }

        string propertyLine =
            "\"" +
            propertyName +
            "\" \"" +
            EscapeMapPropertyValue(
                propertyValue) +
            "\"";

        if (propertyMatches.Length == 1)
        {
            Match existing =
                propertyMatches[0];

            int absoluteStart =
                propertyRegionStart +
                existing.Index;

            return mapText.Remove(
                    absoluteStart,
                    existing.Length)
                .Insert(
                    absoluteStart,
                    propertyLine);
        }

        string newline =
            DetectNewline(
                mapText);

        int insertionIndex =
            propertyRegionStart +
            classnameMatch.Index;

        return mapText.Insert(
            insertionIndex,
            propertyLine +
            newline);
    }

    private static (int Open, int Close) FindFirstEntityBounds(
        string text)
    {
        int open =
            FindNextStructuralBrace(
                text,
                0,
                '{');

        if (open < 0)
        {
            throw new InvalidDataException(
                "The map does not contain a worldspawn entity.");
        }

        int depth =
            0;

        bool inQuote =
            false;

        bool escaped =
            false;

        bool inLineComment =
            false;

        for (int index = open;
             index < text.Length;
             index++)
        {
            char character =
                text[index];

            char next =
                index + 1 < text.Length
                    ? text[index + 1]
                    : '\0';

            if (inLineComment)
            {
                if (character == '\n')
                {
                    inLineComment =
                        false;
                }

                continue;
            }

            if (inQuote)
            {
                if (escaped)
                {
                    escaped =
                        false;

                    continue;
                }

                if (character == '\\')
                {
                    escaped =
                        true;

                    continue;
                }

                if (character == '"')
                {
                    inQuote =
                        false;
                }

                continue;
            }

            if (character == '/' &&
                next == '/')
            {
                inLineComment =
                    true;

                index++;

                continue;
            }

            if (character == '"')
            {
                inQuote =
                    true;

                continue;
            }

            if (character == '{')
            {
                depth++;
            }
            else if (character == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return (
                        open,
                        index);
                }
            }
        }

        throw new InvalidDataException(
            "The worldspawn entity is missing a closing brace.");
    }

    private static int FindFirstNestedOpeningBrace(
        string text,
        int start,
        int entityClose)
    {
        bool inQuote =
            false;

        bool escaped =
            false;

        bool inLineComment =
            false;

        for (int index = start;
             index < entityClose;
             index++)
        {
            char character =
                text[index];

            char next =
                index + 1 < entityClose
                    ? text[index + 1]
                    : '\0';

            if (inLineComment)
            {
                if (character == '\n')
                {
                    inLineComment =
                        false;
                }

                continue;
            }

            if (inQuote)
            {
                if (escaped)
                {
                    escaped =
                        false;

                    continue;
                }

                if (character == '\\')
                {
                    escaped =
                        true;

                    continue;
                }

                if (character == '"')
                {
                    inQuote =
                        false;
                }

                continue;
            }

            if (character == '/' &&
                next == '/')
            {
                inLineComment =
                    true;

                index++;

                continue;
            }

            if (character == '"')
            {
                inQuote =
                    true;

                continue;
            }

            if (character == '{')
            {
                return index;
            }
        }

        return entityClose;
    }

    private static int FindNextStructuralBrace(
        string text,
        int start,
        char target)
    {
        bool inQuote =
            false;

        bool escaped =
            false;

        bool inLineComment =
            false;

        for (int index = start;
             index < text.Length;
             index++)
        {
            char character =
                text[index];

            char next =
                index + 1 < text.Length
                    ? text[index + 1]
                    : '\0';

            if (inLineComment)
            {
                if (character == '\n')
                {
                    inLineComment =
                        false;
                }

                continue;
            }

            if (inQuote)
            {
                if (escaped)
                {
                    escaped =
                        false;

                    continue;
                }

                if (character == '\\')
                {
                    escaped =
                        true;

                    continue;
                }

                if (character == '"')
                {
                    inQuote =
                        false;
                }

                continue;
            }

            if (character == '/' &&
                next == '/')
            {
                inLineComment =
                    true;

                index++;

                continue;
            }

            if (character == '"')
            {
                inQuote =
                    true;

                continue;
            }

            if (character == target)
            {
                return index;
            }
        }

        return -1;
    }

    private static string EscapeMapPropertyValue(
        string value)
    {
        return value
            .Replace(
                "\\",
                "\\\\",
                StringComparison.Ordinal)
            .Replace(
                "\"",
                "\\\"",
                StringComparison.Ordinal);
    }

    private static string DetectNewline(
        string text)
    {
        return text.Contains(
                "\r\n",
                StringComparison.Ordinal)
            ? "\r\n"
            : "\n";
    }

    private sealed record MapTextFile(
        string Text,
        Encoding Encoding);
}

public sealed record CompanionProjectWadImportResult(
    string WadPath,
    string Format,
    int LumpCount,
    bool CopiedIntoProject);

public sealed record CompanionProjectWadReconciliationResult(
    int ReferencedWadCount,
    int ImportedWadCount,
    bool Changed,
    IReadOnlyList<string> ManagedWadPaths,
    IReadOnlyList<string> Issues)
{
    public bool HasIssues =>
        Issues.Count >
        0;
}

public sealed record CompanionProjectWadSyncResult(
    int WadCount,
    bool Changed,
    IReadOnlyList<string> WadPaths);
