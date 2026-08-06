namespace WadForge.Wad;

public sealed record WadExtractionResult(
    int WadCount,
    int TextureCount,
    int RestoredAliasCount,
    IReadOnlyList<string> OutputDirectories,
    IReadOnlyList<string> Warnings);
