using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TrenchBroom.Companion.Core;

namespace TrenchBroom.Companion.App;

public partial class CompanionOnlineWadBrowserWindow :
    Window
{
    private readonly string _managedDataRoot;

    private readonly CompanionWadLibraryService
        _wadLibraryService;

    private readonly CompanionPaletteLibraryService
        _paletteLibraryService;

    private readonly string? _activeGameId;

    private readonly string? _activeGameInstallationDirectory;

    private readonly string? _activeDuskPalettePath;

    private readonly IReadOnlyList<string>
        _quakeInstallations;

    private readonly IReadOnlyList<ICompanionOnlineWadRepository>
        _repositories;

    private readonly ObservableCollection<CompanionOnlineWadEntry>
        _visibleEntries =
            new();

    private IReadOnlyList<CompanionOnlineWadEntry>
        _allEntries =
            Array.Empty<CompanionOnlineWadEntry>();

    private CancellationTokenSource?
        _catalogCancellation;

    private bool _busy;

    public CompanionOnlineWadBrowserWindow(
        string managedDataRoot,
        CompanionWadLibraryService wadLibraryService,
        CompanionPaletteLibraryService paletteLibraryService,
        string? activeGameId,
        string? activeGameInstallationDirectory,
        string? activeDuskPalettePath,
        IReadOnlyList<string> quakeInstallations)
    {
        InitializeComponent();

        _managedDataRoot =
            string.IsNullOrWhiteSpace(
                managedDataRoot)
                ? throw new ArgumentException(
                    "A Companion managed data root is required.",
                    nameof(managedDataRoot))
                : Path.GetFullPath(
                    managedDataRoot);

        _wadLibraryService =
            wadLibraryService ??
            throw new ArgumentNullException(
                nameof(wadLibraryService));

        _paletteLibraryService =
            paletteLibraryService ??
            throw new ArgumentNullException(
                nameof(paletteLibraryService));

        _activeGameId =
            activeGameId;

        _activeGameInstallationDirectory =
            activeGameInstallationDirectory;

        _activeDuskPalettePath =
            activeDuskPalettePath;

        _quakeInstallations =
            quakeInstallations ??
            Array.Empty<string>();

        _repositories =
            CompanionOnlineWadRepositories.CreateDefault();

        RepositoryListBox.ItemsSource =
            _repositories;

        OnlineWadGrid.ItemsSource =
            _visibleEntries;
    }

    public int ImportedCount { get; private set; }

    private void Window_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_repositories.Count >
            0)
        {
            RepositoryListBox.SelectedIndex =
                0;
        }
    }

    private void Window_Closed(
        object? sender,
        EventArgs e)
    {
        _catalogCancellation?.Cancel();
        _catalogCancellation?.Dispose();

        _catalogCancellation =
            null;
    }

    private async void RepositoryListBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (RepositoryListBox.SelectedItem is not
            ICompanionOnlineWadRepository repository)
        {
            return;
        }

        await LoadRepositoryAsync(
            repository);
    }

    private async Task LoadRepositoryAsync(
        ICompanionOnlineWadRepository repository)
    {
        _catalogCancellation?.Cancel();
        _catalogCancellation?.Dispose();

        _catalogCancellation =
            new CancellationTokenSource();

        CancellationToken cancellationToken =
            _catalogCancellation.Token;

        RepositoryTitleText.Text =
            repository.DisplayName;

        RepositoryDescriptionText.Text =
            repository.Description;

        _allEntries =
            Array.Empty<CompanionOnlineWadEntry>();

        _visibleEntries.Clear();

        OnlineCountText.Text =
            "Loading...";

        OnlineStatusText.Text =
            $"Loading {repository.DisplayName}...";

        SetBusy(
            true);

        try
        {
            IReadOnlyList<CompanionOnlineWadEntry> entries =
                await repository.GetEntriesAsync(
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            _allEntries =
                entries;

            ApplySearchFilter();

            OnlineStatusText.Text =
                entries.Count ==
                    0
                    ? $"{repository.DisplayName} returned no direct WAD downloads."
                    : $"Loaded {entries.Count:N0} WAD(s) from {repository.DisplayName}.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _allEntries =
                Array.Empty<CompanionOnlineWadEntry>();

            _visibleEntries.Clear();

            OnlineCountText.Text =
                "0 WADs";

            OnlineStatusText.Text =
                "Could not load this source: " +
                exception.Message;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SetBusy(
                    false);
            }
        }
    }

    private void OnlineSearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        string search =
            OnlineSearchTextBox.Text.Trim();

        IEnumerable<CompanionOnlineWadEntry> filtered =
            _allEntries;

        if (search.Length >
            0)
        {
            filtered =
                filtered.Where(
                    entry =>
                        entry.DisplayName.Contains(
                            search,
                            StringComparison.OrdinalIgnoreCase) ||
                        entry.FileName.Contains(
                            search,
                            StringComparison.OrdinalIgnoreCase));
        }

        CompanionOnlineWadEntry[] visible =
            filtered
                .OrderBy(
                    entry =>
                        entry.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    entry =>
                        entry.FileName,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        _visibleEntries.Clear();

        foreach (CompanionOnlineWadEntry entry in
                 visible)
        {
            _visibleEntries.Add(
                entry);
        }

        OnlineCountText.Text =
            _allEntries.Count ==
                visible.Length
                ? $"{visible.Length:N0} WADs"
                : $"{visible.Length:N0} of {_allEntries.Count:N0} WADs";

        RefreshSelectionButtons();
    }

    private void OnlineWadGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        RefreshSelectionButtons();
    }

    private async void OnlineWadGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (OnlineWadGrid.SelectedItem is not
            CompanionOnlineWadEntry)
        {
            return;
        }

        await PreviewSelectedAsync();
    }

    private void RefreshSelectionButtons()
    {
        bool selected =
            OnlineWadGrid.SelectedItem is
            CompanionOnlineWadEntry;

        PreviewOnlineWadButton.IsEnabled =
            selected &&
            !_busy;

        ImportOnlineWadButton.IsEnabled =
            selected &&
            !_busy;
    }

    private void OpenSourceButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (RepositoryListBox.SelectedItem is not
            ICompanionOnlineWadRepository repository)
        {
            return;
        }

        Process.Start(
            new ProcessStartInfo
            {
                FileName =
                    repository.CatalogUri.AbsoluteUri,
                UseShellExecute =
                    true
            });
    }

    private async void PreviewOnlineWadButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await PreviewSelectedAsync();
    }

    private async Task PreviewSelectedAsync()
    {
        if (OnlineWadGrid.SelectedItem is not
            CompanionOnlineWadEntry entry)
        {
            return;
        }

        string? temporaryPath =
            null;

        SetBusy(
            true);

        OnlineStatusText.Text =
            $"Downloading {entry.FileName} for preview...";

        try
        {
            temporaryPath =
                await CompanionOnlineWadDownloadService.DownloadTemporaryAsync(
                    entry,
                    _managedDataRoot,
                    CancellationToken.None);

            WadRegistrationResult inspection =
                WadRegistrationService.Inspect(
                    temporaryPath);

            if (!inspection.WadIsValid)
            {
                throw new InvalidDataException(
                    $"The downloaded file is not a valid WAD2/WAD3 archive. {inspection.Validation}");
            }

            CompanionPaletteResolution paletteResolution =
                PreparePaletteResolution(
                    entry,
                    inspection);

            CompanionWadBrowserWindow dialog =
                new(
                    inspection,
                    _managedDataRoot,
                    paletteResolution)
                {
                    Owner =
                        this
                };

            OnlineStatusText.Text =
                $"Previewing {entry.FileName}. Companion detected {inspection.WadFormat}.";

            dialog.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Online WAD Preview",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            OnlineStatusText.Text =
                "Preview failed.";
        }
        finally
        {
            CompanionOnlineWadDownloadService.DeleteTemporaryDownload(
                temporaryPath);

            SetBusy(
                false);
        }
    }

    private async void ImportOnlineWadButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (OnlineWadGrid.SelectedItem is not
            CompanionOnlineWadEntry entry)
        {
            return;
        }

        string? temporaryPath =
            null;

        SetBusy(
            true);

        OnlineStatusText.Text =
            $"Downloading {entry.FileName}...";

        try
        {
            temporaryPath =
                await CompanionOnlineWadDownloadService.DownloadTemporaryAsync(
                    entry,
                    _managedDataRoot,
                    CancellationToken.None);

            WadRegistrationResult inspection =
                WadRegistrationService.Inspect(
                    temporaryPath);

            if (!inspection.WadIsValid)
            {
                throw new InvalidDataException(
                    $"The downloaded file is not a valid WAD2/WAD3 archive. {inspection.Validation}");
            }

            CompanionWadLibraryImportResult result =
                _wadLibraryService.Import(
                    _managedDataRoot,
                    temporaryPath);

            RememberSourcePalette(
                entry,
                result);

            ImportedCount++;

            OnlineStatusText.Text =
                result.CopiedIntoLibrary
                    ? $"Added {entry.FileName} to the global {result.WadFormat} library."
                    : $"{entry.FileName} already exists in the global {result.WadFormat} library. Existing asset reused.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Online WAD Import",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            OnlineStatusText.Text =
                "Import failed.";
        }
        finally
        {
            CompanionOnlineWadDownloadService.DeleteTemporaryDownload(
                temporaryPath);

            SetBusy(
                false);
        }
    }

    private CompanionPaletteResolution PreparePaletteResolution(
        CompanionOnlineWadEntry entry,
        WadRegistrationResult inspection)
    {
        if (string.Equals(
                inspection.WadFormat,
                "WAD2",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                entry.PaletteHint,
                "Quake",
                StringComparison.OrdinalIgnoreCase))
        {
            string wadSha256 =
                ComputeSha256(
                    inspection.WadPath);

            CompanionPaletteLibraryAsset? quakePalette =
                FindOrImportQuakePalette();

            if (quakePalette is not
                null)
            {
                _paletteLibraryService.SetWadAssociation(
                    _managedDataRoot,
                    wadSha256,
                    quakePalette.AssetId);

                return new CompanionPaletteResolution(
                    wadSha256,
                    quakePalette.AssetId,
                    quakePalette.PalettePath,
                    "Quake palette from source provenance",
                    true);
            }

            return new CompanionPaletteResolution(
                wadSha256,
                null,
                null,
                "Quake palette expected but not available - neutral preview",
                false);
        }

        return _paletteLibraryService.PrepareForWad(
            _managedDataRoot,
            inspection.WadPath,
            inspection.ManifestPath,
            _activeGameId,
            _activeGameInstallationDirectory,
            _activeDuskPalettePath,
            _quakeInstallations);
    }

    private void RememberSourcePalette(
        CompanionOnlineWadEntry entry,
        CompanionWadLibraryImportResult result)
    {
        if (!string.Equals(
                result.WadFormat,
                "WAD2",
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                entry.PaletteHint,
                "Quake",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CompanionPaletteLibraryAsset? quakePalette =
            FindOrImportQuakePalette();

        if (quakePalette is
            null)
        {
            return;
        }

        _paletteLibraryService.SetWadAssociation(
            _managedDataRoot,
            result.Sha256,
            quakePalette.AssetId);
    }

    private CompanionPaletteLibraryAsset? FindOrImportQuakePalette()
    {
        CompanionPaletteLibraryAsset? existing =
            _paletteLibraryService
                .GetAssets(
                    _managedDataRoot)
                .FirstOrDefault(
                    asset =>
                        string.Equals(
                            asset.DisplayName,
                            "Quake",
                            StringComparison.OrdinalIgnoreCase));

        if (existing is not
            null)
        {
            return existing;
        }

        foreach (string installation in
                 _quakeInstallations)
        {
            CompanionPaletteLibraryAsset? imported =
                _paletteLibraryService.EnsureQuakePalette(
                    _managedDataRoot,
                    installation);

            if (imported is not
                null)
            {
                return imported;
            }
        }

        return null;
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

    private void SetBusy(
        bool busy)
    {
        _busy =
            busy;

        RepositoryListBox.IsEnabled =
            !busy;

        OnlineSearchTextBox.IsEnabled =
            !busy;

        OpenSourceButton.IsEnabled =
            !busy &&
            RepositoryListBox.SelectedItem is
                ICompanionOnlineWadRepository;

        RefreshSelectionButtons();
    }
}