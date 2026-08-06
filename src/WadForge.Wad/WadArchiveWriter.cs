using System.IO;
using System.Text;
using WadForge.Core;
using WadForge.Imaging;

namespace WadForge.Wad;

internal static class WadArchiveWriter
{
    private const int HeaderSize = 12;
    private const int MipTextureHeaderSize = 40;

    private const byte Wad2MipTextureType = 68;
    private const byte Wad3MipTextureType = 67;

    public static void Write(
        string outputPath,
        WadFormat format,
        IReadOnlyList<WadTextureData> textures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(textures);

        if (textures.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one texture is required.");
        }

        string? outputDirectory =
            Path.GetDirectoryName(outputPath);

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException(
                "The WAD output path has no directory.");
        }

        Directory.CreateDirectory(outputDirectory);

        string temporaryPath =
            outputPath + ".temporary";

        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        try
        {
            using (
                FileStream stream = new(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None))
            {
                using BinaryWriter writer = new(
                    stream,
                    Encoding.ASCII,
                    true);

                writer.Write(
                    Encoding.ASCII.GetBytes(
                        format == WadFormat.Wad2
                            ? "WAD2"
                            : "WAD3"));

                writer.Write(textures.Count);
                writer.Write(0);

                List<DirectoryEntry> entries = new(
                    textures.Count);

                foreach (WadTextureData texture in textures)
                {
                    AlignToFourBytes(writer);

                    int filePosition = checked(
                        (int)stream.Position);

                    WriteTextureLump(
                        writer,
                        format,
                        texture);

                    int diskSize = checked(
                        (int)stream.Position -
                        filePosition);

                    entries.Add(
                        new DirectoryEntry(
                            filePosition,
                            diskSize,
                            diskSize,
                            format == WadFormat.Wad2
                                ? Wad2MipTextureType
                                : Wad3MipTextureType,
                            texture.InternalName));
                }

                AlignToFourBytes(writer);

                int directoryOffset = checked(
                    (int)stream.Position);

                foreach (DirectoryEntry entry in entries)
                {
                    writer.Write(entry.FilePosition);
                    writer.Write(entry.DiskSize);
                    writer.Write(entry.FullSize);
                    writer.Write(entry.Type);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((byte)0);

                    WriteFixedAscii(
                        writer,
                        entry.Name,
                        16);
                }

                stream.Position = 8;
                writer.Write(directoryOffset);

                writer.Flush();
                stream.Flush(true);
            }

            File.Move(
                temporaryPath,
                outputPath,
                true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private static void WriteTextureLump(
        BinaryWriter writer,
        WadFormat format,
        WadTextureData texture)
    {
        ValidateTexture(texture);

        WriteFixedAscii(
            writer,
            texture.InternalName,
            16);

        writer.Write(texture.Width);
        writer.Write(texture.Height);

        int[] mipSizes = new int[4];

        for (int level = 0;
             level < mipSizes.Length;
             level++)
        {
            int width =
                texture.Width >> level;

            int height =
                texture.Height >> level;

            mipSizes[level] = checked(
                width * height);
        }

        int[] offsets = new int[4];
        offsets[0] = MipTextureHeaderSize;

        for (int level = 1;
             level < offsets.Length;
             level++)
        {
            offsets[level] = checked(
                offsets[level - 1] +
                mipSizes[level - 1]);
        }

        foreach (int offset in offsets)
        {
            writer.Write(offset);
        }

        foreach (byte[] mipLevel in texture.MipLevels)
        {
            writer.Write(mipLevel);
        }

        if (format == WadFormat.Wad3)
        {
            writer.Write((ushort)256);

            foreach (Rgb24 color in texture.Palette)
            {
                writer.Write(color.R);
                writer.Write(color.G);
                writer.Write(color.B);
            }

            writer.Write((ushort)0);
        }
    }

    private static void ValidateTexture(
        WadTextureData texture)
    {
        if (texture.InternalName.Length > 16)
        {
            throw new InvalidDataException(
                $"Texture name exceeds 16 bytes: {texture.InternalName}");
        }

        if (texture.Width <= 0 ||
            texture.Height <= 0)
        {
            throw new InvalidDataException(
                "Texture dimensions must be positive.");
        }

        if (texture.Width % 16 != 0 ||
            texture.Height % 16 != 0)
        {
            throw new InvalidDataException(
                $"Texture '{texture.InternalName}' does not use dimensions divisible by 16.");
        }

        if (texture.MipLevels.Count != 4)
        {
            throw new InvalidDataException(
                "Exactly four mip levels are required.");
        }

        for (int level = 0;
             level < texture.MipLevels.Count;
             level++)
        {
            int expectedWidth =
                texture.Width >> level;

            int expectedHeight =
                texture.Height >> level;

            int expectedLength = checked(
                expectedWidth * expectedHeight);

            if (texture.MipLevels[level].Length !=
                expectedLength)
            {
                throw new InvalidDataException(
                    $"Mip level {level} for '{texture.InternalName}' has an invalid length.");
            }
        }

        if (texture.Palette.Count != 256)
        {
            throw new InvalidDataException(
                "A 256-color palette is required.");
        }
    }

    private static void AlignToFourBytes(
        BinaryWriter writer)
    {
        long remainder =
            writer.BaseStream.Position % 4;

        if (remainder == 0)
        {
            return;
        }

        int padding = checked(
            (int)(4 - remainder));

        writer.Write(
            new byte[padding]);
    }

    private static void WriteFixedAscii(
        BinaryWriter writer,
        string value,
        int length)
    {
        byte[] bytes =
            Encoding.ASCII.GetBytes(value);

        if (bytes.Length > length)
        {
            throw new InvalidDataException(
                $"ASCII value '{value}' exceeds {length} bytes.");
        }

        writer.Write(bytes);

        int remaining =
            length - bytes.Length;

        if (remaining > 0)
        {
            writer.Write(
                new byte[remaining]);
        }
    }

    private sealed record DirectoryEntry(
        int FilePosition,
        int DiskSize,
        int FullSize,
        byte Type,
        string Name);
}
