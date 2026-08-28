using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TrenchBroom.Companion.Core;

public static class CompanionImportedMapAssetService
{
    private static readonly Regex WorldPropertyPattern =
        new(
            @"(?m)^(?<indent>[ \t]*)""(?<key>[^""]*)""[ \t]+""(?<value>(?:\\.|[^""])*)""[ \t]*(?=\r?$)",
            RegexOptions.CultureInvariant);

    private static readonly Regex PlaceholderPropertyPattern =
        new(
            @"^property\s+\d+$",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant);

    private static readonly Regex FaceTexturePattern =
        new(
            @"(?m)^[ \t]*\([^)\r\n]+\)[ \t]+\([^)\r\n]+\)[ \t]+\([^)\r\n]+\)[ \t]+(?<texture>""[^""]+""|\S+)",
            RegexOptions.CultureInvariant);

    private static readonly HashSet<string> CompilerOnlyTextures =
        new(
            new[]
            {
                "clip",
                "hint",
                "hintskip",
                "skip",
                "trigger",
                "origin",
                "nodraw",
                "*waterskip",
                "*slimeskip",
                "*lavaskip"
            },
            StringComparer.OrdinalIgnoreCase);

    public static CompanionImportedMapAssetNormalizationResult NormalizeForDusk(
        CompanionProjectSession session,
        string mapPath,
        IEnumerable<string> selectedWadPaths,
        string duskPalettePath)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        ArgumentNullException.ThrowIfNull(
            selectedWadPaths);

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

        MapTextFile mapFile =
            ReadMapTextFile(
                fullMapPath);

        string? wadProperty =
            GetWorldspawnProperty(
                mapFile.Text,
                "wad");

        List<string> referencedWads =
            SplitWadReferences(
                wadProperty);

        List<string> managedOrder =
            new();

        List<string> missingReferences =
            new();

        List<string> invalidReferences =
            new();

        int importedWadCount =
            0;

        foreach (string selectedWadPath in
                 selectedWadPaths)
        {
            if (string.IsNullOrWhiteSpace(
                    selectedWadPath))
            {
                continue;
            }

            try
            {
                string fullWadPath =
                    Path.GetFullPath(
                        selectedWadPath);

                if (!File.Exists(
                        fullWadPath))
                {
                    missingReferences.Add(
                        fullWadPath);

                    continue;
                }

                AddUnique(
                    managedOrder,
                    fullWadPath);
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    ArgumentException or
                    NotSupportedException)
            {
                invalidReferences.Add(
                    $"{selectedWadPath} -> {exception.Message}");
            }
        }
        string normalizedText =
            RemoveStaleWorldspawnMetadata(
                mapFile.Text,
                out int removedMetadataPropertyCount);

        string managedWadProperty =
            string.Join(
                ";",
                managedOrder.Select(
                    path =>
                        path.Replace(
                            '\\',
                            '/')));

        normalizedText =
            SetWorldspawnProperty(
                normalizedText,
                "wad",
                managedWadProperty);

        bool mapChanged =
            !string.Equals(
                normalizedText,
                mapFile.Text,
                StringComparison.Ordinal);

        if (mapChanged)
        {
            WriteMapTextFileAtomically(
                fullMapPath,
                normalizedText,
                mapFile.Encoding);
        }

        MapTextFile normalizedMapFile =
            mapChanged
                ? ReadMapTextFile(
                    fullMapPath)
                : mapFile;

        string[] usedTextures =
            ExtractUsedTextureNames(
                normalizedMapFile.Text);

        byte[]? duskPalette =
            ReadDuskPalette(
                duskPalettePath);

        List<WadArchiveIndex> wadIndexes =
            new();

        foreach (string managedWad in
                 managedOrder)
        {
            try
            {
                wadIndexes.Add(
                    ReadWadIndex(
                        managedWad));
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    InvalidDataException)
            {
                invalidReferences.Add(
                    $"{managedWad} -> {exception.Message}");
            }
        }

