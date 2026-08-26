using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TrenchBroom.Companion.Core;

namespace TrenchBroom.Companion.App;

public partial class CompanionWadBrowserWindow : Window
{
    private const double MinimumThumbnailTileWidth = 90;
    private const double MaximumThumbnailTileWidth = 180;
    private const double DefaultThumbnailTileWidth = 115;
    private const double TextureTileHorizontalSpacing = 10;
    private const int ThumbnailConcurrency = 4;

    private readonly WadRegistrationResult _wad;
    private readonly string _managedDataRoot;
    private readonly CompanionPaletteLibraryService _paletteLibraryService =
        new();
    private CompanionPaletteResolution _automaticPaletteResolution;
    private readonly ObservableCollection<TextureRowViewModel> _visibleRows =
        new();
    private readonly SemaphoreSlim _thumbnailSemaphore =
        new(
            ThumbnailConcurrency,
            ThumbnailConcurrency);

    private IReadOnlyList<CompanionWadTextureEntry> _catalog =
        Array.Empty<CompanionWadTextureEntry>();

    private IReadOnlyDictionary<string, string> _aliasDisplayNames =
        new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

    private string? _selectedPalettePath;
    private string _selectedPaletteDescription;
    private double _thumbnailTileWidth =
        DefaultThumbnailTileWidth;
    private int _lastTilesPerRow;
    private IReadOnlyList<TextureTileViewModel> _filteredTiles =
        Array.Empty<TextureTileViewModel>();
    private int _catalogVersion;
    private int _thumbnailVersion;
    private int _previewVersion;

    public CompanionWadBrowserWindow(
        WadRegistrationResult wad,
        string managedDataRoot,
        CompanionPaletteResolution paletteResolution)
    {
        InitializeComponent();

        _wad =
            wad ??
            throw new ArgumentNullException(
                nameof(wad));

        _managedDataRoot =
            string.IsNullOrWhiteSpace(
                managedDataRoot)
                ? throw new ArgumentException(
                    "A Companion managed data root is required.",
                    nameof(managedDataRoot))
                : Path.GetFullPath(
                    managedDataRoot);

        _automaticPaletteResolution =
            paletteResolution ??
            throw new ArgumentNullException(
                nameof(paletteResolution));

        _selectedPalettePath =
            _automaticPaletteResolution.PalettePath;

        _selectedPaletteDescription =
            _automaticPaletteResolution.Description;

        TextureRowsListBox.ItemsSource =
            _visibleRows;

        WadNameText.Text =
            _wad.WadFileName;

        WadSummaryText.Text =
            $"{_wad.WadFormat} - {_wad.TextureCountText} textures - {_wad.AliasCountText} aliases";

        WadMetadataText.Text =
            $"{_wad.WadFormat} archive" +
            Environment.NewLine +
            $"Textures: {_wad.TextureCountText}" +
            Environment.NewLine +
            $"Aliases: {_wad.AliasCountText}" +
            Environment.NewLine +
            $"Status: {_wad.Validation}";

        WadPathText.Text =
            _wad.WadPath;

        bool isWad2 =
            string.Equals(
                _wad.WadFormat,
                "WAD2",
                StringComparison.OrdinalIgnoreCase);

        Wad2PalettePanel.Visibility =
            isWad2
                ? Visibility.Visible
                : Visibility.Collapsed;

        RefreshPaletteSourceText();
        RefreshPaletteChoices();
    }

    private async void Window_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        LoadVerifiedAliases();

