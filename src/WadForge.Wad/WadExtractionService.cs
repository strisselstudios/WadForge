using System.Security.Cryptography;
using System.Text;
using WadForge.Aliases;
using WadForge.Core;
using WadForge.Imaging;

namespace WadForge.Wad;

public sealed class WadExtractionService
{
    private const int MaximumPngBaseNameLength = 220;

    public WadExtractionResult Extract(
        IReadOnlyList<WadExtractionInput> inputs,
        WadExtractionOptions options,
        IProgress<WadExtractionProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(options);

        if (inputs.Count == 0)
        {
            throw new InvalidOperationException(
                "No WAD archives were supplied.");
        }

        if (string.IsNullOrWhiteSpace(
                options.OutputDirectory))
        {
            throw new InvalidOperationException(
                "An output directory is required.");
        }

        Directory.CreateDirectory(
            options.OutputDirectory);

        Rgb24[]? wad2Palette = null;

        if (!string.IsNullOrWhiteSpace(
                options.Wad2PalettePath))
        {
            wad2Palette = PaletteFile.Load(
                options.Wad2PalettePath);
        }

        List<string> outputDirectories = new();
        List<string> warnings = new();

        int totalTextureCount = 0;
        int restoredAliasCount = 0;

        for (int wadIndex = 0;
             wadIndex < inputs.Count;
             wadIndex++)
        {
            WadExtractionInput input =
                inputs[wadIndex];

            progress?.Report(
                new WadExtractionProgress(
                    wadIndex,
                    inputs.Count,
                    Path.GetFileName(input.WadPath)));

            WadArchiveReadResult archive =
                WadArchiveReader.Read(
                    input.WadPath,
                    wad2Palette,
                    options.PreserveTransparency);

            AliasResolution aliasResolution =
                options.RestoreAliases
                    ? ResolveAliases(input.WadPath)
                    : AliasResolution.Empty;

            if (!string.IsNullOrWhiteSpace(
                    aliasResolution.Warning))
            {
                warnings.Add(
                    $"{Path.GetFileName(input.WadPath)}: " +
                    aliasResolution.Warning);
            }

            string requestedDirectory =
                Path.Combine(
                    options.OutputDirectory,
                    Path.GetFileNameWithoutExtension(
                        input.WadPath) +
                    "-png");

            string outputDirectory =
                CreateAvailableDirectory(
                    requestedDirectory);

            Directory.CreateDirectory(
                outputDirectory);

            foreach (WadExtractedTexture texture
                     in archive.Textures)
            {
                TextureAliasEntry? alias = null;

                if (aliasResolution.Aliases.TryGetValue(
                        texture.InternalName,
                        out TextureAliasEntry? resolvedAlias))
                {
                    alias = resolvedAlias;
                    restoredAliasCount++;
                }

                string displayName =
                    alias?.DisplayName ??
                    texture.InternalName;

                RgbaImage outputImage =
                    RestoreOriginalDimensions(
                        texture.Image,
                        alias);

                string baseFileName =
                    SanitizeFileName(
                        displayName);

                string outputPath =
                    CreateAvailableFilePath(
                        outputDirectory,
                        baseFileName,
                        ".png");

                PngImageWriter.Write(
                    outputPath,
                    outputImage);

                totalTextureCount++;
            }

            WriteExtractionSummary(
                outputDirectory,
                input.WadPath,
                archive.Format,
                archive.Textures.Count,
                aliasResolution.Aliases.Count > 0);

            outputDirectories.Add(
                outputDirectory);

            progress?.Report(
                new WadExtractionProgress(
                    wadIndex + 1,
                    inputs.Count,
                    Path.GetFileName(input.WadPath)));
        }

        return new WadExtractionResult(
            inputs.Count,
            totalTextureCount,
            restoredAliasCount,
            outputDirectories,
            warnings);
    }

    private static RgbaImage RestoreOriginalDimensions(
        RgbaImage image,
        TextureAliasEntry? alias)
    {
        if (alias?.OriginalWidth is not int originalWidth ||
            alias.OriginalHeight is not int originalHeight)
        {
            return image;
        }

        if (originalWidth <= 0 ||
            originalHeight <= 0 ||
            originalWidth > image.Width ||
            originalHeight > image.Height)
        {
            return image;
        }

        return RgbaImageCropper.CropTopLeft(
            image,
            originalWidth,
            originalHeight);
    }

