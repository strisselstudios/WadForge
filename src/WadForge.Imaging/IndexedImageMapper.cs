namespace WadForge.Imaging;

public static class IndexedImageMapper
{
    private const byte TransparentIndex = 255;

    public static byte[] Map(
        RgbaImage image,
        IReadOnlyList<Rgb24> palette,
        bool useTransparency,
        bool enableDithering)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(palette);

        if (palette.Count != 256)
        {
            throw new ArgumentException(
                "The palette must contain exactly 256 colors.",
                nameof(palette));
        }

        return enableDithering
            ? MapWithDithering(
                image,
                palette,
                useTransparency)
            : MapWithoutDithering(
                image,
                palette,
                useTransparency);
    }

    private static byte[] MapWithoutDithering(
        RgbaImage image,
        IReadOnlyList<Rgb24> palette,
        bool useTransparency)
    {
        byte[] result = new byte[
            image.Pixels.Length];

        Dictionary<int, byte> cache = new();

        int availableColorCount =
            useTransparency
                ? 255
                : 256;

        for (int index = 0;
             index < image.Pixels.Length;
             index++)
        {
            Rgba32 pixel = image.Pixels[index];

            if (useTransparency &&
                pixel.IsTransparent())
            {
                result[index] = TransparentIndex;
                continue;
            }

            int packedColor =
                (pixel.R << 16) |
                (pixel.G << 8) |
                pixel.B;

            if (!cache.TryGetValue(
                    packedColor,
                    out byte paletteIndex))
            {
                paletteIndex = FindNearestIndex(
                    pixel.R,
                    pixel.G,
                    pixel.B,
                    palette,
                    availableColorCount);

                cache.Add(
                    packedColor,
                    paletteIndex);
            }

            result[index] = paletteIndex;
        }

        return result;
    }

    private static byte[] MapWithDithering(
        RgbaImage image,
        IReadOnlyList<Rgb24> palette,
        bool useTransparency)
    {
        byte[] result = new byte[
            image.Pixels.Length];

        int width = image.Width;
        int height = image.Height;

        int availableColorCount =
            useTransparency
                ? 255
                : 256;

        double[] currentRed = new double[width + 2];
        double[] currentGreen = new double[width + 2];
        double[] currentBlue = new double[width + 2];

        double[] nextRed = new double[width + 2];
        double[] nextGreen = new double[width + 2];
        double[] nextBlue = new double[width + 2];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex =
                    (y * width) + x;

                Rgba32 sourcePixel =
                    image.Pixels[pixelIndex];

                if (useTransparency &&
                    sourcePixel.IsTransparent())
                {
                    result[pixelIndex] =
                        TransparentIndex;

                    continue;
                }

                double red = Math.Clamp(
                    sourcePixel.R +
                    currentRed[x + 1],
                    0.0,
                    255.0);

                double green = Math.Clamp(
                    sourcePixel.G +
                    currentGreen[x + 1],
                    0.0,
                    255.0);

                double blue = Math.Clamp(
                    sourcePixel.B +
                    currentBlue[x + 1],
                    0.0,
                    255.0);

                byte nearestIndex =
                    FindNearestIndex(
                        (byte)Math.Round(red),
                        (byte)Math.Round(green),
                        (byte)Math.Round(blue),
                        palette,
                        availableColorCount);

                result[pixelIndex] =
                    nearestIndex;

                Rgb24 mappedColor =
                    palette[nearestIndex];

                double redError =
                    red - mappedColor.R;

                double greenError =
                    green - mappedColor.G;

                double blueError =
                    blue - mappedColor.B;

                AddError(
                    currentRed,
                    currentGreen,
                    currentBlue,
                    x + 2,
                    redError,
                    greenError,
                    blueError,
                    7.0 / 16.0);

                AddError(
                    nextRed,
                    nextGreen,
                    nextBlue,
                    x,
                    redError,
                    greenError,
                    blueError,
                    3.0 / 16.0);

                AddError(
                    nextRed,
                    nextGreen,
                    nextBlue,
                    x + 1,
                    redError,
                    greenError,
                    blueError,
                    5.0 / 16.0);

                AddError(
                    nextRed,
                    nextGreen,
                    nextBlue,
                    x + 2,
                    redError,
                    greenError,
                    blueError,
                    1.0 / 16.0);
            }

            (currentRed, nextRed) =
                (nextRed, currentRed);

            (currentGreen, nextGreen) =
                (nextGreen, currentGreen);

            (currentBlue, nextBlue) =
                (nextBlue, currentBlue);

            Array.Clear(nextRed);
            Array.Clear(nextGreen);
            Array.Clear(nextBlue);
        }

        return result;
    }

    private static void AddError(
        double[] red,
        double[] green,
        double[] blue,
        int index,
        double redError,
        double greenError,
        double blueError,
        double factor)
    {
        red[index] += redError * factor;
        green[index] += greenError * factor;
        blue[index] += blueError * factor;
    }

    private static byte FindNearestIndex(
        byte red,
        byte green,
        byte blue,
        IReadOnlyList<Rgb24> palette,
        int availableColorCount)
    {
        long bestDistance = long.MaxValue;
        int bestIndex = 0;

        for (int index = 0;
             index < availableColorCount;
             index++)
        {
            Rgb24 candidate = palette[index];

            int redDifference =
                red - candidate.R;

            int greenDifference =
                green - candidate.G;

            int blueDifference =
                blue - candidate.B;

            long distance =
                (redDifference * redDifference * 30L) +
                (greenDifference * greenDifference * 59L) +
                (blueDifference * blueDifference * 11L);

            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            bestIndex = index;

            if (distance == 0)
            {
                break;
            }
        }

        return checked((byte)bestIndex);
    }
}
