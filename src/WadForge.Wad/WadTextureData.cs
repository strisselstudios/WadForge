using WadForge.Imaging;

namespace WadForge.Wad;

internal sealed record WadTextureData(
    string InternalName,
    int Width,
    int Height,
    IReadOnlyList<byte[]> MipLevels,
    IReadOnlyList<Rgb24> Palette);
