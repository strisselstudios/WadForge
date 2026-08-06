namespace WadForge.App.Models;

public sealed record BatchQueueItem(
    string SourcePath,
    string ItemType,
    string DisplayName,
    string InternalName,
    string Details,
    string Status,
    bool HasTransparency)
{
    public string SourceFileName =>
        System.IO.Path.GetFileName(
            SourcePath);
}
