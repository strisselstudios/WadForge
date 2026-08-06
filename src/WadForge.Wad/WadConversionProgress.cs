namespace WadForge.Wad;

public sealed record WadConversionProgress(
    int Completed,
    int Total,
    string CurrentTextureName);
