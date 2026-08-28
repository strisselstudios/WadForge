using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrenchBroom.Companion.Core;

public enum CompanionCommunityWadCompatibilityState
{
    Unknown,
    Compatible,
    Incompatible,
    Unavailable
}

public sealed record CompanionCommunityWadCompatibilityRecord(
    string SourceId,
    string SourceDisplayName,
    string FileName,
    string DownloadUri,
    CompanionCommunityWadCompatibilityState State,
    string Reason,
    int CompatibleWadCount,
    int HiddenItemCount,
    DateTimeOffset LastValidatedUtc);

public sealed class CompanionCommunityWadCompatibilityCatalog
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    private readonly string _catalogPath;

    private readonly Dictionary<string, CompanionCommunityWadCompatibilityRecord>
        _records;

    private CompanionCommunityWadCompatibilityCatalog(
        string catalogPath,
        IEnumerable<CompanionCommunityWadCompatibilityRecord> records)
    {
        _catalogPath =
            catalogPath;

        _records =
            new Dictionary<string, CompanionCommunityWadCompatibilityRecord>(
                StringComparer.OrdinalIgnoreCase);

        foreach (CompanionCommunityWadCompatibilityRecord record in
                 records)
        {
            if (string.IsNullOrWhiteSpace(
                    record.SourceId) ||
                string.IsNullOrWhiteSpace(
                    record.DownloadUri))
            {
                continue;
            }

            _records[BuildKey(
                record.SourceId,
                record.DownloadUri)] =
                record;
        }
    }

    public string CatalogPath =>
        _catalogPath;

    public static CompanionCommunityWadCompatibilityCatalog Load(
        string managedDataRoot)
    {
        if (string.IsNullOrWhiteSpace(
                managedDataRoot))
        {
            throw new ArgumentException(
                "A Companion managed data root is required.",
                nameof(managedDataRoot));
        }

        string directory =
            Path.Combine(
                Path.GetFullPath(
                    managedDataRoot),
                "Cache",
                "CommunityWads");

        Directory.CreateDirectory(
            directory);

        string catalogPath =
            Path.Combine(
                directory,
                "validation-v1.json");

        IReadOnlyList<CompanionCommunityWadCompatibilityRecord> seedRecords =
            LoadSeedRecords();

        if (!File.Exists(
                catalogPath))
        {
            return new CompanionCommunityWadCompatibilityCatalog(
                catalogPath,
                seedRecords);
        }

        try
        {
            string json =
                File.ReadAllText(
                    catalogPath);

            CatalogDocument? document =
                JsonSerializer.Deserialize<CatalogDocument>(
                    json,
                    JsonOptions);

            if (document is null ||
                document.SchemaVersion !=
                    SchemaVersion)
            {
                return new CompanionCommunityWadCompatibilityCatalog(
                    catalogPath,
                    seedRecords);
            }

            IEnumerable<CompanionCommunityWadCompatibilityRecord> mergedRecords =
                seedRecords.Concat(
                    document.Entries ??
                        Array.Empty<CompanionCommunityWadCompatibilityRecord>());

            return new CompanionCommunityWadCompatibilityCatalog(
                catalogPath,
                mergedRecords);
        }
        catch (Exception exception)
            when (exception is
                  IOException or
                  UnauthorizedAccessException or
                  JsonException)
        {
            return new CompanionCommunityWadCompatibilityCatalog(
                catalogPath,
                seedRecords);
        }
    }

    public CompanionCommunityWadCompatibilityRecord? Find(
        CompanionOnlineWadEntry entry)
    {
        ArgumentNullException.ThrowIfNull(
            entry);

        _records.TryGetValue(
            BuildKey(
                entry.SourceId,
                entry.DownloadUri.AbsoluteUri),
            out CompanionCommunityWadCompatibilityRecord? record);

        return record;
    }

    public bool ShouldHide(
        CompanionOnlineWadEntry entry)
    {
        CompanionCommunityWadCompatibilityRecord? record =
            Find(
                entry);

        return record?.State is
            CompanionCommunityWadCompatibilityState.Incompatible or
            CompanionCommunityWadCompatibilityState.Unavailable;
    }

    public void Record(
        CompanionOnlineWadEntry entry,
        CompanionCommunityWadCompatibilityState state,
        string reason,
        int compatibleWadCount,
        int hiddenItemCount)
    {
        ArgumentNullException.ThrowIfNull(
            entry);

        if (state ==
            CompanionCommunityWadCompatibilityState.Unknown)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "Unknown is not a persisted validation result.");
        }

        CompanionCommunityWadCompatibilityRecord record =
            new(
                entry.SourceId,
                entry.SourceDisplayName,
                entry.FileName,
                entry.DownloadUri.AbsoluteUri,
                state,
                reason ??
                    string.Empty,
                Math.Max(
                    0,
                    compatibleWadCount),
                Math.Max(
                    0,
                    hiddenItemCount),
                DateTimeOffset.UtcNow);

        _records[BuildKey(
            record.SourceId,
            record.DownloadUri)] =
                record;

        Save();
    }

    public IReadOnlyList<CompanionCommunityWadCompatibilityRecord>
        GetRecordsForSource(
            string sourceId)
    {
        return _records.Values
            .Where(
                record =>
                    string.Equals(
                        record.SourceId,
                        sourceId,
                        StringComparison.OrdinalIgnoreCase))
            .OrderBy(
                record =>
                    record.FileName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<CompanionCommunityWadCompatibilityRecord>
        LoadSeedRecords()
    {
        try
        {
            string json =
                CompanionCommunityWadCompatibilitySeed.GetJson();

            if (string.IsNullOrWhiteSpace(
                    json))
            {
                return Array.Empty<CompanionCommunityWadCompatibilityRecord>();
            }

            CatalogDocument? document =
                JsonSerializer.Deserialize<CatalogDocument>(
                    json,
                    JsonOptions);

            if (document is null ||
                document.SchemaVersion !=
                    SchemaVersion)
            {
                return Array.Empty<CompanionCommunityWadCompatibilityRecord>();
            }

            return document.Entries ??
                Array.Empty<CompanionCommunityWadCompatibilityRecord>();
        }
        catch (Exception exception)
            when (exception is
                  FormatException or
                  JsonException)
        {
            return Array.Empty<CompanionCommunityWadCompatibilityRecord>();
        }
    }

    private void Save()
    {
        string? directory =
            Path.GetDirectoryName(
                _catalogPath);

        if (string.IsNullOrWhiteSpace(
                directory))
        {
            throw new InvalidOperationException(
                "Community WAD compatibility catalog directory is unavailable.");
        }

        Directory.CreateDirectory(
            directory);

        CatalogDocument document =
            new(
                SchemaVersion,
                DateTimeOffset.UtcNow,
                _records.Values
                    .OrderBy(
                        record =>
                            record.SourceId,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        record =>
                            record.FileName,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        string json =
            JsonSerializer.Serialize(
                document,
                JsonOptions);

        string temporaryPath =
            _catalogPath +
            ".tmp";

        File.WriteAllText(
            temporaryPath,
            json);

        File.Move(
            temporaryPath,
            _catalogPath,
            overwrite:
                true);
    }

    private static string BuildKey(
        string sourceId,
        string downloadUri)
    {
        return sourceId.Trim() +
            "|" +
            downloadUri.Trim();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options =
            new()
            {
                WriteIndented =
                    true,
                PropertyNameCaseInsensitive =
                    true
            };

        options.Converters.Add(
            new JsonStringEnumConverter());

        return options;
    }

    private sealed record CatalogDocument(
        int SchemaVersion,
        DateTimeOffset UpdatedUtc,
        IReadOnlyList<CompanionCommunityWadCompatibilityRecord> Entries);
}
