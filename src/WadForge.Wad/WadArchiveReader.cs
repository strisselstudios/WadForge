using System.Buffers.Binary;
using System.IO;
using System.Text;
using WadForge.Core;
using WadForge.Imaging;

namespace WadForge.Wad;

public static class WadArchiveReader
{
    private const int HeaderSize = 12;
    private const int DirectoryEntrySize = 32;
    private const int MipTextureHeaderSize = 40;
    private const int MaximumTextureDimension = 8192;

    private const byte Wad3MipTextureType = 67;
    private const byte Wad2MipTextureType = 68;

    public static WadArchiveReadResult Read(
        string path,
        IReadOnlyList<Rgb24>? wad2Palette,
        bool preserveTransparency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        using BinaryReader reader = new(
            stream,
            Encoding.ASCII,
            true);

        if (stream.Length < HeaderSize)
        {
            throw new InvalidDataException(
                "The file is too small to be a WAD archive.");
        }

        byte[] header = reader.ReadBytes(HeaderSize);

        if (header.Length != HeaderSize)
        {
            throw new EndOfStreamException(
                "The WAD header could not be read.");
        }

        string signature =
            Encoding.ASCII.GetString(header, 0, 4);

        WadFormat format = signature switch
        {
            "WAD2" => WadFormat.Wad2,
            "WAD3" => WadFormat.Wad3,
            _ => throw new InvalidDataException(
                $"Unsupported WAD signature: {signature}")
        };

        if (format == WadFormat.Wad2 &&
            (wad2Palette is null ||
             wad2Palette.Count != 256))
        {
            throw new InvalidOperationException(
                "A valid 256-color palette is required to extract WAD2 textures.");
        }

        int lumpCount =
            BinaryPrimitives.ReadInt32LittleEndian(
                header.AsSpan(4, 4));

        int directoryOffset =
            BinaryPrimitives.ReadInt32LittleEndian(
                header.AsSpan(8, 4));

        if (lumpCount < 0)
        {
            throw new InvalidDataException(
                "The WAD contains a negative lump count.");
        }

        if (directoryOffset < HeaderSize)
        {
            throw new InvalidDataException(
                "The WAD directory offset is invalid.");
        }

        long directoryLength = checked(
            (long)lumpCount * DirectoryEntrySize);

        long directoryEnd = checked(
            directoryOffset + directoryLength);

        if (directoryEnd > stream.Length)
        {
            throw new InvalidDataException(
                "The WAD directory extends beyond the file.");
        }

        stream.Position = directoryOffset;

        List<DirectoryEntry> entries = new(
            lumpCount);

        for (int index = 0; index < lumpCount; index++)
        {
            int filePosition = reader.ReadInt32();
            int diskSize = reader.ReadInt32();
            int fullSize = reader.ReadInt32();
            byte type = reader.ReadByte();
            byte compression = reader.ReadByte();

            reader.ReadByte();
            reader.ReadByte();

            string name = ReadFixedAscii(
                reader.ReadBytes(16));

            if (filePosition < HeaderSize ||
                diskSize < 0 ||
                fullSize < 0)
            {
                throw new InvalidDataException(
                    $"WAD directory entry {index} is invalid.");
            }

            long lumpEnd = checked(
                (long)filePosition + diskSize);

            if (lumpEnd > stream.Length)
            {
                throw new InvalidDataException(
                    $"WAD lump '{name}' extends beyond the file.");
            }

            entries.Add(
                new DirectoryEntry(
                    filePosition,
                    diskSize,
                    fullSize,
                    type,
                    compression,
                    name));
        }

        List<WadExtractedTexture> textures = new();

        foreach (DirectoryEntry entry in entries)
        {
            if (entry.Type != Wad2MipTextureType &&
                entry.Type != Wad3MipTextureType)
            {
                continue;
            }

            if (entry.Compression != 0)
            {
                throw new NotSupportedException(
                    $"Compressed WAD lump '{entry.Name}' is not supported.");
            }

            if (entry.DiskSize < MipTextureHeaderSize)
            {
                throw new InvalidDataException(
                    $"Texture lump '{entry.Name}' is too small.");
            }

            stream.Position = entry.FilePosition;

            byte[] lumpData =
                reader.ReadBytes(entry.DiskSize);

            if (lumpData.Length != entry.DiskSize)
            {
                throw new EndOfStreamException(
                    $"Texture lump '{entry.Name}' could not be read.");
            }

            textures.Add(
                DecodeTexture(
                    format,
                    entry.Name,
                    lumpData,
                    wad2Palette,
                    preserveTransparency));
        }

        if (textures.Count == 0)
        {
            throw new InvalidDataException(
                "The WAD contains no supported mip textures.");
        }

        return new WadArchiveReadResult(
            format,
            textures);
    }

