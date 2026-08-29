using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WadForge.Wad;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionCommunityWadRepairManifest
{
    public int SchemaVersion { get; set; } = 1;

    public List<CompanionCommunityWadPackageRepairRule> Packages { get; set; } =
        new();
}

public sealed class CompanionCommunityWadPackageRepairRule
{
    public string RuleId { get; set; } =
        string.Empty;

    public string SourceId { get; set; } =
        string.Empty;

    public string PackageSha256 { get; set; } =
        string.Empty;

    public List<CompanionCommunityWadItemRepairRule> Wads { get; set; } =
        new();
}

public sealed class CompanionCommunityWadItemRepairRule
{
    public string ArchivePath { get; set; } =
        string.Empty;

    public string WadSha256 { get; set; } =
        string.Empty;

    public List<CompanionCommunityWadTextureRepairRule> Textures { get; set; } =
        new();
}

public sealed class CompanionCommunityWadTextureRepairRule
{
    public string InternalName { get; set; } =
        string.Empty;

    public int ExpectedWidth { get; set; }

    public int ExpectedHeight { get; set; }

    public bool AddMaskPrefix { get; set; }

    public int? RemapIndexTo255 { get; set; }
}

public sealed record CompanionCommunityWadRepairOutcome(
    CompanionOnlineWadDownloadResult Package,
    int AppliedRuleCount,
    string? PreservedOriginalPackagePath,
    IReadOnlyList<string> ProvenancePaths);

public static class CompanionCommunityWadRepairService
{
    public const string ProvenanceSuffix =
        ".companion-community.json";

    private const int CurrentSchemaVersion =
        1;

    private const string EmbeddedManifestSuffix =
        ".CommunityWadRepairs.v1.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

    public static CompanionCommunityWadRepairManifest LoadDefaultManifest()
    {
        Assembly assembly =
            typeof(CompanionCommunityWadRepairService).Assembly;

        string[] matches =
            assembly
                .GetManifestResourceNames()
                .Where(
                    name =>
                        name.EndsWith(
                            EmbeddedManifestSuffix,
                            StringComparison.Ordinal))
                .ToArray();

        if (matches.Length !=
            1)
        {
            throw new InvalidDataException(
                $"Expected exactly one embedded community WAD repair manifest ending in '{EmbeddedManifestSuffix}', but found {matches.Length:N0}.");
        }

        using Stream stream =
            assembly.GetManifestResourceStream(
                matches[0]) ??
            throw new InvalidDataException(
                "The embedded community WAD repair manifest could not be opened.");

        CompanionCommunityWadRepairManifest manifest =
            JsonSerializer.Deserialize<CompanionCommunityWadRepairManifest>(
                stream,
                JsonOptions) ??
            throw new InvalidDataException(
                "The embedded community WAD repair manifest is empty or invalid.");

        ValidateManifest(
            manifest);

        return manifest;
    }

