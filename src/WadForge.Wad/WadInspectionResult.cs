using WadForge.Core;

namespace WadForge.Wad;

public sealed record WadInspectionResult(
    WadFormat Format,
    int LumpCount,
    int DirectoryOffset,
    long FileSize);
