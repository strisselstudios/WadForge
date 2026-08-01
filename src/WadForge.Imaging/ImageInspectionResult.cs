namespace WadForge.Imaging;

public sealed record ImageInspectionResult(
    string ContainerFormat,
    int Width,
    int Height,
    int BitsPerPixel,
    string PixelFormat,
    bool HasTransparency);