    public static CompanionCommunityWadRepairOutcome ApplyCuratedRepairs(
        CompanionOnlineWadEntry entry,
        CompanionOnlineWadDownloadResult package,
        string managedDataRoot,
        string cacheDirectory,
        CompanionCommunityWadRepairManifest? manifest = null)
    {
        ArgumentNullException.ThrowIfNull(
            entry);

        ArgumentNullException.ThrowIfNull(
            package);

        if (string.IsNullOrWhiteSpace(
                managedDataRoot))
        {
            throw new ArgumentException(
                "A Companion managed data root is required.",
                nameof(managedDataRoot));
        }

        if (string.IsNullOrWhiteSpace(
                cacheDirectory))
        {
            throw new ArgumentException(
                "A community WAD staging directory is required.",
                nameof(cacheDirectory));
        }

        manifest ??=
            LoadDefaultManifest();

        ValidateManifest(
            manifest);

        string packageSha256 =
            ComputeSha256(
                package.SourceFilePath);

        CompanionCommunityWadPackageRepairRule? packageRule =
            manifest.Packages
                .SingleOrDefault(
                    rule =>
                        string.Equals(
                            rule.SourceId,
                            entry.SourceId,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            NormalizeSha256(
                                rule.PackageSha256),
                            packageSha256,
                            StringComparison.Ordinal));

        if (packageRule is null)
        {
            return new CompanionCommunityWadRepairOutcome(
                package,
                0,
                null,
                Array.Empty<string>());
        }

        string preservedOriginal =
            PreserveOriginalPackage(
                entry,
                package,
                managedDataRoot,
                packageRule,
                packageSha256);

        List<CompanionOnlineWadDownloadedItem> outputWads =
            package.Wads.ToList();

        List<string> provenancePaths =
            new();

        int appliedRuleCount =
            0;

        foreach (CompanionCommunityWadItemRepairRule wadRule in
                 packageRule.Wads)
        {
            string normalizedRulePath =
                NormalizeArchivePath(
                    wadRule.ArchivePath);

            int itemIndex =
                outputWads.FindIndex(
                    item =>
                        string.Equals(
                            NormalizeArchivePath(
                                item.ArchivePath),
                            normalizedRulePath,
                            StringComparison.OrdinalIgnoreCase));

            if (itemIndex <
                0)
            {
                throw new InvalidDataException(
                    $"Curated repair rule '{packageRule.RuleId}' expected WAD member '{wadRule.ArchivePath}', but that member was not present in the downloaded package.");
            }

            CompanionOnlineWadDownloadedItem item =
                outputWads[itemIndex];

            string originalWadSha256 =
                ComputeSha256(
                    item.TemporaryPath);

            string expectedWadSha256 =
                NormalizeSha256(
                    wadRule.WadSha256);

            if (!string.Equals(
                    originalWadSha256,
                    expectedWadSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Curated repair rule '{packageRule.RuleId}' matched package SHA-256 but WAD member '{wadRule.ArchivePath}' did not match its expected SHA-256. The source changed and will not be repaired automatically.");
            }

            WadTextureEditorDocument document =
                WadTextureEditorService.Load(
                    item.TemporaryPath);

            List<WadTextureEdit> edits =
                new();

            foreach (CompanionCommunityWadTextureRepairRule textureRule in
                     wadRule.Textures)
            {
                WadTextureEditorTexture[] identityMatches =
                    document.Textures
                        .Where(
                            texture =>
                                string.Equals(
                                    texture.InternalName,
                                    textureRule.InternalName,
                                    StringComparison.OrdinalIgnoreCase) &&
                                texture.Width ==
                                    textureRule.ExpectedWidth &&
                                texture.Height ==
                                    textureRule.ExpectedHeight)
                        .ToArray();

                if (identityMatches.Length !=
                    1)
                {
                    throw new InvalidDataException(
                        $"Curated repair rule '{packageRule.RuleId}' expected exactly one texture '{textureRule.InternalName}' at {textureRule.ExpectedWidth}x{textureRule.ExpectedHeight} in '{wadRule.ArchivePath}', but found {identityMatches.Length:N0}.");
                }

                WadTextureEditorTexture texture =
                    identityMatches[0];

                string newName =
                    texture.InternalName;

                if (textureRule.AddMaskPrefix)
                {
                    if (texture.HasMaskPrefix)
                    {
                        throw new InvalidDataException(
                            $"Curated repair rule '{packageRule.RuleId}' says to add a mask prefix to '{texture.InternalName}', but the texture is already masked.");
                    }

                    newName =
                        "{" +
                        texture.InternalName;
                }

                edits.Add(
                    new WadTextureEdit(
                        texture.DirectoryIndex,
                        newName,
                        textureRule.RemapIndexTo255));
            }

            if (edits.Count ==
                0)
            {
                throw new InvalidDataException(
                    $"Curated repair rule '{packageRule.RuleId}' contains no texture edits for '{wadRule.ArchivePath}'.");
            }

            string repairedDirectory =
                Path.Combine(
                    Path.GetFullPath(
                        cacheDirectory),
                    "Repaired",
                    $"{itemIndex + 1:D4}");

            Directory.CreateDirectory(
                repairedDirectory);

            string repairedPath =
                Path.Combine(
                    repairedDirectory,
                    item.FileName);

            WadTextureEditorService.SaveCopy(
                item.TemporaryPath,
                repairedPath,
                edits);

            WadRegistrationResult inspection =
                WadRegistrationService.Inspect(
                    repairedPath);

            if (!inspection.WadIsValid)
            {
                throw new InvalidDataException(
                    $"Curated repair rule '{packageRule.RuleId}' produced an invalid WAD for '{wadRule.ArchivePath}'. {inspection.Validation}");
            }

            string repairedWadSha256 =
                ComputeSha256(
                    repairedPath);

            string provenancePath =
                repairedPath +
                ProvenanceSuffix;

            WriteProvenance(
                provenancePath,
                entry,
                packageRule,
                wadRule,
                packageSha256,
                originalWadSha256,
                repairedWadSha256);

            provenancePaths.Add(
                provenancePath);

            outputWads[itemIndex] =
                item with
                {
                    TemporaryPath =
                        repairedPath
                };

            appliedRuleCount +=
                edits.Count;
        }

        CompanionOnlineWadDownloadResult repairedPackage =
            package with
            {
                Wads =
                    outputWads
            };

        return new CompanionCommunityWadRepairOutcome(
            repairedPackage,
            appliedRuleCount,
            preservedOriginal,
            provenancePaths);
    }

    public static void ValidateManifest(
        CompanionCommunityWadRepairManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(
            manifest);

        if (manifest.SchemaVersion !=
            CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported community WAD repair manifest schema {manifest.SchemaVersion}. Expected {CurrentSchemaVersion}.");
        }

        manifest.Packages ??=
            new List<CompanionCommunityWadPackageRepairRule>();

        HashSet<string> packageKeys =
            new(
                StringComparer.OrdinalIgnoreCase);

        HashSet<string> ruleIds =
            new(
                StringComparer.OrdinalIgnoreCase);

        foreach (CompanionCommunityWadPackageRepairRule package in
                 manifest.Packages)
        {
            if (string.IsNullOrWhiteSpace(
                    package.RuleId) ||
                !ruleIds.Add(
                    package.RuleId.Trim()))
            {
                throw new InvalidDataException(
                    "Each community WAD repair package must have a unique non-empty ruleId.");
            }

            if (string.IsNullOrWhiteSpace(
                    package.SourceId))
            {
                throw new InvalidDataException(
                    $"Community WAD repair rule '{package.RuleId}' is missing sourceId.");
            }

            string packageSha256 =
                NormalizeSha256(
                    package.PackageSha256);

            string packageKey =
                package.SourceId.Trim() +
                "|" +
                packageSha256;

            if (!packageKeys.Add(
                    packageKey))
            {
                throw new InvalidDataException(
                    $"Duplicate community WAD repair package key '{packageKey}'.");
            }

            package.Wads ??=
                new List<CompanionCommunityWadItemRepairRule>();

            if (package.Wads.Count ==
                0)
            {
                throw new InvalidDataException(
                    $"Community WAD repair rule '{package.RuleId}' contains no WAD members.");
            }

            HashSet<string> archivePaths =
                new(
                    StringComparer.OrdinalIgnoreCase);

            foreach (CompanionCommunityWadItemRepairRule wad in
                     package.Wads)
            {
                string archivePath =
                    NormalizeArchivePath(
                        wad.ArchivePath);

                if (archivePath.Length ==
                    0 ||
                    !archivePaths.Add(
                        archivePath))
                {
                    throw new InvalidDataException(
                        $"Community WAD repair rule '{package.RuleId}' contains a blank or duplicate archivePath.");
                }

                _ =
                    NormalizeSha256(
                        wad.WadSha256);

                wad.Textures ??=
                    new List<CompanionCommunityWadTextureRepairRule>();

                if (wad.Textures.Count ==
                    0)
                {
                    throw new InvalidDataException(
                        $"Community WAD repair rule '{package.RuleId}' contains no texture edits for '{wad.ArchivePath}'.");
                }

                HashSet<string> textureIdentities =
                    new(
                        StringComparer.OrdinalIgnoreCase);

                foreach (CompanionCommunityWadTextureRepairRule texture in
                         wad.Textures)
                {
                    if (string.IsNullOrWhiteSpace(
                            texture.InternalName))
                    {
                        throw new InvalidDataException(
                            $"Community WAD repair rule '{package.RuleId}' contains a texture with no internalName.");
                    }

                    if (texture.ExpectedWidth <=
                            0 ||
                        texture.ExpectedHeight <=
                            0)
                    {
                        throw new InvalidDataException(
                            $"Community WAD repair rule '{package.RuleId}' contains invalid dimensions for '{texture.InternalName}'.");
                    }

                    if (!texture.AddMaskPrefix &&
                        !texture.RemapIndexTo255.HasValue)
                    {
                        throw new InvalidDataException(
                            $"Community WAD repair rule '{package.RuleId}' contains a no-op texture rule for '{texture.InternalName}'.");
                    }

                    if (texture.RemapIndexTo255.HasValue &&
                        (texture.RemapIndexTo255.Value <
                             0 ||
                         texture.RemapIndexTo255.Value >
                             254))
                    {
                        throw new InvalidDataException(
                            $"Community WAD repair rule '{package.RuleId}' has an invalid remap index for '{texture.InternalName}'. Valid source indices are 0-254.");
                    }

                    string identity =
                        texture.InternalName.Trim() +
                        "|" +
                        texture.ExpectedWidth +
                        "x" +
                        texture.ExpectedHeight;

                    if (!textureIdentities.Add(
                            identity))
                    {
                        throw new InvalidDataException(
                            $"Community WAD repair rule '{package.RuleId}' contains duplicate texture identity '{identity}'.");
                    }
                }
            }
        }
    }

    private static string PreserveOriginalPackage(
        CompanionOnlineWadEntry entry,
        CompanionOnlineWadDownloadResult package,
        string managedDataRoot,
        CompanionCommunityWadPackageRepairRule rule,
        string packageSha256)
    {
        string originalDirectory =
            Path.Combine(
                Path.GetFullPath(
                    managedDataRoot),
                "Cache",
                "CommunityWadOriginals",
                packageSha256);

        Directory.CreateDirectory(
            originalDirectory);

        string sourceFileName =
            Path.GetFileName(
                package.SourceFilePath);

        if (string.IsNullOrWhiteSpace(
                sourceFileName))
        {
            sourceFileName =
                "source.bin";
        }

        string preservedPath =
            Path.Combine(
                originalDirectory,
                sourceFileName);

        if (File.Exists(
                preservedPath))
        {
            string existingSha256 =
                ComputeSha256(
                    preservedPath);

            if (!string.Equals(
                    existingSha256,
                    packageSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The hidden community WAD provenance cache contains a conflicting file for package SHA-256 {packageSha256}.");
            }
        }
        else
        {
            File.Copy(
                package.SourceFilePath,
                preservedPath,
                overwrite:
                    false);
        }

        string sourceMetadataPath =
            Path.Combine(
                originalDirectory,
                "source.json");

        var sourceMetadata =
            new
            {
                schemaVersion = 1,
                ruleId =
                    rule.RuleId,
                sourceId =
                    entry.SourceId,
                sourceDisplayName =
                    entry.SourceDisplayName,
                sourcePageUri =
                    entry.SourcePageUri.AbsoluteUri,
                downloadUri =
                    entry.DownloadUri.AbsoluteUri,
                packageSha256,
                originalFileName =
                    sourceFileName
            };

        File.WriteAllText(
            sourceMetadataPath,
            JsonSerializer.Serialize(
                sourceMetadata,
                JsonOptions));

        return preservedPath;
    }

    private static void WriteProvenance(
        string provenancePath,
        CompanionOnlineWadEntry entry,
        CompanionCommunityWadPackageRepairRule packageRule,
        CompanionCommunityWadItemRepairRule wadRule,
        string packageSha256,
        string originalWadSha256,
        string repairedWadSha256)
    {
        var provenance =
            new
            {
                schemaVersion = 1,
                ruleId =
                    packageRule.RuleId,
                sourceId =
                    entry.SourceId,
                sourceDisplayName =
                    entry.SourceDisplayName,
                sourcePageUri =
                    entry.SourcePageUri.AbsoluteUri,
                downloadUri =
                    entry.DownloadUri.AbsoluteUri,
                packageSha256,
                archivePath =
                    NormalizeArchivePath(
                        wadRule.ArchivePath),
                originalWadSha256,
                repairedWadSha256,
                textureRepairs =
                    wadRule.Textures.Select(
                        texture =>
                            new
                            {
                                internalName =
                                    texture.InternalName,
                                expectedWidth =
                                    texture.ExpectedWidth,
                                expectedHeight =
                                    texture.ExpectedHeight,
                                addMaskPrefix =
                                    texture.AddMaskPrefix,
                                remapIndexTo255 =
                                    texture.RemapIndexTo255
                            })
            };

        File.WriteAllText(
            provenancePath,
            JsonSerializer.Serialize(
                provenance,
                JsonOptions));
    }

    private static string NormalizeArchivePath(
        string value)
    {
        return (value ??
                string.Empty)
            .Replace(
                '\\',
                '/')
            .Trim()
            .TrimStart(
                '/');
    }

    private static string NormalizeSha256(
        string value)
    {
        string normalized =
            (value ??
             string.Empty)
                .Trim()
                .ToUpperInvariant();

        if (normalized.Length !=
                64 ||
            normalized.Any(
                character =>
                    !Uri.IsHexDigit(
                        character)))
        {
            throw new InvalidDataException(
                $"'{value}' is not a valid SHA-256 value.");
        }

        return normalized;
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