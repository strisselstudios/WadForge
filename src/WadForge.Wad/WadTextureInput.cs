namespace WadForge.Wad;

public sealed record WadTextureInput(
    string SourcePath,
    string DisplayName,
    bool HasTransparency);
