using WadForge.Imaging;

namespace WadForge.Wad;

public sealed record WadExtractedTexture(
    string InternalName,
    int Width,
    int Height,
    bool HasTransparency,
    RgbaImage Image);
