using System.Text;

namespace TrenchBroom.Companion.Core;

public sealed record CompanionRobustWadTextureCatalog(
    string WadFormat,
    IReadOnlyList<CompanionWadTextureEntry> Textures,
    int TotalEntryCount,
    int MipTextureCandidateCount,
    int SkippedMipTextureCount,
    IReadOnlyList<string> Warnings);

public static class CompanionRobustWadTextureCatalogService
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

    public static CompanionRobustWadTextureCatalog ReadCatalog(
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

        List<string> warnings =
            new();

        int candidates =
            0;

        foreach (DirectoryEntry entry in
                 entries)
        {
            if (entry.Type !=
                expectedType)
            {
                continue;
            }

            candidates++;

            if (entry.Compression !=
                0)
            {
                warnings.Add(
                    $"{entry.Name}: compressed WAD lump type {entry.Compression} is not previewable yet.");

                continue;
            }

            try
            {
                (string name, int width, int height) =
                    ReadTextureHeader(
                        reader,
                        stream,
                        entry);

                textures.Add(
                    new CompanionWadTextureEntry(
                        entry.DirectoryIndex,
                        name,
                        width,
                        height,
                        string.Equals(
                            format,
                            "WAD3",
                            StringComparison.Ordinal)));
            }
            catch (Exception exception)
                when (exception is
                      InvalidDataException or
                      EndOfStreamException or
                      OverflowException)
            {
                warnings.Add(
                    $"{entry.Name}: {exception.Message}");
            }
        }

        return new CompanionRobustWadTextureCatalog(
            format,
            textures,
            entries.Count,
            candidates,
            candidates -
                textures.Count,
            warnings);
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
                continue;
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

    private static (string Name, int Width, int Height) ReadTextureHeader(
        BinaryReader reader,
        FileStream stream,
        DirectoryEntry entry)
    {
        if (entry.DiskSize <
            MipTextureHeaderSize)
        {
            throw new InvalidDataException(
                "smaller than a miptexture header.");
        }

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
                $"invalid dimensions {width} x {height}.");
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
                "invalid mip offsets.");
        }

        int mip0Size =
            checked(
                width *
                height);

        long mip0End =
            checked(
                (long)entry.FilePosition +
                mip0 +
                mip0Size);

        long entryEnd =
            checked(
                (long)entry.FilePosition +
                entry.DiskSize);

        if (mip0End >
                entryEnd ||
            mip0End >
                stream.Length)
        {
            throw new InvalidDataException(
                "base mip pixels are outside the lump bounds.");
        }

        string name =
            string.IsNullOrWhiteSpace(
                internalName)
                ? entry.Name
                : internalName;

        return (
            name,
            width,
            height);
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
