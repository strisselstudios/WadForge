using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TrenchBroom.Companion.Core;

namespace TrenchBroom.Companion.App;

public partial class CompanionWadBrowserWindow
{
    private bool _robustCatalogApplied;

    protected override async void OnContentRendered(
        EventArgs e)
    {
        base.OnContentRendered(
            e);

        if (_robustCatalogApplied)
        {
            return;
        }

        _robustCatalogApplied =
            true;

        int version =
            ++_catalogVersion;

        TextureCountText.Text =
            "Reading texture index...";

        GridStatusText.Text =
            "Reading brush miptextures and skipping malformed entries individually...";

        try
        {
            CompanionRobustWadTextureCatalog catalog =
                await Task.Run(
                    () =>
                        CompanionRobustWadTextureCatalogService.ReadCatalog(
                            _wad.WadPath));

            if (version !=
                _catalogVersion)
            {
                return;
            }

            _catalog =
                catalog.Textures;

            ApplyTextureFilter();

            WadSummaryText.Text =
                $"{_wad.WadFormat} - {_catalog.Count:N0} brush textures - {_wad.AliasCountText} aliases";

            string metadata =
                $"{_wad.WadFormat} archive" +
                Environment.NewLine +
                $"Archive entries: {_wad.TextureCountText}" +
                Environment.NewLine +
                $"Previewable brush textures: {_catalog.Count:N0}" +
                Environment.NewLine +
                $"Aliases: {_wad.AliasCountText}" +
                Environment.NewLine +
                $"Status: {_wad.Validation}";

            if (catalog.SkippedMipTextureCount >
                0)
            {
                metadata +=
                    Environment.NewLine +
                    $"Skipped malformed/unsupported miptextures: {catalog.SkippedMipTextureCount:N0}";
            }

            WadMetadataText.Text =
                metadata;

            if (_catalog.Count ==
                0)
            {
                int nonMipEntries =
                    Math.Max(
                        0,
                        catalog.TotalEntryCount -
                        catalog.MipTextureCandidateCount);

                GridStatusText.Text =
                    nonMipEntries >
                    0
                        ? $"This is a valid {catalog.WadFormat} archive with {catalog.TotalEntryCount:N0} entries, but it contains no standard brush miptextures. The remaining entries may be Quake pictures, palettes, sounds, sprites, labels, or other non-brush WAD lumps."
                        : $"This is a valid {catalog.WadFormat} archive, but no previewable brush miptextures were found.";

                TextureCountText.Text =
                    "0 brush textures";

                return;
            }

            if (catalog.SkippedMipTextureCount >
                0)
            {
                string firstWarning =
                    catalog.Warnings.FirstOrDefault() ??
                    "One or more malformed texture entries were skipped.";

                GridStatusText.Text =
                    $"{_catalog.Count:N0} brush textures are previewable. Companion skipped {catalog.SkippedMipTextureCount:N0} malformed or unsupported miptexture entr{(catalog.SkippedMipTextureCount == 1 ? "y" : "ies")} instead of rejecting the entire WAD. First issue: {firstWarning}";
            }
        }
        catch (Exception exception)
        {
            if (version !=
                _catalogVersion)
            {
                return;
            }

            GridStatusText.Text =
                exception.Message;

            TextureCountText.Text =
                "Texture index unavailable";
        }
    }
}
