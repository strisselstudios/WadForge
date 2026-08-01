namespace WadForge.Imaging;

public sealed record ImageQuantizationOptions(
    bool EnableDithering,
    bool PreserveTransparency,
    bool GenerateMipmaps);
