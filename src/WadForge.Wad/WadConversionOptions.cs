using WadForge.Core;

namespace WadForge.Wad;

public sealed record WadConversionOptions(
    WadFormat Format,
    string OutputPath,
    string? Wad2PalettePath,
    bool EnableDithering,
    bool PreserveTransparency);