        await LoadCatalogAsync();
    }

    private void LoadVerifiedAliases()
    {
        if (!_wad.ManifestExists ||
            !_wad.ManifestIsValid ||
            string.IsNullOrWhiteSpace(
                _wad.ManifestPath))
        {
            _aliasDisplayNames =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            return;
        }

        try
        {
            _aliasDisplayNames =
                CompanionWadTexturePreviewService
                    .ReadAliasDisplayNames(
                        _wad.ManifestPath);
        }
        catch
        {
            _aliasDisplayNames =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task LoadCatalogAsync()
    {
        int version =
            ++_catalogVersion;

        TextureCountText.Text =
            "Reading texture index...";

        GridStatusText.Text =
            "Reading all texture names and dimensions...";

        try
        {
            CompanionWadTextureCatalog catalog =
                await Task.Run(
                    () =>
                        CompanionWadTexturePreviewService
                            .ReadCatalog(
                                _wad.WadPath));

            if (version !=
                _catalogVersion)
            {
                return;
            }

            _catalog =
                catalog.Textures;

            ApplyTextureFilter();
        }
        catch (Exception exception)
        {
            if (version !=
                _catalogVersion)
            {
                return;
            }

            _catalog =
                Array.Empty<CompanionWadTextureEntry>();

            _visibleRows.Clear();

            TextureCountText.Text =
                "Texture index unavailable";

            GridStatusText.Text =
                exception.Message;
        }
    }

    private void TextureSearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        ApplyTextureFilter();
    }

    private void ApplyTextureFilter()
    {
        string search =
            TextureSearchTextBox.Text
                .Trim();

        ++_thumbnailVersion;

        List<TextureTileViewModel> tiles =
            new();

        foreach (CompanionWadTextureEntry texture in
                 _catalog)
        {
            string displayName =
                ResolveDisplayName(
                    texture.Name);

            if (search.Length >
                    0 &&
                !displayName.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) &&
                !texture.Name.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TextureTileViewModel tile =
                new(
                    texture,
                    displayName);

            tile.UpdateScale(
                _thumbnailTileWidth);

            tiles.Add(
                tile);
        }

        _filteredTiles =
            tiles;

        RebuildTextureRows(
            force: true);

        TextureCountText.Text =
            tiles.Count ==
            _catalog.Count
                ? $"{tiles.Count:N0} textures"
                : $"{tiles.Count:N0} of {_catalog.Count:N0} textures";

        if (tiles.Count ==
            0)
        {
            GridStatusText.Text =
                _catalog.Count ==
                    0
                    ? "This WAD does not contain supported miptextures."
                    : "No textures match your search.";
        }
        else
        {
            GridStatusText.Text =
                $"All {tiles.Count:N0} matching textures are available. Thumbnails render as they come into view.";
        }
    }

    private void ThumbnailScaleSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        double value =
            Math.Clamp(
                e.NewValue,
                MinimumThumbnailTileWidth,
                MaximumThumbnailTileWidth);

        _thumbnailTileWidth =
            value;

        if (ThumbnailScaleValueText is not
            null)
        {
            ThumbnailScaleValueText.Text =
                $"{value:N0} px";
        }

        foreach (TextureTileViewModel tile in
                 _filteredTiles)
        {
            tile.UpdateScale(
                value);
        }

        RebuildTextureRows();
    }

    private void TextureRowsListBox_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        RebuildTextureRows();
    }

    private void RebuildTextureRows(
        bool force =
            false)
    {
        if (TextureRowsListBox is null)
        {
            return;
        }

        int tilesPerRow =
            CalculateTilesPerRow();

        if (!force &&
            tilesPerRow ==
                _lastTilesPerRow &&
            _visibleRows.Count >
                0)
        {
            return;
        }

        _lastTilesPerRow =
            tilesPerRow;

        _visibleRows.Clear();

        for (int index = 0;
             index < _filteredTiles.Count;
             index += tilesPerRow)
        {
            int count =
                Math.Min(
                    tilesPerRow,
                    _filteredTiles.Count -
                    index);

            TextureTileViewModel[] rowTiles =
                _filteredTiles
                    .Skip(
                        index)
                    .Take(
                        count)
                    .ToArray();

            _visibleRows.Add(
                new TextureRowViewModel(
                    rowTiles));
        }
    }

    private int CalculateTilesPerRow()
    {
        double availableWidth =
            Math.Max(
                1,
                TextureRowsListBox.ActualWidth -
                28);

        double occupiedWidth =
            _thumbnailTileWidth +
            TextureTileHorizontalSpacing;

        return Math.Max(
            1,
            (int)Math.Floor(
                availableWidth /
                occupiedWidth));
    }
    private string ResolveDisplayName(
        string internalName)
    {
        if (_aliasDisplayNames.TryGetValue(
                internalName,
                out string? displayName) &&
            !string.IsNullOrWhiteSpace(
                displayName))
        {
            return displayName;
        }

        return internalName;
    }

    private async void TextureTile_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not
                Button button ||
            button.Tag is not
                TextureTileViewModel tile ||
            tile.Thumbnail is not
                null ||
            tile.IsLoading ||
            tile.LoadAttempted)
        {
            return;
        }

        int version =
            _thumbnailVersion;

        await LoadThumbnailAsync(
            tile,
            version);
    }

    private async Task LoadThumbnailAsync(
        TextureTileViewModel tile,
        int version)
    {
        tile.IsLoading =
            true;

        tile.LoadAttempted =
            true;

        tile.LoadingText =
            "Loading...";

        await _thumbnailSemaphore.WaitAsync();

        try
        {
            if (version !=
                _thumbnailVersion)
            {
                return;
            }

            CompanionWadTexturePreview preview =
                await Task.Run(
                    () =>
                        CompanionWadTexturePreviewService
                            .ReadPreview(
                                _wad.WadPath,
                                tile.Texture.DirectoryIndex,
                                GetSelectedPalettePath()));

            if (version !=
                _thumbnailVersion)
            {
                return;
            }

            tile.Thumbnail =
                CreateBitmap(
                    preview);

            tile.LoadingText =
                string.Empty;
        }
        catch (Exception exception)
        {
            if (version !=
                _thumbnailVersion)
            {
                return;
            }

            tile.LoadingText =
                "Preview unavailable";

            tile.Error =
                exception.Message;
        }
        finally
        {
            tile.IsLoading =
                false;

            _thumbnailSemaphore.Release();
        }
    }

    private async void TextureTile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not
                Button button ||
            button.Tag is not
                TextureTileViewModel tile)
        {
            return;
        }

        await LoadLargePreviewAsync(
            tile);
    }

    private async Task LoadLargePreviewAsync(
        TextureTileViewModel tile)
    {
        int version =
            ++_previewVersion;

        PreviewImage.Source =
            null;

        PreviewPlaceholderText.Text =
            "Loading preview...";

        PreviewPlaceholderText.Visibility =
            Visibility.Visible;

        PreviewNameText.Text =
            tile.DisplayName;

        PreviewStoredNameText.Text =
            tile.HasAlias
                ? $"Stored WAD name: {tile.Texture.Name}"
                : string.Empty;

        PreviewDetailText.Text =
            tile.Texture.DimensionsText;

        try
        {
            CompanionWadTexturePreview preview =
                await Task.Run(
                    () =>
                        CompanionWadTexturePreviewService
                            .ReadPreview(
                                _wad.WadPath,
                                tile.Texture.DirectoryIndex,
                                GetSelectedPalettePath()));

            if (version !=
                _previewVersion)
            {
                return;
            }

            PreviewImage.Source =
                CreateBitmap(
                    preview);

            PreviewPlaceholderText.Visibility =
                Visibility.Collapsed;

            PreviewDetailText.Text =
                $"{preview.Width:N0} x {preview.Height:N0}" +
                Environment.NewLine +
                $"Palette: {preview.PaletteDescription}" +
                (preview.HasTransparency
                    ? Environment.NewLine + "Transparency: yes"
                    : string.Empty);
        }
        catch (Exception exception)
        {
            if (version !=
                _previewVersion)
            {
                return;
            }

            PreviewImage.Source =
                null;

            PreviewPlaceholderText.Text =
                exception.Message;

            PreviewPlaceholderText.Visibility =
                Visibility.Visible;
        }
    }

    private static BitmapSource CreateBitmap(
        CompanionWadTexturePreview preview)
    {
        BitmapSource bitmap =
            BitmapSource.Create(
                preview.Width,
                preview.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                preview.BgraPixels,
                checked(
                    preview.Width *
                    4));

        bitmap.Freeze();

        return bitmap;
    }

    private string? GetSelectedPalettePath()
    {
        if (!string.Equals(
                _wad.WadFormat,
                "WAD2",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return _selectedPalettePath;
    }

    private void RefreshPaletteChoices(
        string? selectAssetId =
            null)
    {
        IReadOnlyList<CompanionPaletteLibraryAsset> assets =
            _paletteLibraryService.GetAssets(
                _managedDataRoot);

        List<PaletteChoice> choices =
            new()
            {
                new PaletteChoice(
                    "Auto - " +
                    _automaticPaletteResolution.Description,
                    _automaticPaletteResolution.PaletteAssetId,
                    _automaticPaletteResolution.PalettePath,
                    true)
            };

        choices.AddRange(
            assets.Select(
                asset =>
                    new PaletteChoice(
                        asset.Sources.Count ==
                            0
                            ? asset.DisplayName
                            : asset.DisplayName +
                              " - " +
                              string.Join(
                                  ", ",
                                  asset.Sources),
                        asset.AssetId,
                        asset.PalettePath,
                        false)));

        PaletteComboBox.ItemsSource =
            choices;

        PaletteChoice? selected =
            null;

        if (!string.IsNullOrWhiteSpace(
                selectAssetId))
        {
            selected =
                choices.FirstOrDefault(
                    choice =>
                        !choice.IsAutomatic &&
                        string.Equals(
                            choice.AssetId,
                            selectAssetId,
                            StringComparison.OrdinalIgnoreCase));
        }

        selected ??=
            choices[0];

        PaletteComboBox.SelectedItem =
            selected;

        PaletteLibraryPathText.Text =
            "Library: " +
            _paletteLibraryService.GetLibraryDirectory(
                _managedDataRoot);

        CompanionPaletteLibraryAsset? remembered =
            _paletteLibraryService.GetAssociatedAsset(
                _managedDataRoot,
                _automaticPaletteResolution.WadSha256);

        ForgetPaletteButton.IsEnabled =
            remembered is not null;

        PaletteAssociationStatusText.Text =
            remembered is null
                ? "No palette is permanently assigned to this WAD."
                : $"Remembered for this WAD: {remembered.DisplayName}";
    }

    private void PaletteComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (PaletteComboBox.SelectedItem is not
            PaletteChoice choice)
        {
            return;
        }

        _selectedPalettePath =
            choice.PalettePath;

        _selectedPaletteDescription =
            choice.DisplayText;

        RememberPaletteButton.IsEnabled =
            !string.IsNullOrWhiteSpace(
                choice.AssetId);

        RefreshPaletteSourceText();
        ReloadVisiblePreviews();
    }

    private void ImportPalette_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenFileDialog dialog =
            new()
            {
                Title =
                    "Import a WAD2 palette into Companion",
                Filter =
                    "Palette files|*.lmp;*.pal|" +
                    "All files|*.*",
                Multiselect =
                    false,
                CheckFileExists =
                    true,
                CheckPathExists =
                    true
            };

        if (dialog.ShowDialog(
                this) !=
            true)
        {
            return;
        }

        try
        {
            CompanionPaletteLibraryAsset imported =
                _paletteLibraryService.ImportFile(
                    _managedDataRoot,
                    dialog.FileName,
                    Path.GetFileNameWithoutExtension(
                        dialog.FileName),
                    "User imported");

            RefreshPaletteChoices(
                imported.AssetId);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Invalid Palette",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RememberPalette_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (PaletteComboBox.SelectedItem is not
                PaletteChoice choice ||
            string.IsNullOrWhiteSpace(
                choice.AssetId))
        {
            return;
        }

        try
        {
            _paletteLibraryService.SetWadAssociation(
                _managedDataRoot,
                _automaticPaletteResolution.WadSha256,
                choice.AssetId);

            CompanionPaletteLibraryAsset? remembered =
                _paletteLibraryService.GetAssociatedAsset(
                    _managedDataRoot,
                    _automaticPaletteResolution.WadSha256);

            if (remembered is not null)
            {
                _automaticPaletteResolution =
                    new CompanionPaletteResolution(
                        _automaticPaletteResolution.WadSha256,
                        remembered.AssetId,
                        remembered.PalettePath,
                        $"Remembered for this WAD: {remembered.DisplayName}",
                        true);
            }

            RefreshPaletteChoices(
                choice.AssetId);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Palette Association",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ForgetPalette_Click(
        object sender,
        RoutedEventArgs e)
    {
        _paletteLibraryService.ClearWadAssociation(
            _managedDataRoot,
            _automaticPaletteResolution.WadSha256);

        _automaticPaletteResolution =
            new CompanionPaletteResolution(
                _automaticPaletteResolution.WadSha256,
                null,
                null,
                "No remembered palette - choose a library palette or use automatic discovery next time",
                false);

        RefreshPaletteChoices();
    }

    private void RefreshPaletteSourceText()
    {
        if (!string.Equals(
                _wad.WadFormat,
                "WAD2",
                StringComparison.OrdinalIgnoreCase))
        {
            PaletteSourceText.Text =
                "Embedded WAD3 palette";

            return;
        }

        PaletteSourceText.Text =
            "Current: " +
            _selectedPaletteDescription;
    }

    private void ReloadVisiblePreviews()
    {
        ++_thumbnailVersion;
        ++_previewVersion;

        PreviewImage.Source =
            null;

        PreviewPlaceholderText.Text =
            "Click a texture tile to preview it.";

        PreviewPlaceholderText.Visibility =
            Visibility.Visible;

        PreviewNameText.Text =
            "Select a texture";

        PreviewStoredNameText.Text =
            string.Empty;

        PreviewDetailText.Text =
            "Dimensions and palette source appear here.";

        foreach (TextureRowViewModel row in
                 _visibleRows)
        {
            foreach (TextureTileViewModel tile in
                     row.Tiles)
            {
                tile.Thumbnail =
                    null;

                tile.LoadAttempted =
                    false;

                tile.IsLoading =
                    false;

                tile.LoadingText =
                    "Scroll into view to load";

                tile.Error =
                    null;
            }
        }

        TextureRowsListBox.Items.Refresh();
    }


    private sealed record PaletteChoice(
        string DisplayText,
        string? AssetId,
        string? PalettePath,
        bool IsAutomatic);

    private sealed class TextureRowViewModel
    {
        public TextureRowViewModel(
            IReadOnlyList<TextureTileViewModel> tiles)
        {
            Tiles =
                tiles;
        }

        public IReadOnlyList<TextureTileViewModel> Tiles { get; }
    }

    private sealed class TextureTileViewModel :
        INotifyPropertyChanged
    {
        private ImageSource? _thumbnail;
        private string _loadingText =
            "Scroll into view to load";

        public TextureTileViewModel(
            CompanionWadTextureEntry texture,
            string displayName)
        {
            Texture =
                texture;

            DisplayName =
                displayName;

            HasAlias =
                !string.Equals(
                    displayName,
                    texture.Name,
                    StringComparison.Ordinal);

            StoredNameLine =
                HasAlias
                    ? $"WAD: {texture.Name}"
                    : string.Empty;
        }

        public CompanionWadTextureEntry Texture { get; }

        public string DisplayName { get; }

        public bool HasAlias { get; }

        public string StoredNameLine { get; }

        public double TileWidth { get; private set; } =
            DefaultThumbnailTileWidth;

        public double ThumbnailHeight { get; private set; } =
            86;

        public double TileMinHeight { get; private set; } =
            166;

        public bool LoadAttempted { get; set; }

        public bool IsLoading { get; set; }

        public string? Error { get; set; }

        public ImageSource? Thumbnail
        {
            get =>
                _thumbnail;

            set
            {
                if (ReferenceEquals(
                        _thumbnail,
                        value))
                {
                    return;
                }

                _thumbnail =
                    value;

                OnPropertyChanged();
            }
        }

        public string LoadingText
        {
            get =>
                _loadingText;

            set
            {
                if (string.Equals(
                        _loadingText,
                        value,
                        StringComparison.Ordinal))
                {
                    return;
                }

                _loadingText =
                    value;

                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler?
            PropertyChanged;

        public void UpdateScale(
            double tileWidth)
        {
            double clamped =
                Math.Clamp(
                    tileWidth,
                    MinimumThumbnailTileWidth,
                    MaximumThumbnailTileWidth);

            TileWidth =
                clamped;

            ThumbnailHeight =
                Math.Max(
                    64,
                    clamped -
                    30);

            TileMinHeight =
                Math.Max(
                    146,
                    ThumbnailHeight +
                    80);

            OnPropertyChanged(
                nameof(TileWidth));

            OnPropertyChanged(
                nameof(ThumbnailHeight));

            OnPropertyChanged(
                nameof(TileMinHeight));
        }

        private void OnPropertyChanged(
            [CallerMemberName] string? propertyName =
                null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    propertyName));
        }
    }
}