        List<string> missingTextures =
            new();

        List<string> duplicateProviders =
            new();

        List<string> paletteMismatchTextures =
            new();

        List<string> paletteUnknownTextures =
            new();

        List<string> provenanceLines =
            new();

        foreach (string textureName in
                 usedTextures)
        {
            if (CompilerOnlyTextures.Contains(
                    textureName))
            {
                provenanceLines.Add(
                    $"{textureName}: compiler-only texture");

                continue;
            }

            List<WadArchiveIndex> providers =
                wadIndexes
                    .Where(
                        index =>
                            index.Lumps.ContainsKey(
                                textureName))
                    .ToList();

            if (providers.Count == 0)
            {
                missingTextures.Add(
                    textureName);

                provenanceLines.Add(
                    $"{textureName}: MISSING");

                continue;
            }

            WadArchiveIndex primary =
                providers[0];

            provenanceLines.Add(
                $"{textureName}: {Path.GetFileName(primary.Path)} ({primary.Format})");

            if (providers.Count > 1)
            {
                duplicateProviders.Add(
                    textureName +
                    ": " +
                    string.Join(
                        ", ",
                        providers.Select(
                            provider =>
                                $"{Path.GetFileName(provider.Path)} ({provider.Format})")));
            }

            if (!string.Equals(
                    primary.Format,
                    "WAD3",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool? paletteMatches =
                duskPalette is null
                    ? null
                    : TryTexturePaletteMatches(
                        primary,
                        textureName,
                        duskPalette);

            if (paletteMatches == false)
            {
                paletteMismatchTextures.Add(
                    $"{textureName}: {Path.GetFileName(primary.Path)}");
            }
            else if (paletteMatches is null)
            {
                paletteUnknownTextures.Add(
                    $"{textureName}: {Path.GetFileName(primary.Path)}");
            }
        }

        string reportPath =
            WriteAssetReport(
                session,
                fullMapPath,
                referencedWads,
                managedOrder,
                wadIndexes,
                importedWadCount,
                missingReferences,
                invalidReferences,
                removedMetadataPropertyCount,
                usedTextures,
                missingTextures,
                duplicateProviders,
                paletteMismatchTextures,
                paletteUnknownTextures,
                provenanceLines,
                duskPalettePath);

        bool normalizationChanged =
            mapChanged ||
            importedWadCount > 0 ||
            removedMetadataPropertyCount > 0;

        return new CompanionImportedMapAssetNormalizationResult(
            referencedWads.Count,
            importedWadCount,
            managedOrder.Count,
            removedMetadataPropertyCount,
            missingReferences,
            invalidReferences,
            missingTextures,
            duplicateProviders,
            paletteMismatchTextures,
            paletteUnknownTextures,
            mapChanged,
            normalizationChanged,
            reportPath);
    }

    private static WadResolution ResolveReferencedWad(
        string reference,
        string mapPath,
        IReadOnlyList<string> projectWads,
        IReadOnlyList<string> registeredWads)
    {
        string normalizedReference =
            reference
                .Trim()
                .Trim('"');

        if (normalizedReference.Length == 0)
        {
            return new WadResolution(
                null,
                "An empty WAD reference was ignored.");
        }

        string platformReference =
            normalizedReference
                .Replace(
                    '/',
                    Path.DirectorySeparatorChar)
                .Replace(
                    '\\',
                    Path.DirectorySeparatorChar);

        try
        {
            string directPath =
                Path.GetFullPath(
                    platformReference);

            if (File.Exists(
                    directPath))
            {
                return new WadResolution(
                    directPath,
                    null);
            }
        }
        catch
        {
            // Continue through the safer recovery candidates below.
        }

        if (!Path.IsPathRooted(
                platformReference))
        {
            string? mapDirectory =
                Path.GetDirectoryName(
                    mapPath);

            if (!string.IsNullOrWhiteSpace(
                    mapDirectory))
            {
                try
                {
                    string relativeToMap =
                        Path.GetFullPath(
                            Path.Combine(
                                mapDirectory,
                                platformReference));

                    if (File.Exists(
                            relativeToMap))
                    {
                        return new WadResolution(
                            relativeToMap,
                            null);
                    }
                }
                catch
                {
                    // Continue to filename recovery.
                }
            }
        }

        string fileName =
            Path.GetFileName(
                platformReference);

        if (string.IsNullOrWhiteSpace(
                fileName))
        {
            return new WadResolution(
                null,
                $"Could not resolve WAD reference '{reference}'.");
        }

        List<string> projectMatches =
            projectWads
                .Where(
                    path =>
                        File.Exists(
                            path) &&
                        string.Equals(
                            Path.GetFileName(
                                path),
                            fileName,
                            StringComparison.OrdinalIgnoreCase))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (projectMatches.Count == 1)
        {
            return new WadResolution(
                projectMatches[0],
                null);
        }

        if (projectMatches.Count > 1)
        {
            return new WadResolution(
                null,
                $"WAD reference '{reference}' matched more than one project WAD named '{fileName}'.");
        }

        List<string> registeredMatches =
            registeredWads
                .Where(
                    path =>
                        string.Equals(
                            Path.GetFileName(
                                path),
                            fileName,
                            StringComparison.OrdinalIgnoreCase))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (registeredMatches.Count == 1)
        {
            return new WadResolution(
                registeredMatches[0],
                null);
        }

        if (registeredMatches.Count > 1)
        {
            return new WadResolution(
                null,
                $"WAD reference '{reference}' matched more than one registered WAD named '{fileName}'.");
        }

        return new WadResolution(
            null,
            $"Missing WAD: {reference}");
    }

    private static List<string> SplitWadReferences(
        string? wadProperty)
    {
        if (string.IsNullOrWhiteSpace(
                wadProperty))
        {
            return new List<string>();
        }

        return wadProperty
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(
                value =>
                    value.Length > 0)
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string RemoveStaleWorldspawnMetadata(
        string mapText,
        out int removedCount)
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

        string propertyScanRegion =
            MaskCommentsPreservingLayout(
                propertyRegion);

        MatchCollection matches =
            WorldPropertyPattern.Matches(
                propertyScanRegion);

        Match? classname =
            matches
                .Cast<Match>()
                .FirstOrDefault(
                    match =>
                        string.Equals(
                            match.Groups["key"].Value,
                            "classname",
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            UnescapeMapPropertyValue(
                                match.Groups["value"].Value),
                            "worldspawn",
                            StringComparison.OrdinalIgnoreCase));

        if (classname is null)
        {
            throw new InvalidDataException(
                "The first map entity is not a valid worldspawn entity.");
        }

        List<Match> removals =
            matches
                .Cast<Match>()
                .Where(
                    match =>
                    {
                        string key =
                            UnescapeMapPropertyValue(
                                match.Groups["key"].Value);

                        string value =
                            UnescapeMapPropertyValue(
                                match.Groups["value"].Value);

                        return
                            string.Equals(
                                key,
                                "_tb_def",
                                StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(
                                key,
                                "_tb_mod",
                                StringComparison.OrdinalIgnoreCase) ||
                            string.IsNullOrWhiteSpace(
                                key) ||
                            (
                                string.IsNullOrWhiteSpace(
                                    value) &&
                                PlaceholderPropertyPattern.IsMatch(
                                    key)
                            );
                    })
                .OrderByDescending(
                    match =>
                        match.Index)
                .ToList();

        string updated =
            mapText;

        foreach (Match removal in
                 removals)
        {
            int absoluteStart =
                propertyRegionStart +
                removal.Index;

            updated =
                updated.Remove(
                    absoluteStart,
                    removal.Length);
        }

        removedCount =
            removals.Count;

        return updated;
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

        string propertyScanRegion =
            MaskCommentsPreservingLayout(
                propertyRegion);

        MatchCollection matches =
            WorldPropertyPattern.Matches(
                propertyScanRegion);

        Match? classname =
            matches
                .Cast<Match>()
                .FirstOrDefault(
                    match =>
                        string.Equals(
                            match.Groups["key"].Value,
                            "classname",
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            UnescapeMapPropertyValue(
                                match.Groups["value"].Value),
                            "worldspawn",
                            StringComparison.OrdinalIgnoreCase));

        if (classname is null)
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
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

        if (propertyMatches.Length > 1)
        {
            throw new InvalidDataException(
                $"The worldspawn contains more than one '{propertyName}' property.");
        }

        return propertyMatches.Length == 0
            ? null
            : UnescapeMapPropertyValue(
                propertyMatches[0]
                    .Groups["value"]
                    .Value);
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

        string propertyScanRegion =
            MaskCommentsPreservingLayout(
                propertyRegion);

        MatchCollection matches =
            WorldPropertyPattern.Matches(
                propertyScanRegion);

        Match? classnameMatch =
            matches
                .Cast<Match>()
                .FirstOrDefault(
                    match =>
                        string.Equals(
                            match.Groups["key"].Value,
                            "classname",
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            UnescapeMapPropertyValue(
                                match.Groups["value"].Value),
                            "worldspawn",
                            StringComparison.OrdinalIgnoreCase));

        if (classnameMatch is null)
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
                            StringComparison.OrdinalIgnoreCase))
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

            return mapText
                .Remove(
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

    private static string[] ExtractUsedTextureNames(
        string mapText)
    {
        return FaceTexturePattern
            .Matches(
                mapText)
            .Cast<Match>()
            .Select(
                match =>
                    match.Groups["texture"]
                        .Value
                        .Trim()
                        .Trim('"'))
            .Where(
                name =>
                    name.Length > 0)
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                name =>
                    name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WadArchiveIndex ReadWadIndex(
        string wadPath)
    {
        string fullPath =
            Path.GetFullPath(
                wadPath);

        using FileStream stream =
            File.Open(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        using BinaryReader reader =
            new(
                stream,
                Encoding.ASCII,
                leaveOpen: true);

        if (stream.Length < 12)
        {
            throw new InvalidDataException(
                $"WAD '{Path.GetFileName(fullPath)}' is too small.");
        }

        string magic =
            Encoding.ASCII.GetString(
                reader.ReadBytes(
                    4));

        string format =
            magic switch
            {
                "WAD2" => "WAD2",
                "WAD3" => "WAD3",
                _ => throw new InvalidDataException(
                    $"'{Path.GetFileName(fullPath)}' is not WAD2 or WAD3.")
            };

        int lumpCount =
            reader.ReadInt32();

        int directoryOffset =
            reader.ReadInt32();

        if (lumpCount < 0 ||
            lumpCount > 1_000_000)
        {
            throw new InvalidDataException(
                $"WAD '{Path.GetFileName(fullPath)}' has an invalid lump count.");
        }

        long directoryLength =
            (long)lumpCount *
            32L;

        if (directoryOffset < 12 ||
            directoryOffset +
                directoryLength >
            stream.Length)
        {
            throw new InvalidDataException(
                $"WAD '{Path.GetFileName(fullPath)}' has an invalid directory.");
        }

        Dictionary<string, WadLumpInfo> lumps =
            new(
                StringComparer.OrdinalIgnoreCase);

        stream.Position =
            directoryOffset;

        for (int index = 0;
             index < lumpCount;
             index++)
        {
            int fileOffset =
                reader.ReadInt32();

            int diskSize =
                reader.ReadInt32();

            int fullSize =
                reader.ReadInt32();

            byte type =
                reader.ReadByte();

            byte compression =
                reader.ReadByte();

            reader.ReadUInt16();

            byte[] nameBytes =
                reader.ReadBytes(
                    16);

            if (nameBytes.Length !=
                16)
            {
                throw new InvalidDataException(
                    $"WAD '{Path.GetFileName(fullPath)}' ended inside its directory.");
            }

            int nameLength =
                Array.IndexOf(
                    nameBytes,
                    (byte)0);

            if (nameLength < 0)
            {
                nameLength =
                    nameBytes.Length;
            }

            string name =
                Encoding.ASCII
                    .GetString(
                        nameBytes,
                        0,
                        nameLength)
                    .Trim();

            if (name.Length == 0 ||
                fileOffset < 0 ||
                diskSize < 0 ||
                fullSize < 0 ||
                (long)fileOffset +
                    diskSize >
                stream.Length)
            {
                continue;
            }

            if (!lumps.ContainsKey(
                    name))
            {
                lumps.Add(
                    name,
                    new WadLumpInfo(
                        fileOffset,
                        diskSize,
                        fullSize,
                        type,
                        compression));
            }
        }

        return new WadArchiveIndex(
            fullPath,
            format,
            lumps);
    }

    private static bool? TryTexturePaletteMatches(
        WadArchiveIndex wad,
        string textureName,
        byte[] duskPalette)
    {
        if (!string.Equals(
                wad.Format,
                "WAD3",
                StringComparison.OrdinalIgnoreCase) ||
            !wad.Lumps.TryGetValue(
                textureName,
                out WadLumpInfo? lump) ||
            lump is null ||
            lump.Compression != 0 ||
            duskPalette.Length != 768)
        {
            return null;
        }

        try
        {
            using FileStream stream =
                File.Open(
                    wad.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

            using BinaryReader reader =
                new(
                    stream,
                    Encoding.ASCII,
                    leaveOpen: true);

            if (lump.FileOffset < 0 ||
                lump.DiskSize < 40 ||
                (long)lump.FileOffset +
                    lump.DiskSize >
                stream.Length)
            {
                return null;
            }

            stream.Position =
                lump.FileOffset;

            byte[] textureNameBytes =
                reader.ReadBytes(
                    16);

            if (textureNameBytes.Length !=
                16)
            {
                return null;
            }

            uint width =
                reader.ReadUInt32();

            uint height =
                reader.ReadUInt32();

            uint[] offsets =
            {
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32()
            };

            if (width == 0 ||
                height == 0 ||
                width > 65536 ||
                height > 65536 ||
                offsets[3] < 40)
            {
                return null;
            }

            long mip3Width =
                Math.Max(
                    1L,
                    (long)width /
                    8L);

            long mip3Height =
                Math.Max(
                    1L,
                    (long)height /
                    8L);

            long paletteCountOffset =
                (long)lump.FileOffset +
                offsets[3] +
                mip3Width *
                mip3Height;

            long lumpEnd =
                (long)lump.FileOffset +
                lump.DiskSize;

            if (paletteCountOffset < 0 ||
                paletteCountOffset +
                    2L >
                lumpEnd)
            {
                return null;
            }

            stream.Position =
                paletteCountOffset;

            ushort colorCount =
                reader.ReadUInt16();

            if (colorCount != 256 ||
                stream.Position +
                    768L >
                lumpEnd)
            {
                return null;
            }

            byte[] texturePalette =
                reader.ReadBytes(
                    768);

            if (texturePalette.Length !=
                768)
            {
                return null;
            }

            return texturePalette.SequenceEqual(
                duskPalette);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? ReadDuskPalette(
        string palettePath)
    {
        if (string.IsNullOrWhiteSpace(
                palettePath))
        {
            return null;
        }

        try
        {
            string fullPath =
                Path.GetFullPath(
                    palettePath);

            if (!File.Exists(
                    fullPath))
            {
                return null;
            }

            byte[] palette =
                File.ReadAllBytes(
                    fullPath);

            return palette.Length ==
                768
                ? palette
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string WriteAssetReport(
        CompanionProjectSession session,
        string mapPath,
        IReadOnlyList<string> referencedWads,
        IReadOnlyList<string> managedWads,
        IReadOnlyList<WadArchiveIndex> wadIndexes,
        int importedWadCount,
        IReadOnlyList<string> missingReferences,
        IReadOnlyList<string> invalidReferences,
        int removedMetadataPropertyCount,
        IReadOnlyList<string> usedTextures,
        IReadOnlyList<string> missingTextures,
        IReadOnlyList<string> duplicateProviders,
        IReadOnlyList<string> paletteMismatchTextures,
        IReadOnlyList<string> paletteUnknownTextures,
        IReadOnlyList<string> provenanceLines,
        string duskPalettePath)
    {
        string buildDirectory =
            Path.Combine(
                session.ProjectDirectory,
                CompanionProjectLayout.BuildDirectoryName);

        Directory.CreateDirectory(
            buildDirectory);

        string reportPath =
            Path.Combine(
                buildDirectory,
                Path.GetFileNameWithoutExtension(
                    mapPath) +
                "-asset-report.txt");

        Dictionary<string, string> formats =
            wadIndexes.ToDictionary(
                index =>
                    index.Path,
                index =>
                    index.Format,
                StringComparer.OrdinalIgnoreCase);

        StringBuilder report =
            new();

        report.AppendLine(
            "TrenchBroom Companion - Imported Map Asset Report");

        report.AppendLine(
            "Map: " +
            mapPath);

        report.AppendLine(
            "Generated: " +
            DateTimeOffset.Now.ToString(
                "O"));

        report.AppendLine();

        report.AppendLine(
            $"Original WAD references: {referencedWads.Count}");

        foreach (string reference in
                 referencedWads)
        {
            report.AppendLine(
                "  " +
                reference);
        }

        report.AppendLine();

        report.AppendLine(
            $"Managed project WADs: {managedWads.Count} (newly copied: {importedWadCount})");

        for (int index = 0;
             index < managedWads.Count;
             index++)
        {
            string wad =
                managedWads[index];

            string format =
                formats.TryGetValue(
                    wad,
                    out string? foundFormat)
                    ? foundFormat
                    : "Unknown";

            report.AppendLine(
                $"  {index + 1}. {wad} [{format}]");
        }

        report.AppendLine();

        report.AppendLine(
            $"Stale TrenchBroom/worldspawn properties removed: {removedMetadataPropertyCount}");

        WriteList(
            report,
            "Unresolved WAD references",
            missingReferences);

        WriteList(
            report,
            "Invalid/conflicting WAD references",
            invalidReferences);

        report.AppendLine();

        report.AppendLine(
            $"Used map textures: {usedTextures.Count}");

        WriteList(
            report,
            "Missing textures",
            missingTextures);

        WriteList(
            report,
            "Textures with multiple WAD providers",
            duplicateProviders);

        WriteList(
            report,
            "Used WAD3 textures with an embedded palette different from the DUSK global palette (valid WAD3)",
            paletteMismatchTextures);

        WriteList(
            report,
            "Used WAD3 textures whose palette could not be verified",
            paletteUnknownTextures);

        report.AppendLine();

        report.AppendLine(
            "Texture provenance (managed WAD order):");

        foreach (string line in
                 provenanceLines)
        {
            report.AppendLine(
                "  " +
                line);
        }

        report.AppendLine();

        report.AppendLine(
            "Managed DUSK palette: " +
            duskPalettePath);

        report.AppendLine(
            "Note: a WAD3 palette mismatch is diagnostic evidence only. " +
            "This step does not silently recolor or downgrade the source texture.");

        WriteTextAtomically(
            reportPath,
            report.ToString());

        return reportPath;
    }

    private static void WriteList(
        StringBuilder report,
        string title,
        IReadOnlyList<string> values)
    {
        report.AppendLine();

        report.AppendLine(
            $"{title}: {values.Count}");

        foreach (string value in
                 values)
        {
            report.AppendLine(
                "  " +
                value);
        }
    }

    private static void AddUnique(
        List<string> values,
        string value)
    {
        if (values.Any(
                existing =>
                    string.Equals(
                        existing,
                        value,
                        StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        values.Add(
            value);
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

    private static string UnescapeMapPropertyValue(
        string value)
    {
        StringBuilder output =
            new(
                value.Length);

        bool escaped =
            false;

        foreach (char character in
                 value)
        {
            if (escaped)
            {
                output.Append(
                    character);

                escaped =
                    false;

                continue;
            }

            if (character ==
                '\\')
            {
                escaped =
                    true;

                continue;
            }

            output.Append(
                character);
        }

        if (escaped)
        {
            output.Append(
                '\\');
        }

        return output.ToString();
    }

﻿    private static (int Open, int Close) FindFirstEntityBounds(
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

        int close =
            FindMatchingStructuralBrace(
                text,
                open);

        if (close < 0)
        {
            throw new InvalidDataException(
                "The worldspawn entity is missing a closing brace.");
        }

        return (
            open,
            close);
    }

    private static int FindMatchingStructuralBrace(
        string text,
        int openingBrace)
    {
        int depth =
            0;

        bool inQuote =
            false;

        bool escaped =
            false;

        bool inLineComment =
            false;

        bool inBlockComment =
            false;

        for (int index = openingBrace;
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

            if (inBlockComment)
            {
                if (character == '*' &&
                    next == '/')
                {
                    inBlockComment =
                        false;

                    index++;
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

            if (character == '/' &&
                next == '*')
            {
                inBlockComment =
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

            if (character == '{' &&
                IsStructuralBraceToken(
                    text,
                    index))
            {
                depth++;

                continue;
            }

            if (character != '}' ||
                !IsStructuralBraceToken(
                    text,
                    index))
            {
                continue;
            }

            depth--;

            if (depth == 0)
            {
                return index;
            }

            if (depth < 0)
            {
                return -1;
            }
        }

        return -1;
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

        bool inBlockComment =
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

            if (inBlockComment)
            {
                if (character == '*' &&
                    next == '/')
                {
                    inBlockComment =
                        false;

                    index++;
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

            if (character == '/' &&
                next == '*')
            {
                inBlockComment =
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

            if (character == '{' &&
                IsStructuralBraceToken(
                    text,
                    index))
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

        bool inBlockComment =
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

            if (inBlockComment)
            {
                if (character == '*' &&
                    next == '/')
                {
                    inBlockComment =
                        false;

                    index++;
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

            if (character == '/' &&
                next == '*')
            {
                inBlockComment =
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

            if (character == target &&
                IsStructuralBraceToken(
                    text,
                    index))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsStructuralBraceToken(
        string text,
        int index)
    {
        if (index < 0 ||
            index >= text.Length ||
            (text[index] != '{' &&
             text[index] != '}'))
        {
            return false;
        }

        bool leftBoundary =
            index == 0 ||
            char.IsWhiteSpace(
                text[index - 1]) ||
            text[index - 1] == '{' ||
            text[index - 1] == '}';

        if (!leftBoundary)
        {
            return false;
        }

        if (index + 1 >= text.Length)
        {
            return true;
        }

        char next =
            text[index + 1];

        if (char.IsWhiteSpace(
                next) ||
            next == '{' ||
            next == '}')
        {
            return true;
        }

        if (next != '/' ||
            index + 2 >= text.Length)
        {
            return false;
        }

        char commentKind =
            text[index + 2];

        return commentKind == '/' ||
            commentKind == '*';
    }

    private static string MaskCommentsPreservingLayout(
        string text)
    {
        char[] characters =
            text.ToCharArray();

        bool inQuote =
            false;

        bool escaped =
            false;

        bool inLineComment =
            false;

        bool inBlockComment =
            false;

        for (int index = 0;
             index < characters.Length;
             index++)
        {
            char character =
                characters[index];

            char next =
                index + 1 < characters.Length
                    ? characters[index + 1]
                    : '\0';

            if (inLineComment)
            {
                if (character == '\n')
                {
                    inLineComment =
                        false;
                }
                else if (character != '\r')
                {
                    characters[index] =
                        ' ';
                }

                continue;
            }

            if (inBlockComment)
            {
                if (character == '*' &&
                    next == '/')
                {
                    characters[index] =
                        ' ';

                    characters[index + 1] =
                        ' ';

                    inBlockComment =
                        false;

                    index++;

                    continue;
                }

                if (character != '\r' &&
                    character != '\n')
                {
                    characters[index] =
                        ' ';
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
                characters[index] =
                    ' ';

                characters[index + 1] =
                    ' ';

                inLineComment =
                    true;

                index++;

                continue;
            }

            if (character == '/' &&
                next == '*')
            {
                characters[index] =
                    ' ';

                characters[index + 1] =
                    ' ';

                inBlockComment =
                    true;

                index++;

                continue;
            }

            if (character == '"')
            {
                inQuote =
                    true;
            }
        }

        return new string(
            characters);
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
                    bytes.Length -
                    3),
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
                    bytes.Length -
                    2),
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
                    bytes.Length -
                    2),
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
            ".tbcompanion-assets-" +
            Guid.NewGuid().ToString(
                "N");

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
            try
            {
                if (File.Exists(
                        temporaryPath))
                {
                    File.Delete(
                        temporaryPath);
                }
            }
            catch
            {
                // Preserve the original map update result.
            }
        }
    }

    private static void WriteTextAtomically(
        string path,
        string text)
    {
        string temporaryPath =
            path +
            ".temporary-" +
            Guid.NewGuid().ToString(
                "N");

        try
        {
            File.WriteAllText(
                temporaryPath,
                text,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: true));

            File.Move(
                temporaryPath,
                path,
                overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(
                        temporaryPath))
                {
                    File.Delete(
                        temporaryPath);
                }
            }
            catch
            {
                // Preserve the report-writing result.
            }
        }
    }

    private static string DetectNewline(
        string text)
    {
        int newline =
            text.IndexOf(
                '\n');

        if (newline < 0)
        {
            return Environment.NewLine;
        }

        return newline > 0 &&
            text[newline - 1] ==
                '\r'
            ? "\r\n"
            : "\n";
    }

    private sealed record MapTextFile(
        string Text,
        Encoding Encoding);

    private sealed record WadResolution(
        string? ResolvedPath,
        string? Problem);

    private sealed record WadLumpInfo(
        int FileOffset,
        int DiskSize,
        int FullSize,
        byte Type,
        byte Compression);

    private sealed record WadArchiveIndex(
        string Path,
        string Format,
        Dictionary<string, WadLumpInfo> Lumps);
}

public sealed record CompanionImportedMapAssetNormalizationResult(
    int ReferencedWadCount,
    int ImportedWadCount,
    int ManagedWadCount,
    int RemovedMetadataPropertyCount,
    IReadOnlyList<string> MissingWadReferences,
    IReadOnlyList<string> InvalidWadReferences,
    IReadOnlyList<string> MissingTextureNames,
    IReadOnlyList<string> DuplicateTextureProviders,
    IReadOnlyList<string> DuskPaletteMismatchTextures,
    IReadOnlyList<string> DuskPaletteUnknownTextures,
    bool MapChanged,
    bool NormalizationChanged,
    string ReportPath)
{
    public bool HasWarnings =>
        MissingWadReferences.Count > 0 ||
        InvalidWadReferences.Count > 0 ||
        MissingTextureNames.Count > 0 ||
        DuplicateTextureProviders.Count > 0;
}
