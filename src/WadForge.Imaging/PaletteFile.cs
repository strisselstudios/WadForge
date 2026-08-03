using System.Buffers.Binary;
using System.IO;

namespace WadForge.Imaging;

public static class PaletteFile
{
    public const int PaletteColorCount = 256;
    public const int RawPaletteLength =
        PaletteColorCount * 3;

    public static Rgb24[] Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] data = File.ReadAllBytes(path);

        int offset;

        if (data.Length == RawPaletteLength)
        {
            offset = 0;
        }
        else if (
            data.Length == RawPaletteLength + 2 &&
            BinaryPrimitives.ReadUInt16LittleEndian(
                data.AsSpan(0, 2)) ==
            PaletteColorCount)
        {
            offset = 2;
        }
        else
        {
            throw new InvalidDataException(
                "A WAD2 palette must contain exactly " +
                "768 raw RGB bytes, or a 2-byte value of " +
                "256 followed by 768 RGB bytes.");
        }

        Rgb24[] colors = new Rgb24[
            PaletteColorCount];

        for (int index = 0;
             index < PaletteColorCount;
             index++)
        {
            int colorOffset =
                offset + (index * 3);

            colors[index] = new Rgb24(
                data[colorOffset],
                data[colorOffset + 1],
                data[colorOffset + 2]);
        }

        return colors;
    }

    public static void WriteRaw(
        string path,
        IReadOnlyList<Rgb24> colors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(colors);

        if (colors.Count != PaletteColorCount)
        {
            throw new ArgumentException(
                "The palette must contain exactly 256 colors.",
                nameof(colors));
        }

        byte[] data = new byte[
            RawPaletteLength];

        for (int index = 0;
             index < colors.Count;
             index++)
        {
            Rgb24 color = colors[index];
            int offset = index * 3;

            data[offset] = color.R;
            data[offset + 1] = color.G;
            data[offset + 2] = color.B;
        }

        File.WriteAllBytes(
            path,
            data);
    }
}