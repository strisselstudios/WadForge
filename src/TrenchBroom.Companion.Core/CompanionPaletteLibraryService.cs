using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WadForge.Aliases;

namespace TrenchBroom.Companion.Core;

public sealed record CompanionPaletteLibraryAsset(
    string AssetId,
    string PalettePath,
    string DisplayName,
    IReadOnlyList<string> Sources);

public sealed record CompanionPaletteResolution(
    string WadSha256,
    string? PaletteAssetId,
    string? PalettePath,
    string Description,
    bool IsRemembered);

public sealed class CompanionPaletteLibraryService
{
    private const int PaletteByteLength = 768;
    private const int IndexSchemaVersion = 1;
    private const string PaletteDirectoryName = "Palettes";
    private const string IndexFileName = "palette-library.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

    public string GetLibraryDirectory(
        string managedDataRoot)
    {
        if (string.IsNullOrWhiteSpace(
                managedDataRoot))
        {
            throw new ArgumentException(
                "A Companion managed data root is required.",
                nameof(managedDataRoot));
        }

        return Path.Combine(
            CompanionManagedDataRootService.GetAssetsDirectory(
                managedDataRoot),
            PaletteDirectoryName);
    }

    public IReadOnlyList<CompanionPaletteLibraryAsset> GetAssets(
        string managedDataRoot)
    {
        string directory =
            EnsureLibraryDirectory(
                managedDataRoot);

        CompanionPaletteLibraryIndex index =
            ReadIndex(
                directory);

        bool changed =
            false;

        List<CompanionPaletteLibraryStoredAsset> retained =
            new();

        foreach (CompanionPaletteLibraryStoredAsset stored in
                 index.Assets)
        {
            string path =
                Path.Combine(
                    directory,
                    stored.FileName);

            if (!IsValidPaletteFile(
                    path))
            {
                changed =
                    true;

                continue;
            }

            retained.Add(
                stored);
        }

        if (changed)
        {
            index.Assets =
                retained;

            HashSet<string> validIds =
                retained
                    .Select(
                        asset =>
                            asset.AssetId)
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            foreach (string wadId in
                     index.WadAssociations.Keys.ToArray())
            {
                if (!validIds.Contains(
                        index.WadAssociations[wadId]))
                {
                    index.WadAssociations.Remove(
                        wadId);
                }
            }

            WriteIndex(
                directory,
                index);
        }

        return retained
            .Select(
                stored =>
                    new CompanionPaletteLibraryAsset(
                        stored.AssetId,
                        Path.Combine(
                            directory,
                            stored.FileName),
                        stored.DisplayName,
                        stored.Sources
                            .OrderBy(
                                source =>
                                    source,
                                StringComparer.OrdinalIgnoreCase)
                            .ToArray()))
            .OrderBy(
                asset =>
                    asset.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                asset =>
                    asset.AssetId,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public CompanionPaletteLibraryAsset ImportFile(
        string managedDataRoot,
        string sourcePath,
        string displayName,
        string sourceDescription)
    {
        if (string.IsNullOrWhiteSpace(
                sourcePath))
        {
            throw new ArgumentException(
                "A palette path is required.",
                nameof(sourcePath));
        }

        string fullPath =
            Path.GetFullPath(
                sourcePath);

        if (!IsValidPaletteFile(
                fullPath))
        {
            throw new InvalidDataException(
                "A WAD2 preview palette must contain exactly 768 bytes: 256 RGB colors.");
        }

        return ImportBytes(
            managedDataRoot,
            File.ReadAllBytes(
                fullPath),
            displayName,
            sourceDescription);
    }

    public CompanionPaletteLibraryAsset ImportBytes(
        string managedDataRoot,
        byte[] paletteBytes,
        string displayName,
        string sourceDescription)
    {
        ArgumentNullException.ThrowIfNull(
            paletteBytes);

        if (paletteBytes.Length !=
            PaletteByteLength)
        {
            throw new InvalidDataException(
                "A WAD2 preview palette must contain exactly 768 bytes: 256 RGB colors.");
        }

        string directory =
            EnsureLibraryDirectory(
                managedDataRoot);

        string sha256 =
            Convert.ToHexString(
                SHA256.HashData(
                    paletteBytes));

        CompanionPaletteLibraryIndex index =
            ReadIndex(
                directory);

        CompanionPaletteLibraryStoredAsset? existing =
            index.Assets.FirstOrDefault(
                asset =>
                    string.Equals(
                        asset.AssetId,
                        sha256,
                        StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            string existingPath =
                Path.Combine(
                    directory,
                    existing.FileName);

            if (!File.Exists(
                    existingPath))
            {
                File.WriteAllBytes(
                    existingPath,
                    paletteBytes);
            }

            bool changed =
                AddSource(
                    existing,
                    sourceDescription);

            if (changed)
            {
                WriteIndex(
                    directory,
                    index);
            }

            return new CompanionPaletteLibraryAsset(
                existing.AssetId,
                existingPath,
                existing.DisplayName,
                existing.Sources.ToArray());
        }

        string safeName =
            MakeSafeFileStem(
                displayName);

        string fileName =
            $"{safeName}-{sha256[..8].ToLowerInvariant()}.lmp";

        string destination =
            Path.Combine(
                directory,
                fileName);

        File.WriteAllBytes(
            destination,
            paletteBytes);

        CompanionPaletteLibraryStoredAsset stored =
            new()
            {
                AssetId =
                    sha256,

                FileName =
                    fileName,

                DisplayName =
                    string.IsNullOrWhiteSpace(
                        displayName)
                        ? "Palette"
                        : displayName.Trim(),

                Sources =
                    new List<string>()
            };

        AddSource(
            stored,
            sourceDescription);

        index.Assets.Add(
            stored);

        WriteIndex(
            directory,
            index);

        return new CompanionPaletteLibraryAsset(
            stored.AssetId,
            destination,
            stored.DisplayName,
            stored.Sources.ToArray());
    }

    public CompanionPaletteLibraryAsset? EnsureQuakePalette(
        string managedDataRoot,
        string installationDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                installationDirectory) ||
            !Directory.Exists(
                installationDirectory))
        {
            return null;
        }

        string fullInstallation =
            Path.GetFullPath(
                installationDirectory);

        string loosePalette =
            Path.Combine(
                fullInstallation,
                "id1",
                "gfx",
                "palette.lmp");

        if (IsValidPaletteFile(
                loosePalette))
        {
            return ImportFile(
                managedDataRoot,
                loosePalette,
                "Quake",
                $"Quake installation: {fullInstallation}");
        }

        string id1 =
            Path.Combine(
                fullInstallation,
                "id1");

        if (!Directory.Exists(
                id1))
        {
            return null;
        }

        IEnumerable<string> pakFiles;

        try
        {
            pakFiles =
                Directory
                    .EnumerateFiles(
                        id1,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .Where(
                        path =>
                            string.Equals(
                                Path.GetExtension(path),
                                ".pak",
                                StringComparison.OrdinalIgnoreCase))
                    .OrderBy(
                        path =>
                            string.Equals(
                                Path.GetFileName(path),
                                "pak0.pak",
                                StringComparison.OrdinalIgnoreCase)
                                ? 0
                                : 1)
                    .ThenBy(
                        path =>
                            Path.GetFileName(path),
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }
        catch
        {
            return null;
        }

        foreach (string pakPath in
                 pakFiles)
        {
            if (!TryReadPakEntry(
                    pakPath,
                    "gfx/palette.lmp",
                    out byte[]? palette) ||
                palette is null ||
                palette.Length !=
                    PaletteByteLength)
            {
                continue;
            }

            return ImportBytes(
                managedDataRoot,
                palette,
                "Quake",
                $"Extracted from {Path.GetFileName(pakPath)} in {fullInstallation}");
        }

        return null;
    }

    public CompanionPaletteLibraryAsset? EnsureDuskPalette(
        string managedDataRoot,
        string? palettePath)
    {
        if (string.IsNullOrWhiteSpace(
                palettePath) ||
            !IsValidPaletteFile(
                palettePath))
        {
            return null;
        }

        return ImportFile(
            managedDataRoot,
            palettePath,
            "DUSK",
            "Verified DUSK authoring palette");
    }

    public void ImportEvidenceForSourceWad(
        string managedDataRoot,
        string sourceWadPath)
    {
        if (string.IsNullOrWhiteSpace(
                sourceWadPath) ||
            !File.Exists(
                sourceWadPath))
        {
            return;
        }

        string wadPath =
            Path.GetFullPath(
                sourceWadPath);

        string wadSha =
            ComputeSha256(
                wadPath);

        string manifestPath =
            wadPath +
            ".wadforge.json";

        if (TryResolveManifestPalette(
                manifestPath,
                wadPath,
                out string? manifestPalette) &&
            !string.IsNullOrWhiteSpace(
                manifestPalette))
        {
            CompanionPaletteLibraryAsset asset =
                ImportFile(
                    managedDataRoot,
                    manifestPalette,
                    Path.GetFileNameWithoutExtension(
                        manifestPalette),
                    $"WadForge manifest for {Path.GetFileName(wadPath)}");

            SetWadAssociation(
                managedDataRoot,
                wadSha,
                asset.AssetId);

            return;
        }

        if (TryResolveUnambiguousNearbyPalette(
                wadPath,
                out string? nearbyPalette) &&
            !string.IsNullOrWhiteSpace(
                nearbyPalette))
        {
            CompanionPaletteLibraryAsset asset =
                ImportFile(
                    managedDataRoot,
                    nearbyPalette,
                    Path.GetFileNameWithoutExtension(
                        nearbyPalette),
                    $"Palette beside imported WAD {Path.GetFileName(wadPath)}");

            SetWadAssociation(
                managedDataRoot,
                wadSha,
                asset.AssetId);
        }
    }

    public CompanionPaletteResolution PrepareForWad(
        string managedDataRoot,
        string wadPath,
        string? manifestPath,
        string? activeGameId,
        string? activeGameInstallationDirectory,
        string? activeDuskPalettePath,
        IEnumerable<string> discoveredQuakeInstallations)
    {
        if (string.IsNullOrWhiteSpace(
                wadPath) ||
            !File.Exists(
                wadPath))
        {
            throw new FileNotFoundException(
                "The WAD file could not be found.",
                wadPath);
        }

        string fullWadPath =
            Path.GetFullPath(
                wadPath);

        string wadSha =
            ComputeSha256(
                fullWadPath);

        CompanionPaletteLibraryAsset? remembered =
            GetAssociatedAsset(
                managedDataRoot,
                wadSha);

        if (remembered is not null)
        {
            return new CompanionPaletteResolution(
                wadSha,
                remembered.AssetId,
                remembered.PalettePath,
                $"Remembered for this WAD: {remembered.DisplayName}",
                true);
        }

        if (TryResolveManifestPalette(
                manifestPath,
                fullWadPath,
                out string? manifestPalette) &&
            !string.IsNullOrWhiteSpace(
                manifestPalette))
        {
            CompanionPaletteLibraryAsset manifestAsset =
                ImportFile(
                    managedDataRoot,
                    manifestPalette,
                    Path.GetFileNameWithoutExtension(
                        manifestPalette),
                    $"WadForge manifest for {Path.GetFileName(fullWadPath)}");

            return new CompanionPaletteResolution(
                wadSha,
                manifestAsset.AssetId,
                manifestAsset.PalettePath,
                $"WadForge manifest: {manifestAsset.DisplayName}",
                false);
        }

        if (string.Equals(
                activeGameId,
                CompanionGameProfiles.Quake.Id,
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(
                activeGameInstallationDirectory))
        {
            CompanionPaletteLibraryAsset? activeQuake =
                EnsureQuakePalette(
                    managedDataRoot,
                    activeGameInstallationDirectory);

            if (activeQuake is not null)
            {
                return new CompanionPaletteResolution(
                    wadSha,
                    activeQuake.AssetId,
                    activeQuake.PalettePath,
                    "Active Quake project palette",
                    false);
            }
        }

        if (string.Equals(
                activeGameId,
                CompanionGameProfiles.Dusk.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            CompanionPaletteLibraryAsset? dusk =
                EnsureDuskPalette(
                    managedDataRoot,
                    activeDuskPalettePath);

            if (dusk is not null)
            {
                return new CompanionPaletteResolution(
                    wadSha,
                    dusk.AssetId,
                    dusk.PalettePath,
                    "Active DUSK project palette",
                    false);
            }
        }

        if (TryResolveUnambiguousNearbyPalette(
                fullWadPath,
                out string? nearbyPalette) &&
            !string.IsNullOrWhiteSpace(
                nearbyPalette))
        {
            CompanionPaletteLibraryAsset nearby =
                ImportFile(
                    managedDataRoot,
                    nearbyPalette,
                    Path.GetFileNameWithoutExtension(
                        nearbyPalette),
                    $"Palette beside {Path.GetFileName(fullWadPath)}");

            return new CompanionPaletteResolution(
                wadSha,
                nearby.AssetId,
                nearby.PalettePath,
                $"Palette beside WAD: {nearby.DisplayName}",
                false);
        }

        HashSet<string> quakeDirectories =
            new(
                StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(
                activeGameInstallationDirectory))
        {
            quakeDirectories.Add(
                activeGameInstallationDirectory);
        }

        if (discoveredQuakeInstallations is not
            null)
        {
            foreach (string installation in
                     discoveredQuakeInstallations)
            {
                if (!string.IsNullOrWhiteSpace(
                        installation))
                {
                    quakeDirectories.Add(
                        installation);
                }
            }
        }

        foreach (string installation in
                 quakeDirectories)
        {
            EnsureQuakePalette(
                managedDataRoot,
                installation);
        }

        IReadOnlyList<CompanionPaletteLibraryAsset> available =
            GetAssets(
                managedDataRoot);

        if (available.Count ==
            1)
        {
            CompanionPaletteLibraryAsset only =
                available[0];

            return new CompanionPaletteResolution(
                wadSha,
                only.AssetId,
                only.PalettePath,
                $"Only available managed palette: {only.DisplayName}",
                false);
        }

        return new CompanionPaletteResolution(
            wadSha,
            null,
            null,
            available.Count ==
                0
                ? "No reliable palette found - neutral preview"
                : "Multiple palettes are available - choose one or remember a choice for this WAD",
            false);
    }

    public CompanionPaletteLibraryAsset? GetAssociatedAsset(
        string managedDataRoot,
        string wadSha256)
    {
        if (string.IsNullOrWhiteSpace(
                wadSha256))
        {
            return null;
        }

        string directory =
            EnsureLibraryDirectory(
                managedDataRoot);

        CompanionPaletteLibraryIndex index =
            ReadIndex(
                directory);

        if (!index.WadAssociations.TryGetValue(
                wadSha256.Trim().ToUpperInvariant(),
                out string? assetId))
        {
            return null;
        }

        return GetAssets(
                managedDataRoot)
            .FirstOrDefault(
                asset =>
                    string.Equals(
                        asset.AssetId,
                        assetId,
                        StringComparison.OrdinalIgnoreCase));
    }

    public void SetWadAssociation(
        string managedDataRoot,
        string wadSha256,
        string paletteAssetId)
    {
        if (string.IsNullOrWhiteSpace(
                wadSha256) ||
            string.IsNullOrWhiteSpace(
                paletteAssetId))
        {
            throw new ArgumentException(
                "A WAD SHA-256 and palette asset ID are required.");
        }

        string directory =
            EnsureLibraryDirectory(
                managedDataRoot);

        CompanionPaletteLibraryIndex index =
            ReadIndex(
                directory);

        string normalizedPalette =
            paletteAssetId.Trim().ToUpperInvariant();

        bool exists =
            index.Assets.Any(
                asset =>
                    string.Equals(
                        asset.AssetId,
                        normalizedPalette,
                        StringComparison.OrdinalIgnoreCase));

        if (!exists)
        {
            throw new InvalidOperationException(
                "The selected palette is not present in the Companion palette library.");
        }

        index.WadAssociations[
            wadSha256.Trim().ToUpperInvariant()] =
                normalizedPalette;

        WriteIndex(
            directory,
            index);
    }

    public void ClearWadAssociation(
        string managedDataRoot,
        string wadSha256)
    {
        if (string.IsNullOrWhiteSpace(
                wadSha256))
        {
            return;
        }

        string directory =
            EnsureLibraryDirectory(
                managedDataRoot);

        CompanionPaletteLibraryIndex index =
            ReadIndex(
                directory);

        if (index.WadAssociations.Remove(
                wadSha256.Trim().ToUpperInvariant()))
        {
            WriteIndex(
                directory,
                index);
        }
    }

    private static bool TryResolveManifestPalette(
        string? manifestPath,
        string wadPath,
        out string? palettePath)
    {
        palettePath =
            null;

        if (string.IsNullOrWhiteSpace(
                manifestPath) ||
            !File.Exists(
                manifestPath))
        {
            return false;
        }

        try
        {
            WadAliasManifest manifest =
                WadAliasManifestSerializer.Read(
                    manifestPath);

            if (string.IsNullOrWhiteSpace(
                    manifest.PaletteFileName))
            {
                return false;
            }

            string paletteName =
                manifest.PaletteFileName.Trim();

            if (Path.IsPathRooted(
                    paletteName) &&
                IsValidPaletteFile(
                    paletteName))
            {
                palettePath =
                    Path.GetFullPath(
                        paletteName);

                return true;
            }

            string? wadDirectory =
                Path.GetDirectoryName(
                    wadPath);

            string? manifestDirectory =
                Path.GetDirectoryName(
                    manifestPath);

            foreach (string? directory in
                     new[]
                     {
                         wadDirectory,
                         manifestDirectory
                     })
            {
                if (string.IsNullOrWhiteSpace(
                        directory))
                {
                    continue;
                }

                string candidate =
                    Path.Combine(
                        directory,
                        paletteName);

                if (!IsValidPaletteFile(
                        candidate))
                {
                    continue;
                }

                palettePath =
                    Path.GetFullPath(
                        candidate);

                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryResolveUnambiguousNearbyPalette(
        string wadPath,
        out string? palettePath)
    {
        palettePath =
            null;

        string? directory =
            Path.GetDirectoryName(
                wadPath);

        if (string.IsNullOrWhiteSpace(
                directory) ||
            !Directory.Exists(
                directory))
        {
            return false;
        }

        string baseName =
            Path.GetFileNameWithoutExtension(
                wadPath);

        foreach (string preferred in
                 new[]
                 {
                     "palette.lmp",
                     baseName + ".lmp",
                     baseName + ".pal"
                 })
        {
            string candidate =
                Path.Combine(
                    directory,
                    preferred);

            if (IsValidPaletteFile(
                    candidate))
            {
                palettePath =
                    Path.GetFullPath(
                        candidate);

                return true;
            }
        }

        try
        {
            string[] candidates =
                Directory
                    .EnumerateFiles(
                        directory,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .Where(
                        path =>
                            string.Equals(
                                Path.GetExtension(path),
                                ".lmp",
                                StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(
                                Path.GetExtension(path),
                                ".pal",
                                StringComparison.OrdinalIgnoreCase))
                    .Where(
                        IsValidPaletteFile)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .Take(
                        2)
                    .ToArray();

            if (candidates.Length !=
                1)
            {
                return false;
            }

            palettePath =
                Path.GetFullPath(
                    candidates[0]);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadPakEntry(
        string pakPath,
        string entryName,
        out byte[]? data)
    {
        data =
            null;

        try
        {
            using FileStream stream =
                new(
                    pakPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite |
                    FileShare.Delete);

            using BinaryReader reader =
                new(
                    stream,
                    Encoding.ASCII,
                    leaveOpen: true);

            if (stream.Length <
                12)
            {
                return false;
            }

            string signature =
                Encoding.ASCII.GetString(
                    reader.ReadBytes(
                        4));

            if (!string.Equals(
                    signature,
                    "PACK",
                    StringComparison.Ordinal))
            {
                return false;
            }

            int directoryOffset =
                reader.ReadInt32();

            int directoryLength =
                reader.ReadInt32();

            if (directoryOffset <
                    12 ||
                directoryLength <
                    0 ||
                directoryLength %
                    64 !=
                    0 ||
                checked(
                    (long)directoryOffset +
                    directoryLength) >
                    stream.Length)
            {
                return false;
            }

            int entryCount =
                directoryLength /
                64;

            stream.Position =
                directoryOffset;

            for (int index = 0;
                 index < entryCount;
                 index++)
            {
                string name =
                    ReadPakName(
                        reader.ReadBytes(
                            56));

                int offset =
                    reader.ReadInt32();

                int length =
                    reader.ReadInt32();

                if (!string.Equals(
                        name.Replace(
                            '\\',
                            '/'),
                        entryName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (offset <
                        0 ||
                    length !=
                        PaletteByteLength ||
                    checked(
                        (long)offset +
                        length) >
                        stream.Length)
                {
                    return false;
                }

                stream.Position =
                    offset;

                data =
                    reader.ReadBytes(
                        length);

                return data.Length ==
                    PaletteByteLength;
            }
        }
        catch
        {
        }

        data =
            null;

        return false;
    }

    private static string ReadPakName(
        byte[] bytes)
    {
        int length =
            Array.IndexOf(
                bytes,
                (byte)0);

        if (length <
            0)
        {
            length =
                bytes.Length;
        }

        return Encoding.ASCII.GetString(
            bytes,
            0,
            length);
    }

    private static bool IsValidPaletteFile(
        string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(
                    path) &&
                File.Exists(
                    path) &&
                new FileInfo(
                    path).Length ==
                    PaletteByteLength;
        }
        catch
        {
            return false;
        }
    }

    private static string ComputeSha256(
        string filePath)
    {
        using FileStream stream =
            File.OpenRead(
                filePath);

        return Convert.ToHexString(
            SHA256.HashData(
                stream));
    }

    private static bool AddSource(
        CompanionPaletteLibraryStoredAsset asset,
        string sourceDescription)
    {
        if (string.IsNullOrWhiteSpace(
                sourceDescription) ||
            asset.Sources.Any(
                source =>
                    string.Equals(
                        source,
                        sourceDescription,
                        StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        asset.Sources.Add(
            sourceDescription.Trim());

        return true;
    }

    private static string MakeSafeFileStem(
        string displayName)
    {
        string value =
            string.IsNullOrWhiteSpace(
                displayName)
                ? "palette"
                : displayName.Trim();

        foreach (char invalid in
                 Path.GetInvalidFileNameChars())
        {
            value =
                value.Replace(
                    invalid,
                    '-');
        }

        value =
            value.Trim(
                ' ',
                '.');

        return value.Length ==
            0
                ? "palette"
                : value;
    }

    private string EnsureLibraryDirectory(
        string managedDataRoot)
    {
        string directory =
            GetLibraryDirectory(
                managedDataRoot);

        Directory.CreateDirectory(
            directory);

        return directory;
    }

    private static CompanionPaletteLibraryIndex ReadIndex(
        string directory)
    {
        string path =
            Path.Combine(
                directory,
                IndexFileName);

        if (!File.Exists(
                path))
        {
            return new CompanionPaletteLibraryIndex();
        }

        try
        {
            string json =
                File.ReadAllText(
                    path);

            CompanionPaletteLibraryIndex? index =
                JsonSerializer.Deserialize<CompanionPaletteLibraryIndex>(
                    json,
                    JsonOptions);

            if (index is null ||
                index.SchemaVersion !=
                    IndexSchemaVersion)
            {
                return new CompanionPaletteLibraryIndex();
            }

            index.Assets ??=
                new List<CompanionPaletteLibraryStoredAsset>();

            index.WadAssociations ??=
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            index.WadAssociations =
                new Dictionary<string, string>(
                    index.WadAssociations,
                    StringComparer.OrdinalIgnoreCase);

            return index;
        }
        catch
        {
            return new CompanionPaletteLibraryIndex();
        }
    }

    private static void WriteIndex(
        string directory,
        CompanionPaletteLibraryIndex index)
    {
        index.SchemaVersion =
            IndexSchemaVersion;

        string path =
            Path.Combine(
                directory,
                IndexFileName);

        string temporary =
            path +
            ".tmp";

        string json =
            JsonSerializer.Serialize(
                index,
                JsonOptions);

        File.WriteAllText(
            temporary,
            json);

        File.Move(
            temporary,
            path,
            overwrite: true);
    }

    private sealed class CompanionPaletteLibraryIndex
    {
        public int SchemaVersion { get; set; } =
            IndexSchemaVersion;

        public List<CompanionPaletteLibraryStoredAsset> Assets { get; set; } =
            new();

        public Dictionary<string, string> WadAssociations { get; set; } =
            new(
                StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CompanionPaletteLibraryStoredAsset
    {
        public string AssetId { get; set; } =
            string.Empty;

        public string FileName { get; set; } =
            string.Empty;

        public string DisplayName { get; set; } =
            string.Empty;

        public List<string> Sources { get; set; } =
            new();
    }
}
