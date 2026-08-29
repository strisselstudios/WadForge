using System.Security.Cryptography;
using System.Text.Json;

namespace TrenchBroom.Companion.Core;

public static class CompanionCommunityWadProvenanceService
{
    public const string ProvenanceSuffix =
        ".companion-community.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNamingPolicy =
                JsonNamingPolicy.CamelCase,
            WriteIndented =
                true
        };

    public static void WriteIfMissing(
        string wadPath,
        CompanionOnlineWadEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            wadPath);

        ArgumentNullException.ThrowIfNull(
            entry);

        string fullWadPath =
            Path.GetFullPath(
                wadPath);

        if (!File.Exists(
                fullWadPath))
        {
            throw new FileNotFoundException(
                "The community WAD does not exist.",
                fullWadPath);
        }

        string provenancePath =
            fullWadPath +
            ProvenanceSuffix;

        if (File.Exists(
                provenancePath))
        {
            return;
        }

        var provenance =
            new
            {
                schemaVersion =
                    1,
                sourceId =
                    entry.SourceId,
                sourceDisplayName =
                    entry.SourceDisplayName,
                sourcePageUri =
                    entry.SourcePageUri.AbsoluteUri,
                downloadUri =
                    entry.DownloadUri.AbsoluteUri,
                sourceFileName =
                    entry.FileName,
                wadSha256 =
                    ComputeSha256(
                        fullWadPath),
                importedUnmodified =
                    true
            };

        File.WriteAllText(
            provenancePath,
            JsonSerializer.Serialize(
                provenance,
                JsonOptions));
    }

    private static string ComputeSha256(
        string filePath)
    {
        using FileStream stream =
            File.OpenRead(
                filePath);

        return Convert.ToHexString(
            SHA256.HashData(
                stream));
    }
}