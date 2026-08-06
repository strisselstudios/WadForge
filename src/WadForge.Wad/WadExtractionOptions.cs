namespace WadForge.Wad;

public sealed record WadExtractionOptions(
    string OutputDirectory,
    string? Wad2PalettePath,
    bool PreserveTransparency,
    bool RestoreAliases);
