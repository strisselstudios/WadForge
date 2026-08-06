using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WadForge.Aliases;
using WadForge.App.Models;
using WadForge.Core;
using WadForge.Imaging;
using WadForge.Wad;

namespace WadForge.App;

public partial class MainWindow : Window
{
    private readonly HashSet<string> _knownPaths = new(
        StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _reservedInternalNames = new(
        StringComparer.OrdinalIgnoreCase);

    private readonly ImageToWadConversionService _conversionService =
        new();

    private ConversionDirection _activeDirection =
        ConversionDirection.ImagesToWad;

    private WadFormat _activeWadFormat =
        WadFormat.Wad2;

    private string? _outputFolder;
    private string? _wad2PalettePath;

    private bool _restoringDirection;
    private bool _restoringWadFormat;
    private bool _conversionRunning;

    public MainWindow()
    {
        InitializeComponent();

        QueueItems =
            new ObservableCollection<BatchQueueItem>();

        DataContext = this;

        UpdateModeUi();
        UpdateWadFormatUi();
        RefreshQueueUi();
    }

    public ObservableCollection<BatchQueueItem> QueueItems { get; }

    private void AddImages_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Add images to WadForge",
            Filter =
                "Supported images|" +
                "*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|" +
                "PNG images|*.png|" +
                "JPEG images|*.jpg;*.jpeg|" +
                "Bitmap images|*.bmp|" +
                "GIF images|*.gif|" +
                "TIFF images|*.tif;*.tiff|" +
                "All files|*.*",
            Multiselect = true,
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            AddPaths(dialog.FileNames);
        }
    }

    private void AddWadFiles_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Add WAD2 or WAD3 files",
            Filter =
                "WAD archives|*.wad|" +
                "All files|*.*",
            Multiselect = true,
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            AddPaths(dialog.FileNames);
        }
    }

    private void ChooseOutputFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Choose WadForge output folder",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(
                _outputFolder) &&
            Directory.Exists(_outputFolder))
        {
            dialog.InitialDirectory =
                _outputFolder;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _outputFolder =
                dialog.FolderName;

            OutputFolderText.Text =
                _outputFolder;

            StatusText.Text =
                "Output folder selected.";

            RefreshConvertAvailability();
        }
    }

    private void ChooseWad2Palette_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Select a WAD2 RGB palette",
            Filter =
                "Palette files|*.lmp;*.pal|" +
                "All files|*.*",
            Multiselect = false,
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _ = PaletteFile.Load(
                dialog.FileName);

            _wad2PalettePath =
                dialog.FileName;

            Wad2PaletteText.Text =
                _wad2PalettePath;

            StatusText.Text =
                "WAD2 palette validated.";

            RefreshConvertAvailability();
        }
        catch (Exception exception)
        {
            _wad2PalettePath = null;

            Wad2PaletteText.Text =
                "No valid palette selected.";

            RefreshConvertAvailability();

            MessageBox.Show(
                this,
                exception.Message,
                "Invalid WAD2 Palette",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RemoveSelected_Click(
        object sender,
        RoutedEventArgs e)
    {
        BatchQueueItem[] selectedItems =
            QueueGrid
                .SelectedItems
                .Cast<BatchQueueItem>()
                .ToArray();

        if (selectedItems.Length == 0)
        {
            return;
        }

        foreach (BatchQueueItem item in selectedItems)
        {
            QueueItems.Remove(item);
            _knownPaths.Remove(item.SourcePath);

            if (!string.IsNullOrWhiteSpace(
                    item.InternalName))
            {
                _reservedInternalNames.Remove(
                    item.InternalName);
            }
        }

        StatusText.Text =
            $"{selectedItems.Length:N0} item(s) removed.";

        RefreshQueueUi();
    }

    private void ClearQueue_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (QueueItems.Count == 0)
        {
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            "Remove every item from the current queue?",
            "Clear WadForge Queue",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        ClearQueueInternal();
        StatusText.Text = "Queue cleared.";
    }

    private void QueueGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        RemoveSelectedButton.IsEnabled =
            !_conversionRunning &&
            QueueGrid.SelectedItems.Count > 0;
    }

    private void ConversionDirection_Checked(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsLoaded ||
            _restoringDirection)
        {
            return;
        }

        ConversionDirection requestedDirection =
            ImagesToWadRadio.IsChecked == true
                ? ConversionDirection.ImagesToWad
                : ConversionDirection.WadToPng;

        if (requestedDirection ==
            _activeDirection)
        {
            UpdateModeUi();
            return;
        }

        if (QueueItems.Count > 0)
        {
            MessageBoxResult result = MessageBox.Show(
                this,
                "Changing conversion direction requires " +
                "clearing the current queue.",
                "Change Conversion Direction",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
            {
                RestoreDirectionSelection();
                return;
            }

            ClearQueueInternal();
        }

        _activeDirection =
            requestedDirection;

        UpdateModeUi();

        StatusText.Text =
            "Ready for files.";
    }

    private void WadFormat_Checked(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsLoaded ||
            _restoringWadFormat)
        {
            return;
        }

        WadFormat requestedFormat =
            Wad3Radio.IsChecked == true
                ? WadFormat.Wad3
                : WadFormat.Wad2;

        if (requestedFormat ==
            _activeWadFormat)
        {
            UpdateWadFormatUi();
            RefreshConvertAvailability();
            return;
        }

        if (QueueItems.Count > 0)
        {
            MessageBox.Show(
                this,
                "Clear the input queue before switching between WAD2 and WAD3.",
                "Clear Input Queue First",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            RestoreWadFormatSelection();
            return;
        }

        _activeWadFormat =
            requestedFormat;

        UpdateWadFormatUi();
        RefreshConvertAvailability();

        StatusText.Text =
            _activeWadFormat == WadFormat.Wad2
                ? "WAD2 output selected."
                : "WAD3 output selected.";
    }

    private void OutputWadName_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        RefreshConvertAvailability();
    }

    private void RestoreDirectionSelection()
    {
        _restoringDirection = true;

        ImagesToWadRadio.IsChecked =
            _activeDirection ==
            ConversionDirection.ImagesToWad;

        WadToPngRadio.IsChecked =
            _activeDirection ==
            ConversionDirection.WadToPng;

        _restoringDirection = false;
    }

    private void RestoreWadFormatSelection()
    {
        _restoringWadFormat = true;

        Wad2Radio.IsChecked =
            _activeWadFormat ==
            WadFormat.Wad2;

        Wad3Radio.IsChecked =
            _activeWadFormat ==
            WadFormat.Wad3;

        _restoringWadFormat = false;
    }

    private void Window_DragEnter(
        object sender,
        DragEventArgs e)
    {
        if (_conversionRunning ||
            !e.Data.GetDataPresent(
                DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        string[]? paths =
            e.Data.GetData(
                DataFormats.FileDrop)
            as string[];

        e.Effects =
            paths is not null &&
            paths.Length > 0 &&
            ContainsPotentialInput(paths)
                ? DragDropEffects.Copy
                : DragDropEffects.None;

        e.Handled = true;
    }

    private void Window_Drop(
        object sender,
        DragEventArgs e)
    {
        if (_conversionRunning ||
            !e.Data.GetDataPresent(
                DataFormats.FileDrop))
        {
            return;
        }

        string[]? paths =
            e.Data.GetData(
                DataFormats.FileDrop)
            as string[];

        if (paths is null ||
            paths.Length == 0)
        {
            return;
        }

        AddPaths(paths);
    }

    private bool ContainsPotentialInput(
        IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
            {
                return true;
            }

            if (!File.Exists(path))
            {
                continue;
            }

            if (_activeDirection ==
                ConversionDirection.ImagesToWad)
            {
                if (ImageInspector.IsSupportedPath(path))
                {
                    return true;
                }
            }
            else if (
                WadArchiveInspector.IsSupportedPath(path))
            {
                return true;
            }
        }

        return false;
    }

    private void AddPaths(
        IEnumerable<string> inputPaths)
    {
        int addedCount = 0;
        int duplicateCount = 0;
        int unsupportedCount = 0;

        List<string> errors = new();

        string[] expandedPaths =
            ExpandInputPaths(inputPaths)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        foreach (string candidatePath in expandedPaths)
        {
            string? normalizedPath =
                TryNormalizePath(candidatePath);

            if (normalizedPath is null)
            {
                unsupportedCount++;
                continue;
            }

            if (_knownPaths.Contains(
                    normalizedPath))
            {
                duplicateCount++;
                continue;
            }

            bool wasAdded;

            if (_activeDirection ==
                ConversionDirection.ImagesToWad)
            {
                if (!ImageInspector.IsSupportedPath(
                        normalizedPath))
                {
                    unsupportedCount++;
                    continue;
                }

                wasAdded = AddImage(
                    normalizedPath,
                    errors);
            }
            else
            {
                if (!WadArchiveInspector.IsSupportedPath(
                        normalizedPath))
                {
                    unsupportedCount++;
                    continue;
                }

                wasAdded = AddWad(
                    normalizedPath,
                    errors);
            }

            if (wasAdded)
            {
                addedCount++;
            }
        }

        RefreshQueueUi();

        List<string> statusParts = new();

        if (addedCount > 0)
        {
            statusParts.Add(
                $"{addedCount:N0} added");
        }

        if (duplicateCount > 0)
        {
            statusParts.Add(
                $"{duplicateCount:N0} duplicate(s) skipped");
        }

        if (unsupportedCount > 0)
        {
            statusParts.Add(
                $"{unsupportedCount:N0} unsupported item(s) skipped");
        }

        if (errors.Count > 0)
        {
            statusParts.Add(
                $"{errors.Count:N0} invalid item(s)");

            statusParts.Add(
                $"First error: {errors[0]}");
        }

        StatusText.Text =
            statusParts.Count > 0
                ? string.Join(
                    "; ",
                    statusParts) + "."
                : "No compatible files were found.";
    }

    private bool AddImage(
        string path,
        ICollection<string> errors)
    {
        if (!ImageInspector.TryInspect(
                path,
                out ImageInspectionResult? inspection,
                out string error) ||
            inspection is null)
        {
            errors.Add(
                $"{Path.GetFileName(path)}: {error}");

            return false;
        }

        string displayName =
            Path.GetFileNameWithoutExtension(path)
                .Trim();

        if (string.IsNullOrWhiteSpace(
                displayName))
        {
            displayName = "texture";
        }

        string internalName =
            TextureAliasNameGenerator.CreateUnique(
                displayName,
                _reservedInternalNames,
                inspection.HasTransparency);

        string transparencyDescription =
            inspection.HasTransparency
                ? "transparency detected"
                : "opaque";

        string dimensionDescription =
            inspection.Width % 16 == 0 &&
            inspection.Height % 16 == 0
                ? $"{inspection.Width:N0} × " +
                  $"{inspection.Height:N0}"
                : $"{inspection.Width:N0} × " +
                  $"{inspection.Height:N0}, " +
                  "will be edge-padded";

        QueueItems.Add(
            new BatchQueueItem(
                path,
                "Image",
                displayName,
                internalName,
                dimensionDescription + ", " +
                inspection.PixelFormat + ", " +
                transparencyDescription,
                "Ready",
                inspection.HasTransparency));

        _knownPaths.Add(path);
        return true;
    }

    private bool AddWad(
        string path,
        ICollection<string> errors)
    {
        if (!WadArchiveInspector.TryInspect(
                path,
                out WadInspectionResult? inspection,
                out string error) ||
            inspection is null)
        {
            errors.Add(
                $"{Path.GetFileName(path)}: {error}");

            return false;
        }

        string formatName =
            inspection.Format ==
            WadFormat.Wad2
                ? "WAD2"
                : "WAD3";

        QueueItems.Add(
            new BatchQueueItem(
                path,
                formatName,
                Path.GetFileNameWithoutExtension(path),
                string.Empty,
                $"{inspection.LumpCount:N0} lump(s), " +
                $"{inspection.FileSize:N0} bytes",
                "Validated",
                false));

        _knownPaths.Add(path);
        return true;
    }

    private static IEnumerable<string> ExpandInputPaths(
        IEnumerable<string> inputPaths)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip =
                FileAttributes.ReparsePoint |
                FileAttributes.System
        };

        foreach (string inputPath in inputPaths)
        {
            if (File.Exists(inputPath))
            {
                yield return inputPath;
                continue;
            }

            if (!Directory.Exists(inputPath))
            {
                continue;
            }

            IEnumerable<string> files;

            try
            {
                files = Directory.EnumerateFiles(
                    inputPath,
                    "*",
                    options);
            }
            catch
            {
                continue;
            }

            using IEnumerator<string> enumerator =
                files.GetEnumerator();

            while (true)
            {
                string current;

                try
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }

                    current = enumerator.Current;
                }
                catch
                {
                    break;
                }

                yield return current;
            }
        }
    }

    private static string? TryNormalizePath(
        string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private async void ConvertBatch_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_conversionRunning)
        {
            return;
        }

        if (_activeDirection ==
            ConversionDirection.WadToPng)
        {
            await ExtractBatchAsync();
            return;
        }

        if (QueueItems.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(
                _outputFolder))
        {
            MessageBox.Show(
                this,
                "Select an output folder first.",
                "Output Folder Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        WadFormat format =
            _activeWadFormat;

        if (format == WadFormat.Wad2 &&
            string.IsNullOrWhiteSpace(
                _wad2PalettePath))
        {
            MessageBox.Show(
                this,
                "Select a valid WAD2 palette first.",
                "WAD2 Palette Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        string outputFileName =
            NormalizeOutputFileName(
                OutputWadNameTextBox.Text);

        string requestedOutputPath =
            Path.Combine(
                _outputFolder,
                outputFileName);

        string outputPath =
            CreateAvailablePath(
                requestedOutputPath);

        WadTextureInput[] inputs =
            QueueItems
                .Select(
                    item => new WadTextureInput(
                        item.SourcePath,
                        item.DisplayName,
                        item.HasTransparency))
                .ToArray();

        WadConversionOptions options = new(
            format,
            outputPath,
            _wad2PalettePath,
            DitheringCheckBox.IsChecked == true,
            TransparencyCheckBox.IsChecked == true);

        Progress<WadConversionProgress> progress =
            new(
                update =>
                {
                    double percent =
                        update.Total == 0
                            ? 0.0
                            : update.Completed *
                              100.0 /
                              update.Total;

                    ConversionProgressBar.Value =
                        percent;

                    StatusText.Text =
                        $"Converting {update.Completed:N0} " +
                        $"of {update.Total:N0}: " +
                        update.CurrentTextureName;
                });

        _conversionRunning = true;
        SetConversionControlsEnabled(false);

        ConvertButton.Content = "Converting...";
        ConversionProgressBar.Value = 0;

        try
        {
            WadConversionResult result =
                await Task.Run(
                    () => _conversionService.Convert(
                        inputs,
                        options,
                        progress));

            ConversionProgressBar.Value = 100;

            StatusText.Text =
                $"{result.TextureCount:N0} texture(s) " +
                "converted successfully.";

            string paletteLine =
                result.PalettePath is null
                    ? string.Empty
                    : Environment.NewLine +
                      Environment.NewLine +
                      "WAD2 palette copy:" +
                      Environment.NewLine +
                      result.PalettePath;

            MessageBox.Show(
                this,
                "Conversion completed." +
                Environment.NewLine +
                Environment.NewLine +
                "WAD:" +
                Environment.NewLine +
                result.WadPath +
                Environment.NewLine +
                Environment.NewLine +
                "Long-name manifest:" +
                Environment.NewLine +
                result.ManifestPath +
                paletteLine,
                "WadForge Conversion Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            ConversionProgressBar.Value = 0;

            StatusText.Text =
                "Conversion failed.";

            MessageBox.Show(
                this,
                exception.ToString(),
                "WadForge Conversion Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _conversionRunning = false;
            ConvertButton.Content = "Convert Batch";

            SetConversionControlsEnabled(true);
            RefreshQueueUi();
        }
    }

    private static string NormalizeOutputFileName(
        string requestedName)
    {
        string name =
            requestedName.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "textures.wad";
        }

        foreach (char invalidCharacter in
                 Path.GetInvalidFileNameChars())
        {
            name = name.Replace(
                invalidCharacter,
                '_');
        }

        if (!name.EndsWith(
                ".wad",
                StringComparison.OrdinalIgnoreCase))
        {
            name += ".wad";
        }

        if (string.Equals(
                name,
                ".wad",
                StringComparison.OrdinalIgnoreCase))
        {
            name = "textures.wad";
        }

        return name;
    }

    private static string CreateAvailablePath(
        string requestedPath)
    {
        if (!File.Exists(requestedPath))
        {
            return requestedPath;
        }

        string? directory =
            Path.GetDirectoryName(
                requestedPath);

        string baseName =
            Path.GetFileNameWithoutExtension(
                requestedPath);

        string extension =
            Path.GetExtension(
                requestedPath);

        if (string.IsNullOrWhiteSpace(
                directory))
        {
            throw new InvalidOperationException(
                "The output directory is invalid.");
        }

        for (int suffix = 2;
             suffix < int.MaxValue;
             suffix++)
        {
            string candidate = Path.Combine(
                directory,
                $"{baseName}-{suffix}{extension}");

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException(
            "No available output filename could be generated.");
    }

    private void ClearQueueInternal()
    {
        QueueItems.Clear();
        _knownPaths.Clear();
        _reservedInternalNames.Clear();
        QueueGrid.SelectedItems.Clear();

        RefreshQueueUi();
    }

    private void UpdateModeUi()
    {
        bool imagesToWad =
            _activeDirection ==
            ConversionDirection.ImagesToWad;

        WadFormatPanel.Visibility =
            imagesToWad
                ? Visibility.Visible
                : Visibility.Collapsed;

        ColorProcessingPanel.Visibility =
            imagesToWad
                ? Visibility.Visible
                : Visibility.Collapsed;

        OutputWadNamePanel.Visibility =
            Visibility.Visible;

        OutputWadNameLabel.Visibility =
            imagesToWad
                ? Visibility.Visible
                : Visibility.Collapsed;

        OutputWadNameTextBox.Visibility =
            imagesToWad
                ? Visibility.Visible
                : Visibility.Collapsed;

        OutputWadNameHelpText.Visibility =
            imagesToWad
                ? Visibility.Visible
                : Visibility.Collapsed;

        ExtractionOutputFolderHelpText.Visibility =
            imagesToWad
                ? Visibility.Collapsed
                : Visibility.Visible;

        if (!_conversionRunning)
        {
            ConvertButton.Content =
                imagesToWad
                    ? "Convert Batch"
                    : "Extract PNGs";
        }

        AddImagesButton.IsEnabled =
            imagesToWad &&
            !_conversionRunning;

        AddWadFilesButton.IsEnabled =
            !imagesToWad &&
            !_conversionRunning;

        WadFormatPanel.IsEnabled =
            imagesToWad;

        WadFormatPanel.Opacity =
            imagesToWad
                ? 1.0
                : 0.45;

        DitheringCheckBox.IsEnabled =
            imagesToWad;

        TransparencyCheckBox.IsEnabled =
            imagesToWad;

        InternalNameColumn.Visibility =
            imagesToWad
                ? Visibility.Visible
                : Visibility.Collapsed;

        FullDisplayNameColumn.Header =
            imagesToWad
                ? "Full display name"
                : "WAD archive name";

        DropInstructionText.Text =
            imagesToWad
                ? "PNG, JPG, JPEG, BMP, GIF and TIFF images are accepted."
                : "WAD2 and WAD3 archives are accepted.";

        UpdateWadFormatUi();
        RefreshConvertAvailability();
    }

    private void UpdateWadFormatUi()
    {
        bool imageConversionNeedsPalette =
            _activeDirection ==
            ConversionDirection.ImagesToWad &&
            _activeWadFormat ==
            WadFormat.Wad2;

        bool extractionContainsWad2 =
            _activeDirection ==
            ConversionDirection.WadToPng &&
            QueueItems.Any(
                item => string.Equals(
                    item.ItemType,
                    "WAD2",
                    StringComparison.OrdinalIgnoreCase));

        Wad2PalettePanel.Visibility =
            imageConversionNeedsPalette ||
            extractionContainsWad2
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void SetConversionControlsEnabled(
        bool enabled)
    {
        AddImagesButton.IsEnabled =
            enabled &&
            _activeDirection ==
            ConversionDirection.ImagesToWad;

        AddWadFilesButton.IsEnabled =
            enabled &&
            _activeDirection ==
            ConversionDirection.WadToPng;

        RemoveSelectedButton.IsEnabled =
            enabled &&
            QueueGrid.SelectedItems.Count > 0;

        ClearQueueButton.IsEnabled =
            enabled &&
            QueueItems.Count > 0;

        ImagesToWadRadio.IsEnabled = enabled;
        WadToPngRadio.IsEnabled = enabled;
        Wad2Radio.IsEnabled = enabled;
        Wad3Radio.IsEnabled = enabled;
        DitheringCheckBox.IsEnabled = enabled;
        TransparencyCheckBox.IsEnabled = enabled;
        OutputWadNameTextBox.IsEnabled = enabled;
    }

    private void RefreshQueueUi()
    {
        int count = QueueItems.Count;

        QueueCountText.Text =
            count == 1
                ? "1 item"
                : $"{count:N0} items";

        EmptyQueuePanel.Visibility =
            count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        ClearQueueButton.IsEnabled =
            !_conversionRunning &&
            count > 0;

        RemoveSelectedButton.IsEnabled =
            !_conversionRunning &&
            QueueGrid.SelectedItems.Count > 0;

        UpdateWadFormatUi();
        RefreshConvertAvailability();
    }

    private void RefreshConvertAvailability()
    {
        if (!IsLoaded)
        {
            return;
        }

        bool queueReady =
            QueueItems.Count > 0;

        bool outputFolderReady =
            !string.IsNullOrWhiteSpace(
                _outputFolder);

        bool ready;

        if (_activeDirection ==
            ConversionDirection.ImagesToWad)
        {
            bool outputNameValid =
                !string.IsNullOrWhiteSpace(
                    OutputWadNameTextBox.Text);

            bool wad2PaletteReady =
                _activeWadFormat != WadFormat.Wad2 ||
                !string.IsNullOrWhiteSpace(
                    _wad2PalettePath);

            ready =
                !_conversionRunning &&
                queueReady &&
                outputFolderReady &&
                outputNameValid &&
                wad2PaletteReady;
        }
        else
        {
            bool containsWad2 =
                QueueItems.Any(
                    item => string.Equals(
                        item.ItemType,
                        "WAD2",
                        StringComparison.OrdinalIgnoreCase));

            bool extractionPaletteReady =
                !containsWad2 ||
                !string.IsNullOrWhiteSpace(
                    _wad2PalettePath);

            ready =
                !_conversionRunning &&
                queueReady &&
                outputFolderReady &&
                extractionPaletteReady;
        }

        ConvertButton.IsEnabled =
            ready;

        if (!_conversionRunning)
        {
            ConvertButton.Content =
                _activeDirection ==
                ConversionDirection.ImagesToWad
                    ? "Convert Batch"
                    : "Extract PNGs";
        }
    }
}
