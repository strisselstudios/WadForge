namespace WadForge.Aliases;

public sealed record WadAliasManifest(
    int SchemaVersion,
    string WadFileName,
    string WadSha256,
    string WadFormat,
    string? PaletteFileName,
    IReadOnlyList<TextureAliasEntry> Textures);
