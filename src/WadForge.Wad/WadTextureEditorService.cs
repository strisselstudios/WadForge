using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using WadForge.Aliases;
using WadForge.Core;
using WadForge.Imaging;

namespace WadForge.Wad;

public sealed record WadTextureEditorTexture(
    int DirectoryIndex,
    string InternalName,
    int Width,
    int Height,
    bool HasMaskPrefix,
    bool UsesIndex255,
    int Index255PixelCount,
    int DominantEdgeIndex,
    double DominantEdgeShare,
    double DominantAreaShare,
    string DominantColorText);

public sealed record WadTextureEditorDocument(
    string WadPath,
    WadFormat Format,
    IReadOnlyList<WadTextureEditorTexture> Textures);

public sealed record WadIndexedTextureData(
    int DirectoryIndex,
    int Width,
    int Height,
    byte[] Pixels,
    IReadOnlyList<Rgb24> Palette);
public sealed record WadTextureEdit(
    int DirectoryIndex,
    string NewInternalName,
    int? RemapIndexTo255,
    byte[]? EditedMip0Pixels = null);

public sealed record WadTextureEditSaveResult(
    string OutputPath,
    string? BackupPath,
    int EditedTextureCount);

public static class WadTextureEditorService
{
    private const int HeaderSize = 12;
    private const int DirectoryEntrySize = 32;
    private const int MipTextureHeaderSize = 40;
    private const int MaximumTextureDimension = 8192;
    private const byte Wad3MipTextureType = 67;
    private const byte Wad2MipTextureType = 68;

    public static WadTextureEditorDocument Load(
        string wadPath,
        string? wad2PalettePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            wadPath);

        string fullPath =
            Path.GetFullPath(
                wadPath);

        if (!File.Exists(
                fullPath))
        {
            throw new FileNotFoundException(
                "The WAD archive could not be found.",
                fullPath);
        }

        IReadOnlyList<Rgb24>? wad2Palette =
            LoadOptionalPalette(
                wad2PalettePath);

        using FileStream stream =
            new(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite |
                FileShare.Delete);

        using BinaryReader reader =
            new(
                stream,
                Encoding.ASCII,
                leaveOpen:
                    true);

        var (format, _, entries) =
            ReadDirectory(
                stream,
                reader);

        List<WadTextureEditorTexture> textures =
            new();

        byte expectedType =
            format ==
                WadFormat.Wad3
                ? Wad3MipTextureType
                : Wad2MipTextureType;

        foreach (DirectoryEntry entry in
                 entries)
        {
            if (entry.Type !=
                    expectedType ||
                entry.Compression !=
                    0 ||
                entry.DiskSize <
                    MipTextureHeaderSize)
            {
                continue;
            }

            TextureHeader header =
                ReadTextureHeader(
                    stream,
                    reader,
                    entry);

            byte[] mip0 =
                ReadMipLevel(
                    stream,
                    reader,
                    entry,
                    header,
                    level:
                        0);

            int index255Count =
                mip0.Count(
                    value =>
                        value ==
                        255);

            var dominant =
                FindDominantEdgeIndex(
                    mip0,
                    header.Width,
                    header.Height);

            IReadOnlyList<Rgb24>? palette =
                format ==
                    WadFormat.Wad3
                    ? ReadEmbeddedWad3Palette(
                        stream,
                        reader,
                        entry,
                        header)
                    : wad2Palette;

            textures.Add(
                new WadTextureEditorTexture(
                    entry.DirectoryIndex,
                    header.InternalName,
                    header.Width,
                    header.Height,
                    header.InternalName.StartsWith(
                        "{",
                        StringComparison.Ordinal),
                    index255Count >
                        0,
                    index255Count,
                    dominant.Index,
                    dominant.BorderShare,
                    dominant.AreaShare,
                    GetColorText(
                        palette,
                        dominant.Index)));
        }

        if (textures.Count ==
            0)
        {
            throw new InvalidDataException(
                "The WAD contains no supported mip textures.");
        }

