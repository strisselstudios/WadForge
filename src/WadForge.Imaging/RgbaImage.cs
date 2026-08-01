namespace WadForge.Imaging;

public sealed class RgbaImage
{
    public RgbaImage(
        int width,
        int height,
        Rgba32[] pixels)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Image width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                "Image height must be positive.");
        }

        ArgumentNullException.ThrowIfNull(pixels);

        int expectedLength = checked(width * height);

        if (pixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Expected {expectedLength} pixels but received {pixels.Length}.",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public Rgba32[] Pixels { get; }

    public bool HasTransparency
    {
        get
        {
            foreach (Rgba32 pixel in Pixels)
            {
                if (pixel.A < 255)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public Rgba32 this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Width)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            if ((uint)y >= (uint)Height)
            {
                throw new ArgumentOutOfRangeException(nameof(y));
            }

            return Pixels[(y * Width) + x];
        }
    }

    public RgbaImage Clone()
    {
        return new RgbaImage(
            Width,
            Height,
            (Rgba32[])Pixels.Clone());
    }
}
