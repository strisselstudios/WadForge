using System.Security.Cryptography;
using WadForge.Aliases;
using WadForge.Core;
using WadForge.Imaging;

namespace WadForge.Wad;

public sealed class ImageToWadConversionService
{
    public WadConversionResult Convert(
        IReadOnlyList<WadTextureInput> inputs,
        WadConversionOptions options,
        IProgress<WadConversionProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(options);

        if (inputs.Count == 0)
        {
            throw new InvalidOperationException(
                "No input images were supplied.");
        }

        Rgb24[]? wad2Palette = null;

        if (options.Format == WadFormat.Wad2)
        {
            if (string.IsNullOrWhiteSpace(
                    options.Wad2PalettePath))
            {
                throw new InvalidOperationException(
                    "A WAD2 palette file is required.");
            }

            wad2Palette = PaletteFile.Load(
                options.Wad2PalettePath);
        }

        List<WadTextureData> textures = new(
            inputs.Count);

        List<TextureAliasEntry> aliasEntries = new(
            inputs.Count);

        HashSet<string> reservedNames = new(
            StringComparer.OrdinalIgnoreCase);

        for (int inputIndex = 0;
             inputIndex < inputs.Count;
             inputIndex++)
        {
            WadTextureInput input =
                inputs[inputIndex];

            progress?.Report(
                new WadConversionProgress(
                    inputIndex,
                    inputs.Count,
                    input.DisplayName));

            RgbaImage sourceImage =
                ImagePixelReader.Load(
                    input.SourcePath);

            RgbaImage normalizedImage =
                WadImageNormalizer.Normalize(
                    sourceImage);

            bool useTransparency =
                options.PreserveTransparency &&
                sourceImage.HasTransparency;

            string internalName =
                TextureAliasNameGenerator.CreateUnique(
                    input.DisplayName,
                    reservedNames,
                    useTransparency);

            Rgb24[] palette =
                options.Format == WadFormat.Wad3
                    ? AdaptivePaletteQuantizer.CreatePalette(
                        normalizedImage,
                        useTransparency)
                    : (Rgb24[])wad2Palette!.Clone();

            IReadOnlyList<RgbaImage> mipImages =
                MipMapGenerator.CreateFourLevels(
                    normalizedImage);

            List<byte[]> indexedMipLevels = new(
                mipImages.Count);

            foreach (RgbaImage mipImage in mipImages)
            {
                indexedMipLevels.Add(
                    IndexedImageMapper.Map(
                        mipImage,
                        palette,
                        useTransparency,
                        options.EnableDithering));
            }

            textures.Add(
                new WadTextureData(
                    internalName,
                    normalizedImage.Width,
                    normalizedImage.Height,
                    indexedMipLevels,
                    palette));

            aliasEntries.Add(
                new TextureAliasEntry(
                    input.DisplayName,
                    internalName,
                    Path.GetFileName(
                        input.SourcePath),
                    sourceImage.Width,
                    sourceImage.Height,
                    normalizedImage.Width,
                    normalizedImage.Height));
        }

        progress?.Report(
            new WadConversionProgress(
                inputs.Count,
                inputs.Count,
                "Writing WAD archive"));

        WadArchiveWriter.Write(
            options.OutputPath,
            options.Format,
            textures);

        if (!WadArchiveInspector.TryInspect(
                options.OutputPath,
                out WadInspectionResult? inspection,
                out string inspectionError) ||
            inspection is null)
        {
            throw new InvalidDataException(
                "The generated WAD failed validation: " +
                inspectionError);
        }

        if (inspection.Format != options.Format)
        {
            throw new InvalidDataException(
                "The generated WAD format does not match " +
                "the requested output format.");
        }

        if (inspection.LumpCount != inputs.Count)
        {
            throw new InvalidDataException(
                "The generated WAD texture count does not " +
                "match the input count.");
        }

        string? paletteOutputPath = null;
        string? paletteFileName = null;

        string wadHash;

        using (
            FileStream wadStream = File.OpenRead(
                options.OutputPath))
        {
            wadHash = System.Convert.ToHexString(
                SHA256.HashData(wadStream));
        }

        string manifestPath =
            options.OutputPath +
            ".wadforge.json";

        WadAliasManifest manifest = new(
            2,
            Path.GetFileName(
                options.OutputPath),
            wadHash,
            options.Format == WadFormat.Wad2
                ? "WAD2"
                : "WAD3",
            paletteFileName,
            aliasEntries);

        WadAliasManifestSerializer.Write(
            manifestPath,
            manifest);

        return new WadConversionResult(
            options.OutputPath,
            manifestPath,
            paletteOutputPath,
            inputs.Count,
            wadHash);
    }
}
