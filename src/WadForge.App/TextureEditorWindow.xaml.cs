using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WadForge.Core;
using WadForge.Imaging;
using WadForge.Wad;

namespace WadForge.App;

public partial class TextureEditorWindow : Window
{
    private readonly ObservableCollection<TextureRow> _rows =
        new();

    private readonly Dictionary<int, WadTextureEdit> _stagedEdits =
        new();

    private WadTextureEditorDocument? _document;
    private string? _wad2PalettePath;
    private bool _updatingEditorFields;

    public TextureEditorWindow()
    {
        InitializeComponent();

        TextureList.ItemsSource =
            _rows;
    }

    public void OpenWad(
        string wadPath)
    {
        LoadWad(
            wadPath);
    }

    private void OpenWad_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenFileDialog dialog =
            new()
            {
                Title =
                    "Open a WAD in the Texture Editor",
                Filter =
                    "WAD archives|*.wad|All files|*.*",
                Multiselect =
                    false,
                CheckFileExists =
                    true,
                CheckPathExists =
                    true
            };

        if (dialog.ShowDialog(
                this) ==
            true)
        {
            LoadWad(
                dialog.FileName);
        }
    }

    private void ChoosePalette_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenFileDialog dialog =
            new()
            {
                Title =
                    "Choose a WAD2 palette",
                Filter =
                    "Palette files|*.lmp;*.pal|All files|*.*",
                Multiselect =
                    false,
                CheckFileExists =
                    true,
                CheckPathExists =
                    true
            };

        if (!string.IsNullOrWhiteSpace(
                _wad2PalettePath) &&
            File.Exists(
                _wad2PalettePath))
        {
            dialog.InitialDirectory =
                Path.GetDirectoryName(
                    _wad2PalettePath);
        }

        if (dialog.ShowDialog(
                this) !=
            true)
        {
            return;
        }

        try
        {
            _ =
                PaletteFile.Load(
                    dialog.FileName);

            _wad2PalettePath =
                dialog.FileName;

            if (_document is
                not null)
            {
                LoadWad(
                    _document.WadPath,
                    preserveStagedEdits:
                        true);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "WAD2 Palette",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void LoadWad(
        string wadPath,
        bool preserveStagedEdits = false)
    {
        try
        {
            WadTextureEditorDocument document =
                WadTextureEditorService.Load(
                    wadPath,
                    _wad2PalettePath);

            if (!preserveStagedEdits)
            {
                _stagedEdits.Clear();
            }

            _document =
                document;

            WadPathText.Text =
                document.WadPath;

            WadInfoText.Text =
                $"{document.Format.ToString().ToUpperInvariant()} · {document.Textures.Count:N0} textures" +
                (document.Format ==
                     WadFormat.Wad2 &&
                 string.IsNullOrWhiteSpace(
                     _wad2PalettePath)
                    ? " · choose a WAD2 palette for color previews"
                    : string.Empty);

            RefreshRows();

            TextureList.SelectedIndex =
                _rows.Count >
                    0
                    ? 0
                    : -1;

            UpdatePendingUi();

            StatusText.Text =
                "WAD loaded. Select a texture to preview or edit it.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Texture Editor",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RefreshRows()
    {
        _rows.Clear();

        if (_document is
            null)
        {
            return;
        }

        string search =
            SearchTextBox.Text.Trim();

        foreach (WadTextureEditorTexture texture in
                 _document.Textures)
        {
            if (search.Length >
                    0 &&
                !texture.InternalName.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool staged =
                _stagedEdits.ContainsKey(
                    texture.DirectoryIndex);

            string status =
                staged
                    ? "STAGED"
                    : texture.HasMaskPrefix &&
                      texture.UsesIndex255
                        ? "Masked · index 255"
                        : texture.HasMaskPrefix
                            ? "Masked name · no index 255"
                            : texture.UsesIndex255
                                ? "Index 255 present · no { prefix"
                                : $"Edge index {texture.DominantEdgeIndex}";

            _rows.Add(
                new TextureRow(
                    texture,
                    status));
        }
    }

    private void SearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        int? selectedDirectoryIndex =
            (TextureList.SelectedItem as TextureRow)?
                .Texture
                .DirectoryIndex;

        RefreshRows();

        if (selectedDirectoryIndex.HasValue)
        {
            TextureList.SelectedItem =
                _rows.FirstOrDefault(
                    row =>
                        row.Texture.DirectoryIndex ==
                        selectedDirectoryIndex.Value);
        }
    }

    private void TextureList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        TextureRow? row =
            TextureList.SelectedItem as
                TextureRow;

        _updatingEditorFields =
            true;

        try
        {
            if (row is
                null)
            {
                TextureNameTextBox.Text =
                    string.Empty;

                TextureNameTextBox.IsEnabled =
                    false;

                MaskedCheckBox.IsEnabled =
                    false;

                RemapEdgeCheckBox.IsEnabled =
                    false;

                PreviewRepairCheckBox.IsEnabled =
                    false;

                StageEditButton.IsEnabled =
                    false;

                UnstageEditButton.IsEnabled =
                    false;

                TextureDetailsText.Text =
                    "Select a texture for details.";

                PreviewImage.Source =
                    null;

                PreviewPlaceholder.Visibility =
                    Visibility.Visible;

                return;
            }

            WadTextureEditorTexture texture =
                row.Texture;

            WadTextureEdit? staged =
                _stagedEdits.TryGetValue(
                        texture.DirectoryIndex,
                        out WadTextureEdit? pending)
                    ? pending
                    : null;

            string name =
                staged?.NewInternalName ??
                texture.InternalName;

            TextureNameTextBox.Text =
                name;

            TextureNameTextBox.IsEnabled =
                true;

            MaskedCheckBox.IsEnabled =
                true;

            MaskedCheckBox.IsChecked =
                name.StartsWith(
                    "{",
                    StringComparison.Ordinal);

            RemapEdgeCheckBox.IsEnabled =
                texture.DominantEdgeIndex !=
                255;

            RemapEdgeCheckBox.IsChecked =
                staged?.RemapIndexTo255
                    .HasValue ==
                true;

            PreviewRepairCheckBox.IsEnabled =
                true;

            StageEditButton.IsEnabled =
                true;

            UnstageEditButton.IsEnabled =
                staged is not
                null;

            TextureDetailsText.Text =
                $"{texture.Width} x {texture.Height}" +
                Environment.NewLine +
                $"Stored mask prefix: {(texture.HasMaskPrefix ? "yes" : "no")}" +
                Environment.NewLine +
                $"Index 255 pixels in mip 0: {texture.Index255PixelCount:N0}" +
                Environment.NewLine +
                $"Dominant edge index: {texture.DominantEdgeIndex}" +
                Environment.NewLine +
                $"Dominant edge color: {texture.DominantColorText}" +
                Environment.NewLine +
                $"Edge share: {texture.DominantEdgeShare:P1}" +
                Environment.NewLine +
                $"Image share: {texture.DominantAreaShare:P1}";
        }
        finally
        {
            _updatingEditorFields =
                false;
        }

        RefreshPreview();
    }

    private void MaskedCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_updatingEditorFields ||
            TextureList.SelectedItem is not
                TextureRow)
        {
            return;
        }

        _updatingEditorFields =
            true;

        try
        {
            string current =
                TextureNameTextBox.Text.Trim();

            if (MaskedCheckBox.IsChecked ==
                true)
            {
                if (!current.StartsWith(
                        "{",
                        StringComparison.Ordinal))
                {
                    TextureNameTextBox.Text =
                        "{" +
                        current;
                }
            }
            else
            {
                if (current.StartsWith(
                        "{",
                        StringComparison.Ordinal))
                {
                    TextureNameTextBox.Text =
                        current[1..];
                }
            }
        }
        finally
        {
            _updatingEditorFields =
                false;
        }

        RefreshPreview();
    }

    private void EditorField_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_updatingEditorFields)
        {
            return;
        }

        RefreshPreview();
    }

    private void PreviewRepairCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_updatingEditorFields)
        {
            return;
        }

        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_document is
                null ||
            TextureList.SelectedItem is not
                TextureRow row)
        {
            return;
        }

        try
        {
            int? transparentIndex =
                null;

            if (PreviewRepairCheckBox.IsChecked ==
                true)
            {
                if (RemapEdgeCheckBox.IsChecked ==
                    true)
                {
                    transparentIndex =
                        row.Texture.DominantEdgeIndex;
                }
                else if (TextureNameTextBox.Text.Trim()
                    .StartsWith(
                        "{",
                        StringComparison.Ordinal))
                {
                    transparentIndex =
                        255;
                }
            }

            RgbaImage preview =
                WadTextureEditorService.ReadPreview(
                    _document.WadPath,
                    row.Texture.DirectoryIndex,
                    _wad2PalettePath,
                    transparentIndex);

            PreviewImage.Source =
                CreateBitmap(
                    preview);

            PreviewPlaceholder.Visibility =
                Visibility.Collapsed;
        }
        catch (InvalidOperationException exception)
        {
            PreviewImage.Source =
                null;

            PreviewPlaceholder.Text =
                exception.Message;

            PreviewPlaceholder.Visibility =
                Visibility.Visible;
        }
        catch (Exception exception)
        {
            PreviewImage.Source =
                null;

            PreviewPlaceholder.Text =
                "Preview unavailable: " +
                exception.Message;

            PreviewPlaceholder.Visibility =
                Visibility.Visible;
        }
    }

    private void StageEdit_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_document is
                null ||
            TextureList.SelectedItem is not
                TextureRow row)
        {
            return;
        }

        try
        {
            string newName =
                TextureNameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(
                    newName))
            {
                throw new InvalidOperationException(
                    "Texture names cannot be blank.");
            }

            if (newName.Any(
                    character =>
                        character >
                        127))
            {
                throw new InvalidOperationException(
                    "Internal WAD texture names must use ASCII characters.");
            }

            if (System.Text.Encoding.ASCII.GetByteCount(
                    newName) >
                16)
            {
                throw new InvalidOperationException(
                    "Internal WAD texture names are limited to 16 bytes.");
            }

            int? remap =
                RemapEdgeCheckBox.IsChecked ==
                    true
                    ? row.Texture.DominantEdgeIndex
                    : null;

            if (remap ==
                255)
            {
                remap =
                    null;
            }

            _stagedEdits[
                row.Texture.DirectoryIndex] =
                new WadTextureEdit(
                    row.Texture.DirectoryIndex,
                    newName,
                    remap);

            RefreshRows();

            TextureList.SelectedItem =
                _rows.FirstOrDefault(
                    item =>
                        item.Texture.DirectoryIndex ==
                        row.Texture.DirectoryIndex);

            UpdatePendingUi();

            StatusText.Text =
                $"Staged changes for '{row.Texture.InternalName}'.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Stage Texture Edit",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void UnstageEdit_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (TextureList.SelectedItem is not
            TextureRow row)
        {
            return;
        }

        _stagedEdits.Remove(
            row.Texture.DirectoryIndex);

        RefreshRows();

        TextureList.SelectedItem =
            _rows.FirstOrDefault(
                item =>
                    item.Texture.DirectoryIndex ==
                    row.Texture.DirectoryIndex);

        UpdatePendingUi();

        StatusText.Text =
            "Staged edit removed.";
    }

    private void SaveCopy_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_document is
                null ||
            _stagedEdits.Count ==
                0)
        {
            return;
        }

        SaveFileDialog dialog =
            new()
            {
                Title =
                    "Save edited WAD copy",
                Filter =
                    "WAD archives|*.wad|All files|*.*",
                FileName =
                    Path.GetFileNameWithoutExtension(
                        _document.WadPath) +
                    "-edited.wad",
                InitialDirectory =
                    Path.GetDirectoryName(
                        _document.WadPath),
                OverwritePrompt =
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
            WadTextureEditSaveResult result =
                WadTextureEditorService.SaveCopy(
                    _document.WadPath,
                    dialog.FileName,
                    _stagedEdits.Values
                        .OrderBy(
                            edit =>
                                edit.DirectoryIndex)
                        .ToArray());

            _stagedEdits.Clear();

            LoadWad(
                result.OutputPath);

            StatusText.Text =
                $"Saved {result.EditedTextureCount:N0} edited texture(s) to '{result.OutputPath}'.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Save Edited WAD",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SaveInPlace_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_document is
                null ||
            _stagedEdits.Count ==
                0)
        {
            return;
        }

        MessageBoxResult confirmation =
            MessageBox.Show(
                this,
                $"Save {_stagedEdits.Count:N0} staged texture edit(s) into this WAD?{Environment.NewLine}{Environment.NewLine}" +
                "WadForge will create a timestamped backup beside the WAD before replacing it.",
                "Save WAD In Place",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            WadTextureEditSaveResult result =
                WadTextureEditorService.SaveInPlaceWithBackup(
                    _document.WadPath,
                    _stagedEdits.Values
                        .OrderBy(
                            edit =>
                                edit.DirectoryIndex)
                        .ToArray());

            _stagedEdits.Clear();

            LoadWad(
                result.OutputPath);

            StatusText.Text =
                $"Saved in place. Backup: {result.BackupPath}";
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Save WAD In Place",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void UpdatePendingUi()
    {
        PendingText.Text =
            _stagedEdits.Count ==
                1
                ? "1 staged edit"
                : $"{_stagedEdits.Count:N0} staged edits";

        bool canSave =
            _document is not
                null &&
            _stagedEdits.Count >
                0;

        SaveCopyButton.IsEnabled =
            canSave;

        SaveInPlaceButton.IsEnabled =
            canSave;
    }

    private void Window_DragEnter(
        object sender,
        DragEventArgs e)
    {
        e.Effects =
            HasSupportedDrop(
                e.Data)
                ? DragDropEffects.Copy
                : DragDropEffects.None;

        e.Handled =
            true;
    }

    private void Window_Drop(
        object sender,
        DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(
                DataFormats.FileDrop) ||
            e.Data.GetData(
                DataFormats.FileDrop) is not
                string[] paths)
        {
            return;
        }

        string? wad =
            paths.FirstOrDefault(
                path =>
                    string.Equals(
                        Path.GetExtension(
                            path),
                        ".wad",
                        StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(
                wad))
        {
            LoadWad(
                wad);

            return;
        }

        string? palette =
            paths.FirstOrDefault(
                path =>
                    string.Equals(
                        Path.GetExtension(
                            path),
                        ".lmp",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        Path.GetExtension(
                            path),
                        ".pal",
                        StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(
                palette))
        {
            try
            {
                _ =
                    PaletteFile.Load(
                        palette);

                _wad2PalettePath =
                    palette;

                if (_document is
                    not null)
                {
                    LoadWad(
                        _document.WadPath,
                        preserveStagedEdits:
                            true);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "WAD2 Palette",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private static bool HasSupportedDrop(
        IDataObject data)
    {
        if (!data.GetDataPresent(
                DataFormats.FileDrop) ||
            data.GetData(
                DataFormats.FileDrop) is not
                string[] paths)
        {
            return false;
        }

        return paths.Any(
            path =>
                string.Equals(
                    Path.GetExtension(
                        path),
                    ".wad",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    Path.GetExtension(
                        path),
                    ".lmp",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    Path.GetExtension(
                        path),
                    ".pal",
                    StringComparison.OrdinalIgnoreCase));
    }

    private static BitmapSource CreateBitmap(
        RgbaImage image)
    {
        byte[] pixels =
            new byte[
                image.Width *
                image.Height *
                4];

        int destination =
            0;

        foreach (Rgba32 pixel in
                 image.Pixels)
        {
            pixels[destination++] =
                pixel.B;

            pixels[destination++] =
                pixel.G;

            pixels[destination++] =
                pixel.R;

            pixels[destination++] =
                pixel.A;
        }

        BitmapSource bitmap =
            BitmapSource.Create(
                image.Width,
                image.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                image.Width *
                4);

        bitmap.Freeze();

        return bitmap;
    }

    private sealed record TextureRow(
        WadTextureEditorTexture Texture,
        string StatusText)
    {
        public string InternalName =>
            Texture.InternalName;

        public string DimensionsText =>
            $"{Texture.Width} x {Texture.Height}";
    }
}
