namespace WadForge.Wad;

public sealed record WadConversionResult(
    string WadPath,
    string ManifestPath,
    string? PalettePath,
    int TextureCount,
    string WadSha256);
