namespace WadForge.Aliases;

public sealed record TextureAliasEntry(
    string DisplayName,
    string InternalName,
    string SourceFileName,
    int? OriginalWidth = null,
    int? OriginalHeight = null,
    int? StoredWidth = null,
    int? StoredHeight = null);