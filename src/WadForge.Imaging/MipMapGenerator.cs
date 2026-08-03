namespace WadForge.Imaging;

public static class MipMapGenerator
{
    public const int MipLevelCount = 4;

    public static IReadOnlyList<RgbaImage> CreateFourLevels(
        RgbaImage source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<RgbaImage> levels = new(
            MipLevelCount)
        {
            source
        };

        while (levels.Count < MipLevelCount)
        {
            levels.Add(Downsample(
                levels[^1]));
        }

        return levels;
    }

    private static RgbaImage Downsample(
        RgbaImage source)
    {
        int targetWidth = Math.Max(
            1,
            source.Width / 2);

        int targetHeight = Math.Max(
            1,
            source.Height / 2);

        Rgba32[] targetPixels = new Rgba32[
            checked(targetWidth * targetHeight)];

        for (int y = 0; y < targetHeight; y++)
        {
            int sourceY = y * 2;

            for (int x = 0; x < targetWidth; x++)
            {
                int sourceX = x * 2;

                Rgba32 first = source[
                    Math.Min(sourceX, source.Width - 1),
                    Math.Min(sourceY, source.Height - 1)];

                Rgba32 second = source[
                    Math.Min(sourceX + 1, source.Width - 1),
                    Math.Min(sourceY, source.Height - 1)];

                Rgba32 third = source[
                    Math.Min(sourceX, source.Width - 1),
                    Math.Min(sourceY + 1, source.Height - 1)];

                Rgba32 fourth = source[
                    Math.Min(sourceX + 1, source.Width - 1),
                    Math.Min(sourceY + 1, source.Height - 1)];

                targetPixels[(y * targetWidth) + x] =
                    AveragePremultiplied(
                        first,
                        second,
                        third,
                        fourth);
            }
        }

        return new RgbaImage(
            targetWidth,
            targetHeight,
            targetPixels);
    }

    private static Rgba32 AveragePremultiplied(
        Rgba32 first,
        Rgba32 second,
        Rgba32 third,
        Rgba32 fourth)
    {
        int alphaTotal =
            first.A +
            second.A +
            third.A +
            fourth.A;

        byte averageAlpha = (byte)(
            alphaTotal / 4);

        if (alphaTotal == 0)
        {
            return new Rgba32(
                0,
                0,
                0,
                0);
        }

        int redTotal =
            (first.R * first.A) +
            (second.R * second.A) +
            (third.R * third.A) +
            (fourth.R * fourth.A);

        int greenTotal =
            (first.G * first.A) +
            (second.G * second.A) +
            (third.G * third.A) +
            (fourth.G * fourth.A);

        int blueTotal =
            (first.B * first.A) +
            (second.B * second.A) +
            (third.B * third.A) +
            (fourth.B * fourth.A);

        return new Rgba32(
            (byte)(redTotal / alphaTotal),
            (byte)(greenTotal / alphaTotal),
            (byte)(blueTotal / alphaTotal),
            averageAlpha);
    }
}