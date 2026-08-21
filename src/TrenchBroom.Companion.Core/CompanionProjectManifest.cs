namespace TrenchBroom.Companion.Core;

public sealed class CompanionProjectManifest
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Guid ProjectId { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string GameId { get; set; } = string.Empty;

    public string? ModName { get; set; }

    public CompanionProjectGameBinding? GameBinding { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? ActiveMapPath { get; set; }

    public List<CompanionProjectMap> Maps { get; set; } = new();
}
