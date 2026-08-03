namespace TrenchBroom.Companion.Core;

public sealed class CompanionSettings
{
    public string? TrenchBroomExecutablePath { get; set; }

    public List<string> RegisteredWadPaths { get; set; } = new();
}