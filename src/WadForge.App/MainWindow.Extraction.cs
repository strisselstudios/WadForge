using System.IO;
using System.Windows;
using WadForge.Wad;

namespace WadForge.App;

public partial class MainWindow
{
    private readonly WadExtractionService _extractionService =
        new();

    private async Task ExtractBatchAsync()
    {
        if (_conversionRunning)
        {
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

        bool containsWad2 =
            QueueItems.Any(
                item => string.Equals(
                    item.ItemType,
                    "WAD2",
                    StringComparison.OrdinalIgnoreCase));

        if (containsWad2 &&
            string.IsNullOrWhiteSpace(
                _wad2PalettePath))
        {
            MessageBox.Show(
                this,
                "This batch contains at least one WAD2 archive. Select the correct WAD2 palette before extracting.",
                "WAD2 Palette Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        WadExtractionInput[] inputs =
            QueueItems
                .Select(
                    item => new WadExtractionInput(
                        item.SourcePath))
                .ToArray();

        WadExtractionOptions options = new(
            _outputFolder,
            _wad2PalettePath,
            TransparencyCheckBox.IsChecked == true,
            true);

        Progress<WadExtractionProgress> progress = new(
            update =>
            {
                double percent =
                    update.TotalWads == 0
                        ? 0.0
                        : update.CompletedWads *
                          100.0 /
                          update.TotalWads;

                ConversionProgressBar.Value =
                    percent;

                StatusText.Text =
                    $"Extracting WAD {Math.Min(update.CompletedWads + 1, update.TotalWads):N0} " +
                    $"of {update.TotalWads:N0}: " +
                    update.CurrentItem;
            });

        _conversionRunning = true;

        SetConversionControlsEnabled(false);

        ConvertButton.IsEnabled = false;
        ConvertButton.Content = "Extracting...";
        ConversionProgressBar.Value = 0;

        try
        {
            WadExtractionResult result =
                await Task.Run(
                    () => _extractionService.Extract(
                        inputs,
                        options,
                        progress));

            ConversionProgressBar.Value = 100;

            StatusText.Text =
                $"{result.TextureCount:N0} texture(s) extracted from " +
                $"{result.WadCount:N0} WAD archive(s).";

            string displayedDirectories =
                string.Join(
                    Environment.NewLine,
                    result.OutputDirectories.Take(10));

            string additionalDirectories =
                result.OutputDirectories.Count > 10
                    ? Environment.NewLine +
                      $"...and {result.OutputDirectories.Count - 10:N0} more output folder(s)."
                    : string.Empty;

            string warningText =
                result.Warnings.Count == 0
                    ? string.Empty
                    : Environment.NewLine +
                      Environment.NewLine +
                      "Warnings:" +
                      Environment.NewLine +
                      string.Join(
                          Environment.NewLine,
                          result.Warnings.Take(10));

            MessageBox.Show(
                this,
                "Extraction completed." +
                Environment.NewLine +
                Environment.NewLine +
                $"WAD archives: {result.WadCount:N0}" +
                Environment.NewLine +
                $"PNG textures: {result.TextureCount:N0}" +
                Environment.NewLine +
                $"Long names restored: {result.RestoredAliasCount:N0}" +
                Environment.NewLine +
                Environment.NewLine +
                "Output folders:" +
                Environment.NewLine +
                displayedDirectories +
                additionalDirectories +
                warningText,
                "WadForge Extraction Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            ConversionProgressBar.Value = 0;

            StatusText.Text =
                "WAD extraction failed.";

            MessageBox.Show(
                this,
                exception.ToString(),
                "WadForge Extraction Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _conversionRunning = false;

            ConvertButton.Content =
                "Extract PNGs";

            SetConversionControlsEnabled(true);
            RefreshQueueUi();
        }
    }
}
