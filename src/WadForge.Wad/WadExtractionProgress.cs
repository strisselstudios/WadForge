namespace WadForge.Wad;

public sealed record WadExtractionProgress(
    int CompletedWads,
    int TotalWads,
    string CurrentItem);
