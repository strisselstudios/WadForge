using WadForge.Core;

namespace WadForge.Wad;

public sealed record WadArchiveReadResult(
    WadFormat Format,
    IReadOnlyList<WadExtractedTexture> Textures);
