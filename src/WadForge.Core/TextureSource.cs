namespace WadForge.Core;

public sealed record TextureSource(
    string SourcePath,
    string DisplayName,
    string InternalName,
    int Width,
    int Height);
