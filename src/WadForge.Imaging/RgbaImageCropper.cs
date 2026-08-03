namespace WadForge.Imaging;

public static class RgbaImageCropper
{
    public static RgbaImage CropTopLeft(
        RgbaImage source,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (width <= 0 || width > source.Width)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0 || height > source.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (width == source.Width &&
            height == source.Height)
        {
            return source.Clone();
        }

        Rgba32[] pixels = new Rgba32[
            checked(width * height)];

        for (int y = 0; y < height; y++)
        {
            Array.Copy(
                source.Pixels,
                y * source.Width,
                pixels,
                y * width,
                width);
        }

        return new RgbaImage(
            width,
            height,
            pixels);
    }
}