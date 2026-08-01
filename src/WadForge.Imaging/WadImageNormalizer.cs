namespace WadForge.Imaging;

public static class WadImageNormalizer
{
    public const int DimensionMultiple = 16;
    public const int MaximumDimension = 4096;

    public static RgbaImage Normalize(
        RgbaImage source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Width > MaximumDimension ||
            source.Height > MaximumDimension)
        {
            throw new InvalidOperationException(
                $"Images larger than {MaximumDimension} × " +
                $"{MaximumDimension} are not accepted.");
        }

        int targetWidth = RoundUp(
            source.Width,
            DimensionMultiple);

        int targetHeight = RoundUp(
            source.Height,
            DimensionMultiple);

        if (targetWidth == source.Width &&
            targetHeight == source.Height)
        {
            return source.Clone();
        }

        Rgba32[] targetPixels = new Rgba32[
            checked(targetWidth * targetHeight)];

        for (int y = 0; y < targetHeight; y++)
        {
            int sourceY = Math.Min(
                y,
                source.Height - 1);

            for (int x = 0; x < targetWidth; x++)
            {
                int sourceX = Math.Min(
                    x,
                    source.Width - 1);

                targetPixels[(y * targetWidth) + x] =
                    source[sourceX, sourceY];
            }
        }

        return new RgbaImage(
            targetWidth,
            targetHeight,
            targetPixels);
    }

    private static int RoundUp(
        int value,
        int multiple)
    {
        int remainder = value % multiple;

        return remainder == 0
            ? value
            : checked(value + multiple - remainder);
    }
}