    private static AliasResolution ResolveAliases(
        string wadPath)
    {
        string manifestPath =
            wadPath + ".wadforge.json";

        if (!File.Exists(manifestPath))
        {
            return AliasResolution.Empty;
        }

        try
        {
            WadAliasManifest manifest =
                WadAliasManifestSerializer.Read(
                    manifestPath);

            string actualHash;

            using (
                FileStream stream =
                    File.OpenRead(wadPath))
            {
                actualHash =
                    System.Convert.ToHexString(
                        SHA256.HashData(stream));
            }

            if (!string.Equals(
                    actualHash,
                    manifest.WadSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new AliasResolution(
                    new Dictionary<string, TextureAliasEntry>(
                        StringComparer.OrdinalIgnoreCase),
                    "The alias manifest hash does not match the WAD. Aliases were ignored.");
            }

            Dictionary<string, TextureAliasEntry> aliases =
                new(StringComparer.OrdinalIgnoreCase);

            foreach (TextureAliasEntry entry
                     in manifest.Textures)
            {
                if (string.IsNullOrWhiteSpace(
                        entry.InternalName))
                {
                    continue;
                }

                aliases[entry.InternalName] =
                    entry;
            }

            return new AliasResolution(
                aliases,
                null);
        }
        catch (Exception exception)
        {
            return new AliasResolution(
                new Dictionary<string, TextureAliasEntry>(
                    StringComparer.OrdinalIgnoreCase),
                "The alias manifest could not be read: " +
                exception.Message);
        }
    }

    private static string CreateAvailableDirectory(
        string requestedPath)
    {
        if (!Directory.Exists(requestedPath) &&
            !File.Exists(requestedPath))
        {
            return requestedPath;
        }

        for (int suffix = 2;
             suffix < int.MaxValue;
             suffix++)
        {
            string candidate =
                requestedPath + "-" + suffix;

            if (!Directory.Exists(candidate) &&
                !File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException(
            "No available extraction directory could be generated.");
    }

    private static string CreateAvailableFilePath(
        string directory,
        string baseName,
        string extension)
    {
        string requestedPath =
            Path.Combine(
                directory,
                baseName + extension);

        if (!File.Exists(requestedPath))
        {
            return requestedPath;
        }

        for (int suffix = 2;
             suffix < int.MaxValue;
             suffix++)
        {
            string candidate =
                Path.Combine(
                    directory,
                    $"{baseName}-{suffix}{extension}");

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException(
            "No available PNG filename could be generated.");
    }

    private static string SanitizeFileName(
        string displayName)
    {
        string name =
            displayName.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "texture";
        }

        foreach (char invalidCharacter
                 in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(
                invalidCharacter,
                '_');
        }

        name = name.TrimEnd(
            '.',
            ' ');

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "texture";
        }

        if (name.Length <=
            MaximumPngBaseNameLength)
        {
            return name;
        }

        string hash = System.Convert
            .ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(name)))
            [..8];

        int prefixLength =
            MaximumPngBaseNameLength -
            hash.Length -
            1;

        return name[..prefixLength] +
            "_" +
            hash;
    }

    private static void WriteExtractionSummary(
        string outputDirectory,
        string wadPath,
        WadFormat format,
        int textureCount,
        bool aliasesRestored)
    {
        string path = Path.Combine(
            outputDirectory,
            "wadforge-extraction.txt");

        string content =
            "WadForge extraction" +
            Environment.NewLine +
            Environment.NewLine +
            "Source WAD: " +
            wadPath +
            Environment.NewLine +
            "Format: " +
            (format == WadFormat.Wad2
                ? "WAD2"
                : "WAD3") +
            Environment.NewLine +
            "Textures: " +
            textureCount +
            Environment.NewLine +
            "Long names restored: " +
            aliasesRestored;

        File.WriteAllText(
            path,
            content);
    }

    private sealed record AliasResolution(
        IReadOnlyDictionary<string, TextureAliasEntry> Aliases,
        string? Warning)
    {
        public static AliasResolution Empty { get; } =
            new(
                new Dictionary<string, TextureAliasEntry>(
                    StringComparer.OrdinalIgnoreCase),
                null);
    }
}
