namespace TrenchBroom.Companion.Core;

public sealed class CompanionSettings
{
    public string? TrenchBroomExecutablePath { get; set; }

    public string? ManagedDataRootPath { get; set; }

    public string? LastProjectGameId { get; set; }

    public string? LastWorkspaceDriveRoot { get; set; }

    public List<string> RecentProjectDirectories { get; set; } = new();

    public List<string> RegisteredWadPaths { get; set; } = new();
}