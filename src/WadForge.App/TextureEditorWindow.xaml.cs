using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private int? _activeTextureDirectoryIndex;
    private bool _restoringTextureSelection;
    private readonly Dictionary<int, PixelEditState> _pixelStates =
        new();
    private PixelTool _pixelTool =
        PixelTool.Pencil;
    private int _selectedPaletteIndex;
    private bool _pixelStrokeActive;
    private int? _lastPaintX;
    private int? _lastPaintY;
    private double _previewZoom =
        1.0;
    private int? _zoomTextureDirectoryIndex;
    private const double MinimumPreviewZoom =
        0.25;
    private const double MaximumPreviewZoom =
        32.0;    public TextureEditorWindow()
    {
        InitializeComponent();

        TextureList.ItemsSource =
            _rows;

        PaletteComboBox.SelectedIndex =
            0;

        UpdatePixelToolButtons();
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

    private void PaletteComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (PaletteComboBox.SelectedItem is not
                ComboBoxItem item ||
            item.Tag is not
                string tag)
        {
            return;
        }

        if (string.Equals(
                tag,
                "quake",
                StringComparison.Ordinal))
        {
            _wad2PalettePath =
                null;
        }
        else
        {
            _wad2PalettePath =
                tag;
        }

        if (_document is not
                null &&
            _document.Format ==
                WadFormat.Wad2)
        {
            LoadWad(
                _document.WadPath,
                preserveStagedEdits:
                    true);
        }
    }

    private void AddPalette_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenFileDialog dialog =
            new()
            {
                Title =
                    "Add a custom WAD2 palette",
                Filter =
                    "Palette files|*.lmp;*.pal|All files|*.*",
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
            _ =
                PaletteFile.Load(
                    dialog.FileName);

            string fullPath =
                Path.GetFullPath(
                    dialog.FileName);

            ComboBoxItem? existing =
                PaletteComboBox.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(
                        candidate =>
                            candidate.Tag is
                                string candidateTag &&
                            string.Equals(
                                candidateTag,
                                fullPath,
                                StringComparison.OrdinalIgnoreCase));

            ComboBoxItem item =
                existing ??
                new ComboBoxItem
                {
                    Tag =
                        fullPath,
                    Content =
                        Path.GetFileName(
                            fullPath),
                    Foreground =
                        Brushes.White,
                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(
                                48,
                                56,
                                66))
                };

            if (existing is
                null)
            {
                PaletteComboBox.Items.Add(
                    item);
            }

            PaletteComboBox.SelectedItem =
                item;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Custom WAD2 Palette",
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

            _pixelStates.Clear();

            _document =
                document;

            _activeTextureDirectoryIndex =
                null;

            WadPathText.Text =
                document.WadPath;

            string paletteLabel =
                string.IsNullOrWhiteSpace(
                    _wad2PalettePath)
                    ? "Quake (default)"
                    : Path.GetFileName(
                        _wad2PalettePath);

            WadInfoText.Text =
                $"{document.Format.ToString().ToUpperInvariant()} | {document.Textures.Count:N0} textures" +
                (document.Format ==
                     WadFormat.Wad2
                    ? " | palette: " +
                      paletteLabel
                    : " | embedded texture palettes");

            bool wad2 =
                document.Format ==
                WadFormat.Wad2;

            PalettePanel.Visibility =
                wad2
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            AddPaletteButton.Visibility =
                wad2
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (wad2 &&
                string.IsNullOrWhiteSpace(
                    _wad2PalettePath))
            {
                PaletteComboBox.SelectedIndex =
                    0;
            }

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
        int[] selectedDirectoryIndexes =
            TextureList.SelectedItems
                .Cast<TextureRow>()
                .Select(
                    row =>
                        row.Texture.DirectoryIndex)
                .ToArray();

        int? activeDirectoryIndex =
            _activeTextureDirectoryIndex;

        RefreshRowsPreservingSelection(
            selectedDirectoryIndexes,
            activeDirectoryIndex);
    }

    private TextureRow? GetActiveTextureRow()
    {
        if (_activeTextureDirectoryIndex.HasValue)
        {
            TextureRow? active =
                _rows.FirstOrDefault(
                    row =>
                        row.Texture.DirectoryIndex ==
                        _activeTextureDirectoryIndex.Value);

            if (active is not
                    null &&
                TextureList.SelectedItems.Contains(
                    active))
            {
                return active;
            }
        }

        return TextureList.SelectedItem as
            TextureRow;
    }

    private void RefreshRowsPreservingSelection(
        IReadOnlyCollection<int> selectedDirectoryIndexes,
        int? activeDirectoryIndex)
    {
        _restoringTextureSelection =
            true;

        try
        {
            RefreshRows();

            TextureList.SelectedItems.Clear();

            foreach (TextureRow row in
                     _rows.Where(
                         candidate =>
                             selectedDirectoryIndexes.Contains(
                                 candidate.Texture.DirectoryIndex) &&
                             candidate.Texture.DirectoryIndex !=
                                 activeDirectoryIndex))
            {
                TextureList.SelectedItems.Add(
                    row);
            }

            if (activeDirectoryIndex.HasValue)
            {
                TextureRow? active =
                    _rows.FirstOrDefault(
                        row =>
                            row.Texture.DirectoryIndex ==
                            activeDirectoryIndex.Value &&
                            selectedDirectoryIndexes.Contains(
                                row.Texture.DirectoryIndex));

                if (active is not
                    null)
                {
                    TextureList.SelectedItems.Add(
                        active);
                }
            }
        }
        finally
        {
            _restoringTextureSelection =
                false;
        }

        TextureRow? restoredActive =
            GetActiveTextureRow() ??
            TextureList.SelectedItems
                .OfType<TextureRow>()
                .LastOrDefault();

        _activeTextureDirectoryIndex =
            restoredActive?
                .Texture
                .DirectoryIndex;

        UpdateSelectionSummary();

        ShowTexture(
            restoredActive);
    }
    private void TextureList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateSelectionSummary();

        if (_restoringTextureSelection)
        {
            return;
        }

        TextureRow? row =
            e.AddedItems
                .OfType<TextureRow>()
                .LastOrDefault() ??
            GetActiveTextureRow() ??
            TextureList.SelectedItem as
                TextureRow;

        _activeTextureDirectoryIndex =
            row?
                .Texture
                .DirectoryIndex;

        ShowTexture(
            row);
    }

    private void ShowTexture(
        TextureRow? row)
    {
        bool fitNewTexture =
            row is not
                null &&
            _zoomTextureDirectoryIndex !=
                row.Texture.DirectoryIndex;

        _updatingEditorFields =
            true;

        try
        {
            if (row is
                null)
            {
                _zoomTextureDirectoryIndex =
                    null;

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

                PixelToolsPanel.IsEnabled =
                    false;

                TextureDetailsText.Text =
                    "Select a texture for details.";

                PreviewImage.Source =
                    null;

                PreviewPlaceholder.Visibility =
                    Visibility.Visible;

                PaletteGrid.Children.Clear();

                return;
            }

            _zoomTextureDirectoryIndex =
                row.Texture.DirectoryIndex;

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
                TextureList.SelectedItems
                    .OfType<TextureRow>()
                    .Any(
                        selectedRow =>
                            selectedRow.Texture.DominantEdgeIndex !=
                            255);

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

            PixelToolsPanel.IsEnabled =
                true;

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

        PixelEditState state =
            EnsurePixelState(
                row);

        RefreshPaletteGrid(
            state.Palette);

        UpdateUndoRedoButtons(
            state);

        RefreshPreview();

        if (fitNewTexture)
        {
            Dispatcher.BeginInvoke(
                new Action(
                    FitPreviewToViewport));
        }
    }
    private void MaskedCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_updatingEditorFields)
        {
            return;
        }

        ApplyMaskedStateToSelection(
            MaskedCheckBox.IsChecked ==
            true);
    }
    private void EditorField_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_updatingEditorFields)
        {
            return;
        }

        if (ReferenceEquals(
                sender,
                RemapEdgeCheckBox))
        {
            ApplyRemapStateToSelection(
                RemapEdgeCheckBox.IsChecked ==
                true);

            return;
        }

        CaptureCurrentEdit();

        RefreshPreview();
    }
    private static bool IsValidDraftName(
        string name)
    {
        return
            !string.IsNullOrWhiteSpace(
                name) &&
            !name.Any(
                character =>
                    character >
                    127) &&
            System.Text.Encoding.ASCII.GetByteCount(
                name) <=
            16;
    }

    private void CaptureCurrentEdit()
    {
        if (_updatingEditorFields ||
            _document is
                null ||
            GetActiveTextureRow() is not
                TextureRow row)
        {
            return;
        }

        string newName =
            TextureNameTextBox.Text.Trim();

        if (!IsValidDraftName(
                newName))
        {
            return;
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

        SetStagedTextureEdit(
            row,
            newName,
            remap,
            GetEditedPixelsForRow(
                row));

        UnstageEditButton.IsEnabled =
            _stagedEdits.ContainsKey(
                row.Texture.DirectoryIndex);

        UpdatePendingUi();
    }
    private void UpdateSelectionSummary()
    {
        int count =
            TextureList.SelectedItems.Count;

        SelectionSummaryText.Text =
            count ==
                0
                ? "0 selected - Ctrl/Shift-click for batch actions"
                : count ==
                    1
                    ? "1 selected - Ctrl/Shift-click for batch actions"
                    : $"{count:N0} selected - checkbox edits apply to all";

        bool enabled =
            count >
            0;

        BatchAddMaskButton.IsEnabled =
            enabled;

        BatchRemoveMaskButton.IsEnabled =
            enabled;
    }
    private void BatchAddMask_Click(
        object sender,
        RoutedEventArgs e)
    {
        ApplyBatchMaskPrefix(
            addPrefix:
                true);
    }

    private void BatchRemoveMask_Click(
        object sender,
        RoutedEventArgs e)
    {
        ApplyBatchMaskPrefix(
            addPrefix:
                false);
    }

    private void ApplyBatchMaskPrefix(
        bool addPrefix)
    {
        ApplyMaskedStateToSelection(
            addPrefix);
    }
    private void ApplyMaskedStateToSelection(
        bool masked)
    {
        TextureRow[] selected =
            TextureList.SelectedItems
                .Cast<TextureRow>()
                .ToArray();

        if (selected.Length ==
            0)
        {
            return;
        }

        int[] selectedDirectoryIndexes =
            selected
                .Select(
                    row =>
                        row.Texture.DirectoryIndex)
                .ToArray();

        int? activeDirectoryIndex =
            _activeTextureDirectoryIndex;

        int changed =
            0;

        int skipped =
            0;

        foreach (TextureRow row in
                 selected)
        {
            WadTextureEdit? existing =
                _stagedEdits.TryGetValue(
                        row.Texture.DirectoryIndex,
                        out WadTextureEdit? pending)
                    ? pending
                    : null;

            string currentName =
                existing?.NewInternalName ??
                row.Texture.InternalName;

            string newName =
                masked
                    ? currentName.StartsWith(
                          "{",
                          StringComparison.Ordinal)
                        ? currentName
                        : "{" +
                          currentName
                    : currentName.StartsWith(
                          "{",
                          StringComparison.Ordinal)
                        ? currentName[1..]
                        : currentName;

            if (!IsValidDraftName(
                    newName))
            {
                skipped++;

                continue;
            }

            SetStagedTextureEdit(
                row,
                newName,
                existing?.RemapIndexTo255,
                GetEditedPixelsForRow(
                    row));

            changed++;
        }

        RefreshRowsPreservingSelection(
            selectedDirectoryIndexes,
            activeDirectoryIndex);

        UpdatePendingUi();

        StatusText.Text =
            skipped ==
                0
                ? $"Masked state applied to {changed:N0} selected texture(s)."
                : $"Masked state applied to {changed:N0} texture(s); skipped {skipped:N0} invalid or overlength name(s).";
    }

    private void ApplyRemapStateToSelection(
        bool remapTo255)
    {
        TextureRow[] selected =
            TextureList.SelectedItems
                .Cast<TextureRow>()
                .ToArray();

        if (selected.Length ==
            0)
        {
            return;
        }

        int[] selectedDirectoryIndexes =
            selected
                .Select(
                    row =>
                        row.Texture.DirectoryIndex)
                .ToArray();

        int? activeDirectoryIndex =
            _activeTextureDirectoryIndex;

        int changed =
            0;

        foreach (TextureRow row in
                 selected)
        {
            WadTextureEdit? existing =
                _stagedEdits.TryGetValue(
                        row.Texture.DirectoryIndex,
                        out WadTextureEdit? pending)
                    ? pending
                    : null;

            string currentName =
                existing?.NewInternalName ??
                row.Texture.InternalName;

            int? remap =
                remapTo255 &&
                row.Texture.DominantEdgeIndex !=
                    255
                    ? row.Texture.DominantEdgeIndex
                    : null;

            SetStagedTextureEdit(
                row,
                currentName,
                remap,
                GetEditedPixelsForRow(
                    row));

            changed++;
        }

        RefreshRowsPreservingSelection(
            selectedDirectoryIndexes,
            activeDirectoryIndex);

        UpdatePendingUi();

        StatusText.Text =
            remapTo255
                ? $"Remap to 255 staged for {changed:N0} selected texture(s)."
                : $"Remap to 255 cleared for {changed:N0} selected texture(s).";
    }

    private void SetStagedTextureEdit(
        TextureRow row,
        string newName,
        int? remapIndexTo255,
        byte[]? editedPixels)
    {
        if (remapIndexTo255 ==
            255)
        {
            remapIndexTo255 =
                null;
        }

        byte[]? storedPixels =
            editedPixels;

        if (_pixelStates.TryGetValue(
                row.Texture.DirectoryIndex,
                out PixelEditState? state) &&
            state.Pixels.SequenceEqual(
                state.OriginalPixels))
        {
            storedPixels =
                null;
        }

        bool differsFromStored =
            !string.Equals(
                newName,
                row.Texture.InternalName,
                StringComparison.Ordinal) ||
            remapIndexTo255.HasValue ||
            storedPixels is not
                null;

        if (differsFromStored)
        {
            _stagedEdits[
                row.Texture.DirectoryIndex] =
                new WadTextureEdit(
                    row.Texture.DirectoryIndex,
                    newName,
                    remapIndexTo255,
                    storedPixels?
                        .ToArray());
        }
        else
        {
            _stagedEdits.Remove(
                row.Texture.DirectoryIndex);
        }
    }

    private byte[]? GetEditedPixelsForRow(
        TextureRow row)
    {
        if (_pixelStates.TryGetValue(
                row.Texture.DirectoryIndex,
                out PixelEditState? state))
        {
            return state.Pixels.SequenceEqual(
                    state.OriginalPixels)
                ? null
                : state.Pixels;
        }

        return _stagedEdits.TryGetValue(
                row.Texture.DirectoryIndex,
                out WadTextureEdit? staged)
            ? staged.EditedMip0Pixels
            : null;
    }

    private PixelEditState EnsurePixelState(
        TextureRow row)
    {
        if (_pixelStates.TryGetValue(
                row.Texture.DirectoryIndex,
                out PixelEditState? existing))
        {
            return existing;
        }

        if (_document is
            null)
        {
            throw new InvalidOperationException(
                "No WAD is loaded.");
        }

        WadIndexedTextureData data =
            WadTextureEditorService.ReadIndexedTexture(
                _document.WadPath,
                row.Texture.DirectoryIndex,
                _wad2PalettePath);

        byte[] currentPixels =
            _stagedEdits.TryGetValue(
                    row.Texture.DirectoryIndex,
                    out WadTextureEdit? staged) &&
                staged.EditedMip0Pixels is not
                    null
                ? staged.EditedMip0Pixels.ToArray()
                : data.Pixels.ToArray();

        if (currentPixels.Length !=
            data.Pixels.Length)
        {
            throw new InvalidOperationException(
                "The staged pixel buffer no longer matches this texture.");
        }

        PixelEditState state =
            new(
                data.Width,
                data.Height,
                data.Pixels.ToArray(),
                currentPixels,
                data.Palette);

        _pixelStates[
            row.Texture.DirectoryIndex] =
            state;

        return state;
    }

    private void RefreshPaletteGrid(
        IReadOnlyList<Rgb24> palette)
    {
        PaletteGrid.Children.Clear();

        for (int index = 0;
             index <
                 256;
             index++)
        {
            Rgb24 color =
                palette[index];

            Button button =
                new()
                {
                    Tag =
                        index,
                    MinWidth =
                        13,
                    MinHeight =
                        13,
                    Padding =
                        new Thickness(
                            0),
                    Margin =
                        new Thickness(
                            0.5),
                    BorderBrush =
                        index ==
                            _selectedPaletteIndex
                            ? Brushes.White
                            : Brushes.DimGray,
                    BorderThickness =
                        index ==
                            _selectedPaletteIndex
                            ? new Thickness(
                                2)
                            : new Thickness(
                                0.5),
                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(
                                color.R,
                                color.G,
                                color.B)),
                    ToolTip =
                        index ==
                            255
                            ? "Index 255 - transparency when masked"
                            : $"Index {index} - RGB {color.R}, {color.G}, {color.B}"
                };

            button.Click +=
                PaletteIndexButton_Click;

            PaletteGrid.Children.Add(
                button);
        }

        UpdateSelectedPaletteDisplay(
            palette);
    }

    private void PaletteIndexButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not
                Button button ||
            button.Tag is not
                int index ||
            GetActiveTextureRow() is not
                TextureRow row)
        {
            return;
        }

        _selectedPaletteIndex =
            index;

        RefreshPaletteGrid(
            EnsurePixelState(
                row).Palette);
    }

    private void UpdateSelectedPaletteDisplay(
        IReadOnlyList<Rgb24> palette)
    {
        Rgb24 color =
            palette[
                _selectedPaletteIndex];

        SelectedPaletteSwatch.Background =
            new SolidColorBrush(
                Color.FromRgb(
                    color.R,
                    color.G,
                    color.B));

        SelectedPaletteIndexText.Text =
            _selectedPaletteIndex ==
                255
                ? "Index 255 - transparency"
                : $"Index {_selectedPaletteIndex} - RGB {color.R}, {color.G}, {color.B}";
    }

    private void PencilTool_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetPixelTool(
            PixelTool.Pencil);
    }

    private void FillTool_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetPixelTool(
            PixelTool.Fill);
    }

    private void EyedropperTool_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetPixelTool(
            PixelTool.Eyedropper);
    }

    private void TransparencyTool_Click(
        object sender,
        RoutedEventArgs e)
    {
        _selectedPaletteIndex =
            255;

        SetPixelTool(
            PixelTool.Pencil);

        if (GetActiveTextureRow() is
            TextureRow row)
        {
            RefreshPaletteGrid(
                EnsurePixelState(
                    row).Palette);
        }

        StatusText.Text =
            "Transparency selected: painting writes palette index 255.";
    }

    private void SetPixelTool(
        PixelTool tool)
    {
        _pixelTool =
            tool;

        UpdatePixelToolButtons();
    }

    private void UpdatePixelToolButtons()
    {
        foreach (Button button in
                 new[]
                 {
                     PencilToolButton,
                     FillToolButton,
                     EyedropperToolButton
                 })
        {
            button.ClearValue(
                Button.BackgroundProperty);
        }

        Button active =
            _pixelTool switch
            {
                PixelTool.Fill =>
                    FillToolButton,
                PixelTool.Eyedropper =>
                    EyedropperToolButton,
                _ =>
                    PencilToolButton
            };

        active.Background =
            new SolidColorBrush(
                Color.FromRgb(
                    148,
                    106,
                    24));
    }

    private int GetBrushSize()
    {
        if (BrushSizeComboBox.SelectedItem is
                ComboBoxItem item &&
            int.TryParse(
                item.Tag?.ToString(),
                out int size))
        {
            return size;
        }

        return 1;
    }

    private void ZoomOut_Click(
        object sender,
        RoutedEventArgs e)
    {
        ZoomPreviewBy(
            1.0 /
            1.25);
    }

    private void ZoomIn_Click(
        object sender,
        RoutedEventArgs e)
    {
        ZoomPreviewBy(
            1.25);
    }

    private void ZoomFit_Click(
        object sender,
        RoutedEventArgs e)
    {
        FitPreviewToViewport();
    }

    private void ZoomActual_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetPreviewZoom(
            1.0,
            "1:1");
    }

    private void PreviewScrollViewer_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (GetActiveTextureRow() is
            null)
        {
            return;
        }

        ZoomPreviewBy(
            e.Delta >
                0
                ? 1.25
                : 1.0 /
                  1.25);

        e.Handled =
            true;
    }

    private void ZoomPreviewBy(
        double factor)
    {
        double oldExtentWidth =
            Math.Max(
                1,
                PreviewScrollViewer.ExtentWidth);

        double oldExtentHeight =
            Math.Max(
                1,
                PreviewScrollViewer.ExtentHeight);

        double centerRatioX =
            (PreviewScrollViewer.HorizontalOffset +
             (PreviewScrollViewer.ViewportWidth /
              2)) /
            oldExtentWidth;

        double centerRatioY =
            (PreviewScrollViewer.VerticalOffset +
             (PreviewScrollViewer.ViewportHeight /
              2)) /
            oldExtentHeight;

        SetPreviewZoom(
            _previewZoom *
            factor);

        Dispatcher.BeginInvoke(
            new Action(
                () =>
                {
                    double targetX =
                        (centerRatioX *
                         PreviewScrollViewer.ExtentWidth) -
                        (PreviewScrollViewer.ViewportWidth /
                         2);

                    double targetY =
                        (centerRatioY *
                         PreviewScrollViewer.ExtentHeight) -
                        (PreviewScrollViewer.ViewportHeight /
                         2);

                    PreviewScrollViewer.ScrollToHorizontalOffset(
                        Math.Max(
                            0,
                            targetX));

                    PreviewScrollViewer.ScrollToVerticalOffset(
                        Math.Max(
                            0,
                            targetY));
                }));
    }

    private void FitPreviewToViewport()
    {
        if (GetActiveTextureRow() is not
                TextureRow row)
        {
            return;
        }

        PixelEditState state =
            EnsurePixelState(
                row);

        double availableWidth =
            Math.Max(
                1,
                PreviewScrollViewer.ViewportWidth -
                38);

        double availableHeight =
            Math.Max(
                1,
                PreviewScrollViewer.ViewportHeight -
                38);

        double zoom =
            Math.Min(
                availableWidth /
                    state.Width,
                availableHeight /
                    state.Height);

        SetPreviewZoom(
            zoom,
            "Fit");

        Dispatcher.BeginInvoke(
            new Action(
                () =>
                {
                    PreviewScrollViewer.ScrollToHorizontalOffset(
                        0);

                    PreviewScrollViewer.ScrollToVerticalOffset(
                        0);
                }));
    }

    private void SetPreviewZoom(
        double zoom,
        string? label = null)
    {
        _previewZoom =
            Math.Clamp(
                zoom,
                MinimumPreviewZoom,
                MaximumPreviewZoom);

        PreviewScaleTransform.ScaleX =
            _previewZoom;

        PreviewScaleTransform.ScaleY =
            _previewZoom;

        ZoomText.Text =
            label ??
            $"{_previewZoom:0.##}x";
    }
    private void PreviewImage_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (GetActiveTextureRow() is not
                TextureRow row)
        {
            return;
        }

        PixelEditState state =
            EnsurePixelState(
                row);

        if (!TryGetTexturePixel(
                e,
                state,
                out int x,
                out int y))
        {
            return;
        }

        if (_pixelTool ==
            PixelTool.Eyedropper)
        {
            _selectedPaletteIndex =
                state.Pixels[
                    (y *
                     state.Width) +
                    x];

            RefreshPaletteGrid(
                state.Palette);

            StatusText.Text =
                $"Picked palette index {_selectedPaletteIndex}.";

            e.Handled =
                true;

            return;
        }

        if (_selectedPaletteIndex ==
                255 &&
            !TryEnsureActiveTextureMasked(
                row))
        {
            e.Handled =
                true;

            return;
        }

        BeginPixelOperation(
            state);

        if (_pixelTool ==
            PixelTool.Fill)
        {
            FloodFill(
                state,
                x,
                y,
                (byte)_selectedPaletteIndex);

            CompletePixelOperation(
                row,
                state);

            e.Handled =
                true;

            return;
        }

        _pixelStrokeActive =
            true;

        _lastPaintX =
            x;

        _lastPaintY =
            y;

        PaintBrushAt(
            state,
            x,
            y,
            (byte)_selectedPaletteIndex);

        CaptureCurrentEdit();

        RefreshPreview();

        PreviewImage.CaptureMouse();

        e.Handled =
            true;
    }

    private void PreviewImage_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_pixelStrokeActive ||
            e.LeftButton !=
                MouseButtonState.Pressed ||
            GetActiveTextureRow() is not
                TextureRow row)
        {
            return;
        }

        PixelEditState state =
            EnsurePixelState(
                row);

        if (!TryGetTexturePixel(
                e,
                state,
                out int x,
                out int y))
        {
            return;
        }

        PaintLine(
            state,
            _lastPaintX ??
                x,
            _lastPaintY ??
                y,
            x,
            y,
            (byte)_selectedPaletteIndex);

        _lastPaintX =
            x;

        _lastPaintY =
            y;

        CaptureCurrentEdit();

        RefreshPreview();

        e.Handled =
            true;
    }

    private void PreviewImage_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_pixelStrokeActive)
        {
            return;
        }

        _pixelStrokeActive =
            false;

        _lastPaintX =
            null;

        _lastPaintY =
            null;

        PreviewImage.ReleaseMouseCapture();

        if (GetActiveTextureRow() is
                TextureRow row &&
            _pixelStates.TryGetValue(
                row.Texture.DirectoryIndex,
                out PixelEditState? state))
        {
            CompletePixelOperation(
                row,
                state);
        }

        e.Handled =
            true;
    }

    private bool TryGetTexturePixel(
        MouseEventArgs e,
        PixelEditState state,
        out int x,
        out int y)
    {
        x =
            0;

        y =
            0;

        if (PreviewImage.ActualWidth <=
                0 ||
            PreviewImage.ActualHeight <=
                0)
        {
            return false;
        }

        Point point =
            e.GetPosition(
                PreviewImage);

        if (point.X <
                0 ||
            point.Y <
                0 ||
            point.X >=
                PreviewImage.ActualWidth ||
            point.Y >=
                PreviewImage.ActualHeight)
        {
            return false;
        }

        double normalizedX =
            point.X /
            PreviewImage.ActualWidth;

        double normalizedY =
            point.Y /
            PreviewImage.ActualHeight;

        x =
            Math.Clamp(
                (int)(normalizedX *
                      state.Width),
                0,
                state.Width -
                    1);

        y =
            Math.Clamp(
                (int)(normalizedY *
                      state.Height),
                0,
                state.Height -
                    1);

        return true;
    }
    private void BeginPixelOperation(
        PixelEditState state)
    {
        state.Undo.Push(
            state.Pixels.ToArray());

        state.Redo.Clear();

        UpdateUndoRedoButtons(
            state);
    }

    private void CompletePixelOperation(
        TextureRow row,
        PixelEditState state)
    {
        CaptureCurrentEdit();

        int[] selectedDirectoryIndexes =
            TextureList.SelectedItems
                .Cast<TextureRow>()
                .Select(
                    selected =>
                        selected.Texture.DirectoryIndex)
                .ToArray();

        RefreshRowsPreservingSelection(
            selectedDirectoryIndexes,
            row.Texture.DirectoryIndex);

        UpdateUndoRedoButtons(
            state);

        UpdatePendingUi();

        RefreshPreview();
    }

    private void PaintLine(
        PixelEditState state,
        int x0,
        int y0,
        int x1,
        int y1,
        byte paletteIndex)
    {
        int dx =
            Math.Abs(
                x1 -
                x0);

        int sx =
            x0 <
                x1
                ? 1
                : -1;

        int dy =
            -Math.Abs(
                y1 -
                y0);

        int sy =
            y0 <
                y1
                ? 1
                : -1;

        int error =
            dx +
            dy;

        while (true)
        {
            PaintBrushAt(
                state,
                x0,
                y0,
                paletteIndex);

            if (x0 ==
                    x1 &&
                y0 ==
                    y1)
            {
                break;
            }

            int doubled =
                2 *
                error;

            if (doubled >=
                dy)
            {
                error +=
                    dy;

                x0 +=
                    sx;
            }

            if (doubled <=
                dx)
            {
                error +=
                    dx;

                y0 +=
                    sy;
            }
        }
    }

    private void PaintBrushAt(
        PixelEditState state,
        int centerX,
        int centerY,
        byte paletteIndex)
    {
        int brushSize =
            GetBrushSize();

        int half =
            brushSize /
            2;

        for (int y = centerY -
                         half;
             y <=
                 centerY +
                 half;
             y++)
        {
            if (y <
                    0 ||
                y >=
                    state.Height)
            {
                continue;
            }

            for (int x = centerX -
                             half;
                 x <=
                     centerX +
                     half;
                 x++)
            {
                if (x <
                        0 ||
                    x >=
                        state.Width)
                {
                    continue;
                }

                state.Pixels[
                    (y *
                     state.Width) +
                    x] =
                    paletteIndex;
            }
        }
    }

    private static void FloodFill(
        PixelEditState state,
        int startX,
        int startY,
        byte replacement)
    {
        int startIndex =
            (startY *
             state.Width) +
            startX;

        byte target =
            state.Pixels[
                startIndex];

        if (target ==
            replacement)
        {
            return;
        }

        Queue<int> queue =
            new();

        queue.Enqueue(
            startIndex);

        state.Pixels[
            startIndex] =
            replacement;

        while (queue.Count >
               0)
        {
            int index =
                queue.Dequeue();

            int x =
                index %
                state.Width;

            int y =
                index /
                state.Width;

            TryQueue(
                x -
                    1,
                y);

            TryQueue(
                x +
                    1,
                y);

            TryQueue(
                x,
                y -
                    1);

            TryQueue(
                x,
                y +
                    1);
        }

        void TryQueue(
            int x,
            int y)
        {
            if (x <
                    0 ||
                y <
                    0 ||
                x >=
                    state.Width ||
                y >=
                    state.Height)
            {
                return;
            }

            int index =
                (y *
                 state.Width) +
                x;

            if (state.Pixels[index] !=
                target)
            {
                return;
            }

            state.Pixels[index] =
                replacement;

            queue.Enqueue(
                index);
        }
    }

    private bool TryEnsureActiveTextureMasked(
        TextureRow row)
    {
        WadTextureEdit? existing =
            _stagedEdits.TryGetValue(
                    row.Texture.DirectoryIndex,
                    out WadTextureEdit? pending)
                ? pending
                : null;

        string currentName =
            existing?.NewInternalName ??
            row.Texture.InternalName;

        if (currentName.StartsWith(
                "{",
                StringComparison.Ordinal))
        {
            return true;
        }

        string maskedName =
            "{" +
            currentName;

        if (!IsValidDraftName(
                maskedName))
        {
            StatusText.Text =
                "Transparency painting needs a { mask prefix, but adding it would exceed the 16-byte texture-name limit. Shorten the internal name first.";

            return false;
        }

        _updatingEditorFields =
            true;

        try
        {
            TextureNameTextBox.Text =
                maskedName;

            MaskedCheckBox.IsChecked =
                true;
        }
        finally
        {
            _updatingEditorFields =
                false;
        }

        SetStagedTextureEdit(
            row,
            maskedName,
            existing?.RemapIndexTo255,
            GetEditedPixelsForRow(
                row));

        UpdatePendingUi();

        return true;
    }

    private void UndoPixel_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GetActiveTextureRow() is not
                TextureRow row)
        {
            return;
        }

        PixelEditState state =
            EnsurePixelState(
                row);

        if (state.Undo.Count ==
            0)
        {
            return;
        }

        state.Redo.Push(
            state.Pixels.ToArray());

        state.Pixels =
            state.Undo.Pop();

        CompletePixelOperation(
            row,
            state);
    }

    private void RedoPixel_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GetActiveTextureRow() is not
                TextureRow row)
        {
            return;
        }

        PixelEditState state =
            EnsurePixelState(
                row);

        if (state.Redo.Count ==
            0)
        {
            return;
        }

        state.Undo.Push(
            state.Pixels.ToArray());

        state.Pixels =
            state.Redo.Pop();

        CompletePixelOperation(
            row,
            state);
    }

    private void UpdateUndoRedoButtons(
        PixelEditState state)
    {
        UndoPixelButton.IsEnabled =
            state.Undo.Count >
            0;

        RedoPixelButton.IsEnabled =
            state.Redo.Count >
            0;
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
            GetActiveTextureRow() is not
                TextureRow row)
        {
            return;
        }

        try
        {
            PixelEditState state =
                EnsurePixelState(
                    row);

            byte[] previewPixels =
                state.Pixels.ToArray();

            WadTextureEdit? staged =
                _stagedEdits.TryGetValue(
                        row.Texture.DirectoryIndex,
                        out WadTextureEdit? pending)
                    ? pending
                    : null;

            string name =
                staged?.NewInternalName ??
                row.Texture.InternalName;

            int? remap =
                staged?.RemapIndexTo255;

            bool showTransparency =
                PreviewRepairCheckBox.IsChecked ==
                true;

            bool make255Transparent =
                false;

            if (showTransparency &&
                remap.HasValue &&
                remap.Value !=
                    255)
            {
                for (int index = 0;
                     index <
                         previewPixels.Length;
                     index++)
                {
                    if (previewPixels[index] ==
                        (byte)remap.Value)
                    {
                        previewPixels[index] =
                            255;
                    }
                }

                make255Transparent =
                    true;
            }

            if (showTransparency &&
                name.StartsWith(
                    "{",
                    StringComparison.Ordinal))
            {
                make255Transparent =
                    true;
            }

            RgbaImage preview =
                WadTextureEditorService.RenderIndexedPreview(
                    state.Width,
                    state.Height,
                    previewPixels,
                    state.Palette,
                    make255Transparent
                        ? 255
                        : null);

            PreviewImage.Source =
                CreateBitmap(
                    preview);

            PreviewPlaceholder.Visibility =
                Visibility.Collapsed;
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
        if (GetActiveTextureRow() is not
                TextureRow row)
        {
            return;
        }

        CaptureCurrentEdit();

        int[] selectedDirectoryIndexes =
            TextureList.SelectedItems
                .Cast<TextureRow>()
                .Select(
                    selected =>
                        selected.Texture.DirectoryIndex)
                .ToArray();

        RefreshRowsPreservingSelection(
            selectedDirectoryIndexes,
            row.Texture.DirectoryIndex);

        UpdatePendingUi();

        StatusText.Text =
            $"Staged changes for '{row.Texture.InternalName}'.";
    }
    private void UnstageEdit_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GetActiveTextureRow() is not
            TextureRow row)
        {
            return;
        }

        int[] selectedDirectoryIndexes =
            TextureList.SelectedItems
                .Cast<TextureRow>()
                .Select(
                    selected =>
                        selected.Texture.DirectoryIndex)
                .ToArray();

        _stagedEdits.Remove(
            row.Texture.DirectoryIndex);

        _pixelStates.Remove(
            row.Texture.DirectoryIndex);

        RefreshRowsPreservingSelection(
            selectedDirectoryIndexes,
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
            _pixelStates.Clear();

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
            _pixelStates.Clear();

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

    private enum PixelTool
    {
        Pencil,
        Fill,
        Eyedropper
    }

    private sealed class PixelEditState
    {
        public PixelEditState(
            int width,
            int height,
            byte[] originalPixels,
            byte[] pixels,
            IReadOnlyList<Rgb24> palette)
        {
            Width =
                width;

            Height =
                height;

            OriginalPixels =
                originalPixels;

            Pixels =
                pixels;

            Palette =
                palette;
        }

        public int Width { get; }

        public int Height { get; }

        public byte[] OriginalPixels { get; }

        public byte[] Pixels { get; set; }

        public IReadOnlyList<Rgb24> Palette { get; }

        public Stack<byte[]> Undo { get; } =
            new();

        public Stack<byte[]> Redo { get; } =
            new();
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
