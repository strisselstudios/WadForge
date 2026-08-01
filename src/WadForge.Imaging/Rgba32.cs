namespace WadForge.Imaging;

public readonly record struct Rgba32(
    byte R,
    byte G,
    byte B,
    byte A)
{
    public bool IsTransparent(byte threshold = 128)
    {
        return A < threshold;
    }
}
