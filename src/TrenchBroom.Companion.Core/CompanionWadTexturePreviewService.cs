using System.Text;
using WadForge.Aliases;

namespace TrenchBroom.Companion.Core;

public sealed record CompanionWadTextureEntry(
    int DirectoryIndex,
    string Name,
    int Width,
    int Height,
    bool HasEmbeddedPalette)
{
    public string DimensionsText =>
        $"{Width:N0} x {Height:N0}";
}

public sealed record CompanionWadTextureCatalog(
    string WadFormat,
    IReadOnlyList<CompanionWadTextureEntry> Textures);

public sealed record CompanionWadTexturePreview(
    string Name,
    int Width,
    int Height,
    bool HasTransparency,
    byte[] BgraPixels,
    string PaletteDescription);

public static class CompanionWadTexturePreviewService
{
    private const int HeaderSize = 12;
    private const int DirectoryEntrySize = 32;
    private const int MipTextureHeaderSize = 40;
    private const int MaximumTextureDimension = 8192;
    private const byte Wad3MipTextureType = 67;
    private const byte Wad2MipTextureType = 68;

    private sealed record DirectoryEntry(
        int DirectoryIndex,
        int FilePosition,
        int DiskSize,
        int FullSize,
        byte Type,
        byte Compression,
        string Name);

    private sealed record TextureHeader(
        string Name,
        int Width,
        int Height,
        int Mip0Offset,
        int Mip1Offset,
        int Mip2Offset,
        int Mip3Offset);

    public static IReadOnlyDictionary<string, string>
        ReadAliasDisplayNames(
            string manifestPath)
    {
        Dictionary<string, string> aliases =
            new(
                StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(
                manifestPath) ||
            !File.Exists(
                manifestPath))
        {
            return aliases;
        }

        WadAliasManifest manifest =
            WadAliasManifestSerializer.Read(
                manifestPath);

        foreach (TextureAliasEntry entry in
                 manifest.Textures)
        {
            if (string.IsNullOrWhiteSpace(
                    entry.InternalName) ||
                string.IsNullOrWhiteSpace(
                    entry.DisplayName))
            {
                continue;
            }

            aliases[entry.InternalName] =
                entry.DisplayName;
        }

        return aliases;
    }

    public static string? ReadManifestPaletteFileName(
        string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(
                manifestPath) ||
            !File.Exists(
                manifestPath))
        {
            return null;
        }

        WadAliasManifest manifest =
            WadAliasManifestSerializer.Read(
                manifestPath);