    private static WadExtractedTexture DecodeTexture(
        WadFormat format,
        string directoryName,
        byte[] lumpData,
        IReadOnlyList<Rgb24>? wad2Palette,
        bool preserveTransparency)
    {
        ReadOnlySpan<byte> span = lumpData;

        string headerName =
            ReadFixedAscii(span[..16]);

        string internalName =
            string.IsNullOrWhiteSpace(headerName)
                ? directoryName
                : headerName;

        int width =
            BinaryPrimitives.ReadInt32LittleEndian(
                span.Slice(16, 4));

        int height =
            BinaryPrimitives.ReadInt32LittleEndian(
                span.Slice(20, 4));

        if (width <= 0 ||
            height <= 0 ||
            width > MaximumTextureDimension ||
            height > MaximumTextureDimension)
        {
            throw new InvalidDataException(
                $"Texture '{internalName}' has invalid dimensions {width} × {height}.");
        }

        int[] offsets = new int[4];

        for (int level = 0; level < 4; level++)
        {
            offsets[level] =
                BinaryPrimitives.ReadInt32LittleEndian(
                    span.Slice(24 + (level * 4), 4));
        }

        int[] mipSizes = new int[4];

        for (int level = 0; level < 4; level++)
        {
            int mipWidth = Math.Max(
                1,
                width >> level);

            int mipHeight = Math.Max(
                1,
                height >> level);

            mipSizes[level] = checked(
                mipWidth * mipHeight);

            if (offsets[level] < MipTextureHeaderSize)
            {
                throw new InvalidDataException(
                    $"Texture '{internalName}' has an invalid mip offset.");
            }

            long mipEnd = checked(
                (long)offsets[level] +
                mipSizes[level]);

            if (mipEnd > lumpData.Length)
            {
                throw new InvalidDataException(
                    $"Texture '{internalName}' has truncated mip data.");
            }
        }

        IReadOnlyList<Rgb24> palette;

        if (format == WadFormat.Wad3)
        {
            int paletteOffset = checked(
                offsets[3] + mipSizes[3]);

            if (paletteOffset + 2 > lumpData.Length)
            {
                throw new InvalidDataException(
                    $"Texture '{internalName}' has no WAD3 palette.");
            }

            ushort paletteCount =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    span.Slice(paletteOffset, 2));

            if (paletteCount != 256)
            {
                throw new InvalidDataException(
                    $"Texture '{internalName}' declares {paletteCount} palette colors instead of 256.");
            }

            int paletteDataOffset =
                paletteOffset + 2;

            int paletteByteLength =
                paletteCount * 3;

            if (paletteDataOffset +
                paletteByteLength >
                lumpData.Length)
            {
                throw new InvalidDataException(
                    $"Texture '{internalName}' has a truncated WAD3 palette.");
            }

            Rgb24[] embeddedPalette =
                new Rgb24[256];

            for (int index = 0; index < 256; index++)
            {
                int colorOffset =
                    paletteDataOffset +
                    (index * 3);

                embeddedPalette[index] =
                    new Rgb24(
                        lumpData[colorOffset],
                        lumpData[colorOffset + 1],
                        lumpData[colorOffset + 2]);
            }

            palette = embeddedPalette;
        }
        else
        {
            palette = wad2Palette ??
                throw new InvalidOperationException(
                    "A WAD2 palette was not supplied.");
        }

        int pixelCount = checked(
            width * height);

        ReadOnlySpan<byte> indexedPixels =
            span.Slice(
                offsets[0],
                pixelCount);

        bool transparentTexture =
            preserveTransparency &&
            internalName.StartsWith(
                "{",
                StringComparison.Ordinal);

        Rgba32[] pixels =
            new Rgba32[pixelCount];

        for (int index = 0;
             index < pixelCount;
             index++)
        {
            byte paletteIndex =
                indexedPixels[index];

            Rgb24 color =
                palette[paletteIndex];

            byte alpha =
                transparentTexture &&
                paletteIndex == 255
                    ? (byte)0
                    : (byte)255;

            pixels[index] =
                new Rgba32(
                    color.R,
                    color.G,
                    color.B,
                    alpha);
        }

        return new WadExtractedTexture(
            internalName,
            width,
            height,
            transparentTexture,
            new RgbaImage(
                width,
                height,
                pixels));
    }

    private static string ReadFixedAscii(
        ReadOnlySpan<byte> bytes)
    {
        int length = bytes.IndexOf((byte)0);

        if (length < 0)
        {
            length = bytes.Length;
        }

        return Encoding.ASCII
            .GetString(bytes[..length])
            .TrimEnd();
    }

    private static string ReadFixedAscii(
        byte[] bytes)
    {
        return ReadFixedAscii(
            bytes.AsSpan());
    }

    private sealed record DirectoryEntry(
        int FilePosition,
        int DiskSize,
        int FullSize,
        byte Type,
        byte Compression,
        string Name);
}