        return new WadTextureEditorDocument(
            fullPath,
            format,
            textures);
    }

    public static WadIndexedTextureData ReadIndexedTexture(
        string wadPath,
        int directoryIndex,
        string? wad2PalettePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            wadPath);

        using FileStream stream =
            new(
                Path.GetFullPath(
                    wadPath),
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite |
                FileShare.Delete);

        using BinaryReader reader =
            new(
                stream,
                Encoding.ASCII,
                leaveOpen:
                    true);

        var (format, _, entries) =
            ReadDirectory(
                stream,
                reader);

        DirectoryEntry entry =
            entries.FirstOrDefault(
                candidate =>
                    candidate.DirectoryIndex ==
                    directoryIndex) ??
            throw new ArgumentOutOfRangeException(
                nameof(directoryIndex),
                "The selected texture directory entry does not exist.");

        TextureHeader header =
            ReadTextureHeader(
                stream,
                reader,
                entry);

        byte[] pixels =
            ReadMipLevel(
                stream,
                reader,
                entry,
                header,
                level:
                    0);

        IReadOnlyList<Rgb24>? palette =
            format ==
                WadFormat.Wad3
                ? ReadEmbeddedWad3Palette(
                    stream,
                    reader,
                    entry,
                    header)
                : LoadOptionalPalette(
                    wad2PalettePath);

        if (palette is
            null)
        {
            throw new InvalidOperationException(
                "A WAD2 palette is required for indexed texture editing.");
        }

        return new WadIndexedTextureData(
            directoryIndex,
            header.Width,
            header.Height,
            pixels,
            palette);
    }

    public static RgbaImage RenderIndexedPreview(
        int width,
        int height,
        IReadOnlyList<byte> indexedPixels,
        IReadOnlyList<Rgb24> palette,
        int? transparentIndex = null)
    {
        ArgumentNullException.ThrowIfNull(
            indexedPixels);

        ArgumentNullException.ThrowIfNull(
            palette);

        int expected =
            checked(
                width *
                height);

        if (width <=
                0 ||
            height <=
                0 ||
            indexedPixels.Count !=
                expected)
        {
            throw new ArgumentException(
                "Indexed pixel dimensions do not match the supplied pixel buffer.");
        }

        if (palette.Count <
            256)
        {
            throw new ArgumentException(
                "Indexed texture palettes must contain 256 colors.");
        }

        Rgba32[] pixels =
            new Rgba32[
                expected];

        for (int index = 0;
             index <
                 expected;
             index++)
        {
            byte paletteIndex =
                indexedPixels[index];

            Rgb24 color =
                palette[paletteIndex];

            byte alpha =
                transparentIndex.HasValue &&
                paletteIndex ==
                    transparentIndex.Value
                    ? (byte)0
                    : (byte)255;

            pixels[index] =
                new Rgba32(
                    color.R,
                    color.G,
                    color.B,
                    alpha);
        }

        return new RgbaImage(
            width,
            height,
            pixels);
    }
    public static RgbaImage ReadPreview(
        string wadPath,
        int directoryIndex,
        string? wad2PalettePath = null,
        int? transparentIndexOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            wadPath);

        using FileStream stream =
            new(
                wadPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite |
                FileShare.Delete);

        using BinaryReader reader =
            new(
                stream,
                Encoding.ASCII,
                leaveOpen:
                    true);

        var (format, _, entries) =
            ReadDirectory(
                stream,
                reader);

        DirectoryEntry entry =
            entries.FirstOrDefault(
                candidate =>
                    candidate.DirectoryIndex ==
                    directoryIndex) ??
            throw new ArgumentOutOfRangeException(
                nameof(directoryIndex),
                "The selected texture directory entry does not exist.");

        TextureHeader header =
            ReadTextureHeader(
                stream,
                reader,
                entry);

        byte[] indexedPixels =
            ReadMipLevel(
                stream,
                reader,
                entry,
                header,
                level:
                    0);

        IReadOnlyList<Rgb24>? palette =
            format ==
                WadFormat.Wad3
                ? ReadEmbeddedWad3Palette(
                    stream,
                    reader,
                    entry,
                    header)
                : LoadOptionalPalette(
                    wad2PalettePath);

        if (palette is
            null)
        {
            throw new InvalidOperationException(
                "Choose a WAD2 palette before previewing this texture.");
        }

        int? transparentIndex =
            transparentIndexOverride;

        if (transparentIndex is
                null &&
            header.InternalName.StartsWith(
                "{",
                StringComparison.Ordinal))
        {
            transparentIndex =
                255;
        }

        Rgba32[] pixels =
            new Rgba32[
                indexedPixels.Length];

        for (int index = 0;
             index < indexedPixels.Length;
             index++)
        {
            byte paletteIndex =
                indexedPixels[index];

            Rgb24 color =
                palette[paletteIndex];

            byte alpha =
                transparentIndex.HasValue &&
                paletteIndex ==
                    transparentIndex.Value
                    ? (byte)0
                    : (byte)255;

            pixels[index] =
                new Rgba32(
                    color.R,
                    color.G,
                    color.B,
                    alpha);
        }

        return new RgbaImage(
            header.Width,
            header.Height,
            pixels);
    }

    public static WadTextureEditSaveResult SaveCopy(
        string sourceWadPath,
        string destinationWadPath,
        IReadOnlyList<WadTextureEdit> edits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            sourceWadPath);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            destinationWadPath);

        ArgumentNullException.ThrowIfNull(
            edits);

        if (edits.Count ==
            0)
        {
            throw new InvalidOperationException(
                "No texture edits have been staged.");
        }

        string sourcePath =
            Path.GetFullPath(
                sourceWadPath);

        string destinationPath =
            Path.GetFullPath(
                destinationWadPath);

        if (string.Equals(
                sourcePath,
                destinationPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Use SaveInPlaceWithBackup when saving over the source WAD.");
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(
                destinationPath) ??
            throw new InvalidDataException(
                "The destination directory could not be resolved."));

        string tempPath =
            destinationPath +
            ".tmp-" +
            Guid.NewGuid().ToString(
                "N");

        string tempSidecar =
            tempPath +
            ".wadforge.json";

        try
        {
            File.Copy(
                sourcePath,
                tempPath,
                overwrite:
                    false);

            ApplyEdits(
                tempPath,
                edits);

            _ =
                Load(
                    tempPath);

            WriteUpdatedAliasManifest(
                sourcePath,
                tempPath,
                edits);

            File.Move(
                tempPath,
                destinationPath,
                overwrite:
                    true);

            string destinationSidecar =
                destinationPath +
                ".wadforge.json";

            if (File.Exists(
                    tempSidecar))
            {
                File.Move(
                    tempSidecar,
                    destinationSidecar,
                    overwrite:
                        true);
            }
            else if (File.Exists(
                         destinationSidecar))
            {
                File.Delete(
                    destinationSidecar);
            }

            return new WadTextureEditSaveResult(
                destinationPath,
                null,
                edits.Count);
        }
        catch
        {
            TryDelete(
                tempPath);

            TryDelete(
                tempSidecar);

            throw;
        }
    }

    public static WadTextureEditSaveResult SaveInPlaceWithBackup(
        string wadPath,
        IReadOnlyList<WadTextureEdit> edits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            wadPath);

        ArgumentNullException.ThrowIfNull(
            edits);

        if (edits.Count ==
            0)
        {
            throw new InvalidOperationException(
                "No texture edits have been staged.");
        }

        string fullPath =
            Path.GetFullPath(
                wadPath);

        if (!File.Exists(
                fullPath))
        {
            throw new FileNotFoundException(
                "The WAD archive could not be found.",
                fullPath);
        }

        string directory =
            Path.GetDirectoryName(
                fullPath) ??
            throw new InvalidDataException(
                "The WAD directory could not be resolved.");

        string baseName =
            Path.GetFileNameWithoutExtension(
                fullPath);

        string extension =
            Path.GetExtension(
                fullPath);

        string stamp =
            DateTime.Now.ToString(
                "yyyyMMdd-HHmmss");

        string backupPath =
            Path.Combine(
                directory,
                $"{baseName}.before-texture-edit-{stamp}{extension}");

        int suffix =
            2;

        while (File.Exists(
                   backupPath))
        {
            backupPath =
                Path.Combine(
                    directory,
                    $"{baseName}.before-texture-edit-{stamp}-{suffix}{extension}");

            suffix++;
        }

        string tempPath =
            Path.Combine(
                directory,
                $".{baseName}.texture-edit-{Guid.NewGuid():N}{extension}");

        string sourceSidecar =
            fullPath +
            ".wadforge.json";

        string backupSidecar =
            backupPath +
            ".wadforge.json";

        string tempSidecar =
            tempPath +
            ".wadforge.json";

        try
        {
            File.Copy(
                fullPath,
                backupPath,
                overwrite:
                    false);

            if (File.Exists(
                    sourceSidecar))
            {
                File.Copy(
                    sourceSidecar,
                    backupSidecar,
                    overwrite:
                        false);
            }

            File.Copy(
                fullPath,
                tempPath,
                overwrite:
                    false);

            ApplyEdits(
                tempPath,
                edits);

            _ =
                Load(
                    tempPath);

            WriteUpdatedAliasManifest(
                fullPath,
                tempPath,
                edits);

            File.Move(
                tempPath,
                fullPath,
                overwrite:
                    true);

            if (File.Exists(
                    tempSidecar))
            {
                File.Move(
                    tempSidecar,
                    sourceSidecar,
                    overwrite:
                        true);
            }
            else if (File.Exists(
                         sourceSidecar))
            {
                File.Delete(
                    sourceSidecar);
            }

            return new WadTextureEditSaveResult(
                fullPath,
                backupPath,
                edits.Count);
        }
        catch
        {
            TryDelete(
                tempPath);

            TryDelete(
                tempSidecar);

            if (File.Exists(
                    backupPath))
            {
                File.Copy(
                    backupPath,
                    fullPath,
                    overwrite:
                        true);
            }

            if (File.Exists(
                    backupSidecar))
            {
                File.Copy(
                    backupSidecar,
                    sourceSidecar,
                    overwrite:
                        true);
            }

            throw;
        }
    }

    private static void ApplyEdits(
        string wadPath,
        IReadOnlyList<WadTextureEdit> edits)
    {
        if (edits
            .GroupBy(
                edit =>
                    edit.DirectoryIndex)
            .Any(
                group =>
                    group.Count() >
                    1))
        {
            throw new InvalidOperationException(
                "Only one staged edit is allowed per texture.");
        }

        using FileStream stream =
            new(
                wadPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

        using BinaryReader reader =
            new(
                stream,
                Encoding.ASCII,
                leaveOpen:
                    true);

        using BinaryWriter writer =
            new(
                stream,
                Encoding.ASCII,
                leaveOpen:
                    true);

        var (_, directoryOffset, entries) =
            ReadDirectory(
                stream,
                reader);

        Dictionary<int, WadTextureEdit> editByDirectory =
            edits.ToDictionary(
                edit =>
                    edit.DirectoryIndex);

        Dictionary<int, string> finalNames =
            entries.ToDictionary(
                entry =>
                    entry.DirectoryIndex,
                entry =>
                    editByDirectory.TryGetValue(
                            entry.DirectoryIndex,
                            out WadTextureEdit? edit)
                        ? ValidateTextureName(
                            edit.NewInternalName)
                        : entry.Name);

        string? duplicateName =
            finalNames
                .GroupBy(
                    pair =>
                        pair.Value,
                    StringComparer.OrdinalIgnoreCase)
                .Where(
                    group =>
                        group.Count() >
                        1)
                .Select(
                    group =>
                        group.Key)
                .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(
                duplicateName))
        {
            throw new InvalidOperationException(
                $"The edited WAD would contain duplicate internal name '{duplicateName}'.");
        }

        foreach (WadTextureEdit edit in
                 edits)
        {
            DirectoryEntry entry =
                entries.FirstOrDefault(
                    candidate =>
                        candidate.DirectoryIndex ==
                        edit.DirectoryIndex) ??
                throw new InvalidOperationException(
                    $"Texture directory entry {edit.DirectoryIndex} no longer exists.");

            TextureHeader header =
                ReadTextureHeader(
                    stream,
                    reader,
                    entry);

            string validatedName =
                ValidateTextureName(
                    edit.NewInternalName);

            WriteFixedAsciiAt(
                stream,
                writer,
                entry.FilePosition,
                validatedName,
                16);

            WriteFixedAsciiAt(
                stream,
                writer,
                checked(
                    directoryOffset +
                    (entry.DirectoryIndex *
                     DirectoryEntrySize) +
                    16),
                validatedName,
                16);

            if (edit.EditedMip0Pixels is not
                null)
            {
                byte[] editedPixels =
                    edit.EditedMip0Pixels.ToArray();

                int expectedPixelCount =
                    checked(
                        header.Width *
                        header.Height);

                if (editedPixels.Length !=
                    expectedPixelCount)
                {
                    throw new InvalidOperationException(
                        $"Edited pixel buffer for '{header.InternalName}' has {editedPixels.Length:N0} pixels; expected {expectedPixelCount:N0}.");
                }

                if (edit.RemapIndexTo255.HasValue &&
                    edit.RemapIndexTo255.Value !=
                        255)
                {
                    ReplacePaletteIndex(
                        editedPixels,
                        edit.RemapIndexTo255.Value,
                        255);
                }

                WriteEditedMipChain(
                    stream,
                    reader,
                    writer,
                    entry,
                    header,
                    editedPixels);
            }
            else if (edit.RemapIndexTo255.HasValue &&
                     edit.RemapIndexTo255.Value !=
                         255)
            {
                RemapMipIndex(
                    stream,
                    reader,
                    writer,
                    entry,
                    header,
                    edit.RemapIndexTo255.Value);
            }
        }

        writer.Flush();
        stream.Flush(
            flushToDisk:
                true);
    }

    private static void WriteEditedMipChain(
        FileStream stream,
        BinaryReader reader,
        BinaryWriter writer,
        DirectoryEntry entry,
        TextureHeader header,
        byte[] mip0Pixels)
    {
        byte[] current =
            mip0Pixels;

        int currentWidth =
            header.Width;

        int currentHeight =
            header.Height;

        for (int level = 0;
             level <
                 4;
             level++)
        {
            int expectedWidth =
                Math.Max(
                    1,
                    header.Width >>
                    level);

            int expectedHeight =
                Math.Max(
                    1,
                    header.Height >>
                    level);

            if (level >
                0)
            {
                current =
                    DownsampleIndexed(
                        current,
                        currentWidth,
                        currentHeight,
                        expectedWidth,
                        expectedHeight);

                currentWidth =
                    expectedWidth;

                currentHeight =
                    expectedHeight;
            }

            byte[] existing =
                ReadMipLevel(
                    stream,
                    reader,
                    entry,
                    header,
                    level);

            if (current.Length !=
                existing.Length)
            {
                throw new InvalidOperationException(
                    $"Generated mip level {level} for '{header.InternalName}' has an unexpected size.");
            }

            stream.Position =
                checked(
                    (long)entry.FilePosition +
                    header.MipOffsets[level]);

            writer.Write(
                current);
        }
    }

    private static byte[] DownsampleIndexed(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        int destinationWidth,
        int destinationHeight)
    {
        byte[] destination =
            new byte[
                checked(
                    destinationWidth *
                    destinationHeight)];

        Span<byte> samples =
            stackalloc byte[
                4];

        for (int y = 0;
             y <
                 destinationHeight;
             y++)
        {
            for (int x = 0;
                 x <
                     destinationWidth;
                 x++)
            {
                int sourceX =
                    Math.Min(
                        sourceWidth -
                        1,
                        x *
                        2);

                int sourceY =
                    Math.Min(
                        sourceHeight -
                        1,
                        y *
                        2);

                int sampleCount =
                    0;

                for (int offsetY = 0;
                     offsetY <
                         2;
                     offsetY++)
                {
                    int sampleY =
                        Math.Min(
                            sourceHeight -
                                1,
                            sourceY +
                                offsetY);

                    for (int offsetX = 0;
                         offsetX <
                             2;
                         offsetX++)
                    {
                        int sampleX =
                            Math.Min(
                                sourceWidth -
                                    1,
                                sourceX +
                                    offsetX);

                        samples[sampleCount++] =
                            source[
                                (sampleY *
                                 sourceWidth) +
                                sampleX];
                    }
                }

                byte selected =
                    samples[0];

                int selectedCount =
                    0;

                for (int candidateIndex = 0;
                     candidateIndex <
                         sampleCount;
                     candidateIndex++)
                {
                    byte candidate =
                        samples[candidateIndex];

                    int count =
                        0;

                    for (int sampleIndex = 0;
                         sampleIndex <
                             sampleCount;
                         sampleIndex++)
                    {
                        if (samples[sampleIndex] ==
                            candidate)
                        {
                            count++;
                        }
                    }

                    if (count >
                        selectedCount)
                    {
                        selected =
                            candidate;

                        selectedCount =
                            count;
                    }
                }

                destination[
                    (y *
                     destinationWidth) +
                    x] =
                    selected;
            }
        }

        return destination;
    }

    private static void ReplacePaletteIndex(
        byte[] pixels,
        int sourceIndex,
        byte destinationIndex)
    {
        if (sourceIndex <
                0 ||
            sourceIndex >
                255)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceIndex));
        }

        for (int index = 0;
             index <
                 pixels.Length;
             index++)
        {
            if (pixels[index] ==
                (byte)sourceIndex)
            {
                pixels[index] =
                    destinationIndex;
            }
        }
    }
    private static void RemapMipIndex(
        FileStream stream,
        BinaryReader reader,
        BinaryWriter writer,
        DirectoryEntry entry,
        TextureHeader header,
        int sourceIndex)
    {
        if (sourceIndex <
                0 ||
            sourceIndex >
                254)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceIndex),
                "The source palette index must be between 0 and 254.");
        }

        int changed =
            0;

        for (int level = 0;
             level < 4;
             level++)
        {
            byte[] pixels =
                ReadMipLevel(
                    stream,
                    reader,
                    entry,
                    header,
                    level);

            bool levelChanged =
                false;

            for (int index = 0;
                 index < pixels.Length;
                 index++)
            {
                if (pixels[index] !=
                    (byte)sourceIndex)
                {
                    continue;
                }

                pixels[index] =
                    255;

                changed++;
                levelChanged =
                    true;
            }

            if (!levelChanged)
            {
                continue;
            }

            stream.Position =
                checked(
                    (long)entry.FilePosition +
                    header.MipOffsets[level]);

            writer.Write(
                pixels);
        }

        if (changed ==
            0)
        {
            throw new InvalidOperationException(
                $"Palette index {sourceIndex} was not used by texture '{header.InternalName}'.");
        }
    }

    private static void WriteUpdatedAliasManifest(
        string sourceWadPath,
        string editedWadPath,
        IReadOnlyList<WadTextureEdit> edits)
    {
        string sourceSidecar =
            sourceWadPath +
            ".wadforge.json";

        string editedSidecar =
            editedWadPath +
            ".wadforge.json";

        if (!File.Exists(
                sourceSidecar))
        {
            TryDelete(
                editedSidecar);

            return;
        }

        WadAliasManifest sourceManifest =
            WadAliasManifestSerializer.Read(
                sourceSidecar);

        WadTextureEditorDocument original =
            Load(
                sourceWadPath);

        Dictionary<string, string> renamedByOldName =
            edits
                .Select(
                    edit =>
                        new
                        {
                            Edit =
                                edit,
                            Texture =
                                original.Textures.FirstOrDefault(
                                    texture =>
                                        texture.DirectoryIndex ==
                                        edit.DirectoryIndex)
                        })
                .Where(
                    pair =>
                        pair.Texture is not
                        null)
                .ToDictionary(
                    pair =>
                        pair.Texture!.InternalName,
                    pair =>
                        ValidateTextureName(
                            pair.Edit.NewInternalName),
                    StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<TextureAliasEntry> textures =
            sourceManifest.Textures
                .Select(
                    entry =>
                    {
                        if (!renamedByOldName.TryGetValue(
                                entry.InternalName,
                                out string? newName))
                        {
                            return entry;
                        }

                        return entry with
                        {
                            DisplayName =
                                newName,
                            InternalName =
                                newName
                        };
                    })
                .ToArray();

        WadAliasManifest updated =
            sourceManifest with
            {
                WadFileName =
                    Path.GetFileName(
                        editedWadPath),
                WadSha256 =
                    ComputeSha256(
                        editedWadPath),
                Textures =
                    textures
            };

        WadAliasManifestSerializer.Write(
            editedSidecar,
            updated);
    }

    private static string ValidateTextureName(
        string name)
    {
        string trimmed =
            name.Trim();

        if (string.IsNullOrWhiteSpace(
                trimmed))
        {
            throw new InvalidOperationException(
                "Texture names cannot be blank.");
        }

        if (trimmed.IndexOf(
                '\0') >=
            0)
        {
            throw new InvalidOperationException(
                "Texture names cannot contain NUL characters.");
        }

        if (trimmed.Any(
                character =>
                    character >
                    127))
        {
            throw new InvalidOperationException(
                "WAD internal texture names must use ASCII characters.");
        }

        if (Encoding.ASCII.GetByteCount(
                trimmed) >
            16)
        {
            throw new InvalidOperationException(
                $"Texture name '{trimmed}' is longer than the 16-byte WAD internal-name limit.");
        }

        return trimmed;
    }

    private static IReadOnlyList<Rgb24>? LoadOptionalPalette(
        string? palettePath)
    {
        if (string.IsNullOrWhiteSpace(
                palettePath))
        {
            return BuiltInPalettes.Quake;
        }

        return PaletteFile.Load(
            palettePath);
    }
    private static IReadOnlyList<Rgb24> ReadEmbeddedWad3Palette(
        FileStream stream,
        BinaryReader reader,
        DirectoryEntry entry,
        TextureHeader header)
    {
        int mip3Size =
            checked(
                Math.Max(
                    1,
                    header.Width >>
                    3) *
                Math.Max(
                    1,
                    header.Height >>
                    3));

        long paletteCountPosition =
            checked(
                (long)entry.FilePosition +
                header.MipOffsets[3] +
                mip3Size);

        long lumpEnd =
            checked(
                (long)entry.FilePosition +
                entry.DiskSize);

        if (paletteCountPosition +
                2 +
                768 >
                lumpEnd ||
            paletteCountPosition +
                2 +
                768 >
                stream.Length)
        {
            throw new InvalidDataException(
                $"Texture '{header.InternalName}' has no complete WAD3 palette.");
        }

        stream.Position =
            paletteCountPosition;

        ushort colorCount =
            reader.ReadUInt16();

        if (colorCount !=
            256)
        {
            throw new InvalidDataException(
                $"Texture '{header.InternalName}' declares {colorCount} colors instead of 256.");
        }

        Rgb24[] palette =
            new Rgb24[
                256];

        for (int index = 0;
             index < palette.Length;
             index++)
        {
            palette[index] =
                new Rgb24(
                    reader.ReadByte(),
                    reader.ReadByte(),
                    reader.ReadByte());
        }

        return palette;
    }

    private static TextureHeader ReadTextureHeader(
        FileStream stream,
        BinaryReader reader,
        DirectoryEntry entry)
    {
        stream.Position =
            entry.FilePosition;

        byte[] bytes =
            reader.ReadBytes(
                MipTextureHeaderSize);

        if (bytes.Length !=
            MipTextureHeaderSize)
        {
            throw new EndOfStreamException(
                $"Texture lump '{entry.Name}' could not be read.");
        }

        string internalName =
            ReadFixedAscii(
                bytes.AsSpan(
                    0,
                    16));

        if (string.IsNullOrWhiteSpace(
                internalName))
        {
            internalName =
                entry.Name;
        }

        int width =
            BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(
                    16,
                    4));

        int height =
            BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(
                    20,
                    4));

        if (width <=
                0 ||
            height <=
                0 ||
            width >
                MaximumTextureDimension ||
            height >
                MaximumTextureDimension)
        {
            throw new InvalidDataException(
                $"Texture '{internalName}' has invalid dimensions {width} x {height}.");
        }

        int[] offsets =
            new int[
                4];

        for (int level = 0;
             level < offsets.Length;
             level++)
        {
            offsets[level] =
                BinaryPrimitives.ReadInt32LittleEndian(
                    bytes.AsSpan(
                        24 +
                        (level *
                         4),
                        4));

            if (offsets[level] <
                MipTextureHeaderSize)
            {
                throw new InvalidDataException(
                    $"Texture '{internalName}' has an invalid mip offset.");
            }
        }

        return new TextureHeader(
            internalName,
            width,
            height,
            offsets);
    }

    private static byte[] ReadMipLevel(
        FileStream stream,
        BinaryReader reader,
        DirectoryEntry entry,
        TextureHeader header,
        int level)
    {
        if (level <
                0 ||
            level >
                3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level));
        }

        int width =
            Math.Max(
                1,
                header.Width >>
                level);

        int height =
            Math.Max(
                1,
                header.Height >>
                level);

        int size =
            checked(
                width *
                height);

        long position =
            checked(
                (long)entry.FilePosition +
                header.MipOffsets[level]);

        long lumpEnd =
            checked(
                (long)entry.FilePosition +
                entry.DiskSize);

        if (position <
                entry.FilePosition ||
            position +
                size >
                lumpEnd ||
            position +
                size >
                stream.Length)
        {
            throw new InvalidDataException(
                $"Mip level {level} for '{header.InternalName}' is outside its WAD lump.");
        }

        stream.Position =
            position;

        byte[] pixels =
            reader.ReadBytes(
                size);

        if (pixels.Length !=
            size)
        {
            throw new EndOfStreamException(
                $"Mip level {level} for '{header.InternalName}' could not be read.");
        }

        return pixels;
    }

    private static (
        int Index,
        double BorderShare,
        double AreaShare)
        FindDominantEdgeIndex(
            byte[] pixels,
            int width,
            int height)
    {
        int[] borderCounts =
            new int[
                256];

        int[] areaCounts =
            new int[
                256];

        foreach (byte value in
                 pixels)
        {
            areaCounts[value]++;
        }

        for (int x = 0;
             x < width;
             x++)
        {
            borderCounts[
                pixels[x]]++;

            if (height >
                1)
            {
                borderCounts[
                    pixels[
                        ((height -
                          1) *
                         width) +
                        x]]++;
            }
        }

        for (int y = 1;
             y < height -
                 1;
             y++)
        {
            borderCounts[
                pixels[
                    y *
                    width]]++;

            if (width >
                1)
            {
                borderCounts[
                    pixels[
                        (y *
                         width) +
                        (width -
                         1)]]++;
            }
        }

        int totalBorder =
            borderCounts.Sum();

        int dominant =
            0;

        for (int index = 1;
             index < borderCounts.Length;
             index++)
        {
            if (borderCounts[index] >
                borderCounts[dominant])
            {
                dominant =
                    index;
            }
        }

        return (
            dominant,
            totalBorder ==
                0
                ? 0
                : (double)borderCounts[dominant] /
                  totalBorder,
            pixels.Length ==
                0
                ? 0
                : (double)areaCounts[dominant] /
                  pixels.Length);
    }

    private static string GetColorText(
        IReadOnlyList<Rgb24>? palette,
        int index)
    {
        if (palette is
                null ||
            index <
                0 ||
            index >=
                palette.Count)
        {
            return "palette unavailable";
        }

        Rgb24 color =
            palette[index];

        return $"RGB {color.R}, {color.G}, {color.B}";
    }

    private static (
        WadFormat Format,
        int DirectoryOffset,
        IReadOnlyList<DirectoryEntry> Entries)
        ReadDirectory(
            FileStream stream,
            BinaryReader reader)
    {
        if (stream.Length <
            HeaderSize)
        {
            throw new InvalidDataException(
                "The file is too small to be a WAD archive.");
        }

        stream.Position =
            0;

        byte[] header =
            reader.ReadBytes(
                HeaderSize);

        if (header.Length !=
            HeaderSize)
        {
            throw new EndOfStreamException(
                "The WAD header could not be read.");
        }

        string signature =
            Encoding.ASCII.GetString(
                header,
                0,
                4);

        WadFormat format =
            signature switch
            {
                "WAD2" =>
                    WadFormat.Wad2,
                "WAD3" =>
                    WadFormat.Wad3,
                _ =>
                    throw new InvalidDataException(
                        $"Unsupported WAD signature '{signature}'.")
            };

        int lumpCount =
            BinaryPrimitives.ReadInt32LittleEndian(
                header.AsSpan(
                    4,
                    4));

        int directoryOffset =
            BinaryPrimitives.ReadInt32LittleEndian(
                header.AsSpan(
                    8,
                    4));

        if (lumpCount <
                0 ||
            lumpCount >
                1_000_000)
        {
            throw new InvalidDataException(
                "The WAD lump count is invalid.");
        }

        long directoryLength =
            checked(
                (long)lumpCount *
                DirectoryEntrySize);

        if (directoryOffset <
                HeaderSize ||
            (long)directoryOffset +
                directoryLength >
                stream.Length)
        {
            throw new InvalidDataException(
                "The WAD directory is invalid.");
        }

        stream.Position =
            directoryOffset;

        List<DirectoryEntry> entries =
            new(
                lumpCount);

        for (int index = 0;
             index < lumpCount;
             index++)
        {
            int filePosition =
                reader.ReadInt32();

            int diskSize =
                reader.ReadInt32();

            int fullSize =
                reader.ReadInt32();

            byte type =
                reader.ReadByte();

            byte compression =
                reader.ReadByte();

            reader.ReadByte();
            reader.ReadByte();

            string name =
                ReadFixedAscii(
                    reader.ReadBytes(
                        16));

            if (filePosition <
                    HeaderSize ||
                diskSize <
                    0 ||
                fullSize <
                    0 ||
                (long)filePosition +
                    diskSize >
                    stream.Length)
            {
                throw new InvalidDataException(
                    $"WAD directory entry {index} is invalid.");
            }

            entries.Add(
                new DirectoryEntry(
                    index,
                    filePosition,
                    diskSize,
                    fullSize,
                    type,
                    compression,
                    name));
        }

        return (
            format,
            directoryOffset,
            entries);
    }

    private static void WriteFixedAsciiAt(
        FileStream stream,
        BinaryWriter writer,
        long position,
        string value,
        int length)
    {
        byte[] bytes =
            Encoding.ASCII.GetBytes(
                value);

        byte[] fixedBytes =
            new byte[
                length];

        Array.Copy(
            bytes,
            fixedBytes,
            bytes.Length);

        stream.Position =
            position;

        writer.Write(
            fixedBytes);
    }

    private static string ReadFixedAscii(
        ReadOnlySpan<byte> bytes)
    {
        int length =
            bytes.IndexOf(
                (byte)0);

        if (length <
            0)
        {
            length =
                bytes.Length;
        }

        return Encoding.ASCII
            .GetString(
                bytes[..length])
            .TrimEnd();
    }

    private static string ReadFixedAscii(
        byte[] bytes)
    {
        return ReadFixedAscii(
            bytes.AsSpan());
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

    private static void TryDelete(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            return;
        }

        try
        {
            if (File.Exists(
                    path))
            {
                File.Delete(
                    path);
            }
        }
        catch
        {
        }
    }

    private sealed record DirectoryEntry(
        int DirectoryIndex,
        int FilePosition,
        int DiskSize,
        int FullSize,
        byte Type,
        byte Compression,
        string Name);

    private sealed record TextureHeader(
        string InternalName,
        int Width,
        int Height,
        IReadOnlyList<int> MipOffsets);
}