        return string.IsNullOrWhiteSpace(
                manifest.PaletteFileName)
                ? null
                : manifest.PaletteFileName.Trim();
    }

    public static CompanionWadTextureCatalog ReadCatalog(
        string wadPath)
    {
        string fullPath =
            RequireWadPath(
                wadPath);

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
                leaveOpen: true);

        string format =
            ReadFormat(
                reader,
                stream);

        IReadOnlyList<DirectoryEntry> entries =
            ReadDirectory(
                reader,
                stream);

        byte expectedType =
            string.Equals(
                format,
                "WAD3",
                StringComparison.Ordinal)
                ? Wad3MipTextureType
                : Wad2MipTextureType;

        List<CompanionWadTextureEntry> textures =
            new();

        foreach (DirectoryEntry entry in
                 entries)
        {
            if (entry.Type !=
                    expectedType ||
                entry.Compression !=
                    0)
            {
                continue;
            }

            TextureHeader header =
                ReadTextureHeader(
                    reader,
                    stream,
                    entry);

            textures.Add(
                new CompanionWadTextureEntry(
                    entry.DirectoryIndex,
                    header.Name,
                    header.Width,
                    header.Height,
                    string.Equals(
                        format,
                        "WAD3",
                        StringComparison.Ordinal)));
        }

        return new CompanionWadTextureCatalog(
            format,
            textures);
    }

    public static CompanionWadTexturePreview ReadPreview(
        string wadPath,
        int directoryIndex,
        string? wad2PalettePath)
    {
        string fullPath =
            RequireWadPath(
                wadPath);

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
                leaveOpen: true);

        string format =
            ReadFormat(
                reader,
                stream);

        IReadOnlyList<DirectoryEntry> entries =
            ReadDirectory(
                reader,
                stream);

        DirectoryEntry entry =
            entries.FirstOrDefault(
                candidate =>
                    candidate.DirectoryIndex ==
                    directoryIndex) ??
            throw new InvalidDataException(
                "The selected texture no longer exists in this WAD.");

        byte expectedType =
            string.Equals(
                format,
                "WAD3",
                StringComparison.Ordinal)
                ? Wad3MipTextureType
                : Wad2MipTextureType;

        if (entry.Type !=
                expectedType ||
            entry.Compression !=
                0)
        {
            throw new InvalidDataException(
                "The selected WAD lump is not an uncompressed miptexture.");
        }

        TextureHeader header =
            ReadTextureHeader(
                reader,
                stream,
                entry);

        int pixelCount =
            checked(
                header.Width *
                header.Height);

        long mip0Position =
            checked(
                (long)entry.FilePosition +
                header.Mip0Offset);

        EnsureRange(
            mip0Position,
            pixelCount,
            entry,
            stream.Length,
            "base mip pixels");

        stream.Position =
            mip0Position;

        byte[] indices =
            reader.ReadBytes(
                pixelCount);

        if (indices.Length !=
            pixelCount)
        {
            throw new EndOfStreamException(
                "The selected texture pixels are truncated.");
        }

        byte[] palette;
        string paletteDescription;

        if (string.Equals(
                format,
                "WAD3",
                StringComparison.Ordinal))
        {
            int mip3Width =
                Math.Max(
                    1,
                    header.Width /
                    8);

            int mip3Height =
                Math.Max(
                    1,
                    header.Height /
                    8);

            int mip3Size =
                checked(
                    mip3Width *
                    mip3Height);

            long paletteCountPosition =
                checked(
                    (long)entry.FilePosition +
                    header.Mip3Offset +
                    mip3Size);

            EnsureRange(
                paletteCountPosition,
                2 + (256 * 3),
                entry,
                stream.Length,
                "embedded palette");

            stream.Position =
                paletteCountPosition;

            ushort colorCount =
                reader.ReadUInt16();

            if (colorCount !=
                256)
            {
                throw new InvalidDataException(
                    $"Texture '{header.Name}' contains {colorCount} palette colors; Companion expected 256.");
            }

            palette =
                reader.ReadBytes(
                    256 *
                    3);

            if (palette.Length !=
                768)
            {
                throw new EndOfStreamException(
                    "The selected WAD3 texture palette is truncated.");
            }

            paletteDescription =
                "Embedded WAD3 palette";
        }
        else if (!string.IsNullOrWhiteSpace(
                     wad2PalettePath))
        {
            string palettePath =
                Path.GetFullPath(
                    wad2PalettePath);

            if (!File.Exists(
                    palettePath))
            {
                throw new FileNotFoundException(
                    "The selected WAD2 preview palette could not be found.",
                    palettePath);
            }

            palette =
                File.ReadAllBytes(
                    palettePath);

            if (palette.Length !=
                768)
            {
                throw new InvalidDataException(
                    "A WAD2 preview palette must contain exactly 768 bytes: 256 RGB colors.");
            }

            paletteDescription =
                $"External game palette ({Path.GetFileName(palettePath)})";
        }
        else
        {
            palette =
                CreateNeutralPreviewPalette();

            paletteDescription =
                "Neutral indexed preview palette (no reliable external palette was found)";
        }

        byte[] bgra =
            new byte[
                checked(
                    pixelCount *
                    4)];

        bool transparentName =
            header.Name.StartsWith(
                "{",
                StringComparison.Ordinal);

        bool hasTransparency =
            false;

        for (int index = 0;
             index < pixelCount;
             index++)
        {
            byte paletteIndex =
                indices[index];

            int paletteOffset =
                paletteIndex *
                3;

            int outputOffset =
                index *
                4;

            bgra[outputOffset] =
                palette[paletteOffset + 2];

            bgra[outputOffset + 1] =
                palette[paletteOffset + 1];

            bgra[outputOffset + 2] =
                palette[paletteOffset];

            byte alpha =
                transparentName &&
                paletteIndex ==
                255
                    ? (byte)0
                    : (byte)255;

            bgra[outputOffset + 3] =
                alpha;

            if (alpha <
                255)
            {
                hasTransparency =
                    true;
            }
        }

        return new CompanionWadTexturePreview(
            header.Name,
            header.Width,
            header.Height,
            hasTransparency,
            bgra,
            paletteDescription);
    }

    private static byte[] CreateNeutralPreviewPalette()
    {
        byte[] palette =
            new byte[
                256 *
                3];

        for (int index = 0;
             index < 256;
             index++)
        {
            byte value =
                (byte)index;

            int offset =
                index *
                3;

            palette[offset] =
                value;

            palette[offset + 1] =
                value;

            palette[offset + 2] =
                value;
        }

        return palette;
    }

    private static string RequireWadPath(
        string wadPath)
    {
        if (string.IsNullOrWhiteSpace(
                wadPath))
        {
            throw new ArgumentException(
                "A WAD path is required.",
                nameof(wadPath));
        }

        string fullPath =
            Path.GetFullPath(
                wadPath);

        if (!File.Exists(
                fullPath))
        {
            throw new FileNotFoundException(
                "The WAD file could not be found.",
                fullPath);
        }

        return fullPath;
    }

    private static string ReadFormat(
        BinaryReader reader,
        FileStream stream)
    {
        if (stream.Length <
            HeaderSize)
        {
            throw new InvalidDataException(
                "The file is too small to be a WAD archive.");
        }

        stream.Position =
            0;

        string signature =
            Encoding.ASCII.GetString(
                reader.ReadBytes(
                    4));

        if (!string.Equals(
                signature,
                "WAD2",
                StringComparison.Ordinal) &&
            !string.Equals(
                signature,
                "WAD3",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported WAD signature: {signature}");
        }

        return signature;
    }

    private static IReadOnlyList<DirectoryEntry> ReadDirectory(
        BinaryReader reader,
        FileStream stream)
    {
        stream.Position =
            4;

        int lumpCount =
            reader.ReadInt32();

        int directoryOffset =
            reader.ReadInt32();

        if (lumpCount <
                0 ||
            lumpCount >
                1_000_000)
        {
            throw new InvalidDataException(
                "The WAD contains an invalid lump count.");
        }

        long directoryLength =
            checked(
                (long)lumpCount *
                DirectoryEntrySize);

        if (directoryOffset <
                HeaderSize ||
            checked(
                (long)directoryOffset +
                directoryLength) >
                stream.Length)
        {
            throw new InvalidDataException(
                "The WAD directory is outside the file bounds.");
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

            reader.ReadUInt16();

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
                checked(
                    (long)filePosition +
                    diskSize) >
                    stream.Length)
            {
                throw new InvalidDataException(
                    $"WAD directory entry {index} is outside the file bounds.");
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

        return entries;
    }

    private static TextureHeader ReadTextureHeader(
        BinaryReader reader,
        FileStream stream,
        DirectoryEntry entry)
    {
        if (entry.DiskSize <
            MipTextureHeaderSize)
        {
            throw new InvalidDataException(
                $"Texture '{entry.Name}' is smaller than a miptexture header.");
        }

        EnsureRange(
            entry.FilePosition,
            MipTextureHeaderSize,
            entry,
            stream.Length,
            "miptexture header");

        stream.Position =
            entry.FilePosition;

        string internalName =
            ReadFixedAscii(
                reader.ReadBytes(
                    16));

        int width =
            reader.ReadInt32();

        int height =
            reader.ReadInt32();

        int mip0 =
            reader.ReadInt32();

        int mip1 =
            reader.ReadInt32();

        int mip2 =
            reader.ReadInt32();

        int mip3 =
            reader.ReadInt32();

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
                $"Texture '{entry.Name}' has invalid dimensions {width} x {height}.");
        }

        if (mip0 <
                MipTextureHeaderSize ||
            mip1 <=
                mip0 ||
            mip2 <=
                mip1 ||
            mip3 <=
                mip2)
        {
            throw new InvalidDataException(
                $"Texture '{entry.Name}' has invalid mip offsets.");
        }

        string name =
            string.IsNullOrWhiteSpace(
                internalName)
                ? entry.Name
                : internalName;

        return new TextureHeader(
            name,
            width,
            height,
            mip0,
            mip1,
            mip2,
            mip3);
    }

    private static void EnsureRange(
        long position,
        int length,
        DirectoryEntry entry,
        long streamLength,
        string description)
    {
        if (position <
                entry.FilePosition ||
            length <
                0 ||
            checked(
                position +
                length) >
                checked(
                    (long)entry.FilePosition +
                    entry.DiskSize) ||
            checked(
                position +
                length) >
                streamLength)
        {
            throw new InvalidDataException(
                $"Texture '{entry.Name}' has invalid {description} bounds.");
        }
    }

    private static string ReadFixedAscii(
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

        return Encoding.ASCII
            .GetString(
                bytes,
                0,
                length)
            .Trim();
    }
}
