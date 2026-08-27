using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TrenchBroom.Companion.Core;

namespace TrenchBroom.Companion.App;

public partial class MainWindow : Window
{
    private readonly CompanionSettings _settings;
    private TrenchBroomInstallationInfo? _installation;
    private Button? _installationActionButton;

    private readonly CompanionWadLibraryService
        _wadLibraryService =
            new();

    private readonly CompanionPaletteLibraryService
        _paletteLibraryService =
            new();

    private ICollectionView? _wadLibraryView;

    private string _wadLibraryFormat =
        "WAD2";

    public MainWindow()
    {
        InitializeComponent();

        RegisteredWads =
            new ObservableCollection<WadRegistrationResult>();

        _wadLibraryView =
            CollectionViewSource.GetDefaultView(
                RegisteredWads);

        _wadLibraryView.Filter =
            FilterWadLibraryItem;

        DataContext = this;

        UpdateWadLibraryTabPresentation();
        ConfigureTrenchBroomPresentation();

        try
        {
            _settings =
                CompanionSettingsStore.Load();
        }
        catch (Exception exception)
        {
            _settings =
                new CompanionSettings();

            StatusText.Text =
                "Existing settings could not be read: " +
                exception.Message;
        }

        if (CompanionManagedDataRootService
            .TryInitializeFromExistingWorkspace(
                _settings))
        {
            SaveSettings();
        }

        LoadSavedState();
        ResolveTrenchBroomInstallation();
        RefreshInterface();
    }

    public ObservableCollection<WadRegistrationResult>
        RegisteredWads { get; }

    public ICollectionView? WadLibraryView =>
        _wadLibraryView;

    private void LoadSavedState()
    {
        if (!string.IsNullOrWhiteSpace(
                _settings.TrenchBroomExecutablePath))
        {
            _installation =
                TrenchBroomInstallationService.Inspect(
                    _settings.TrenchBroomExecutablePath);
        }

        HashSet<string> loadedPaths =
            new(
                StringComparer.OrdinalIgnoreCase);

        foreach (string wadPath in
                 _settings.RegisteredWadPaths)
        {
            string normalizedPath;

            try
            {
                normalizedPath =
                    Path.GetFullPath(
                        wadPath);
            }
            catch
            {
                normalizedPath =
                    wadPath;
            }

            if (!loadedPaths.Add(
                    normalizedPath))
            {
                continue;
            }

            RegisteredWads.Add(
                WadRegistrationService.Inspect(
                    normalizedPath));
        }

        if (CompanionManagedDataRootService
            .TryGetConfiguredRoot(
                _settings,
                out string managedDataRoot))
        {
            bool changed =
                SynchronizeRegisteredWadsWithLibrary(
                    managedDataRoot,
                    adoptExternalWads: true);

            if (changed)
            {
                SaveSettings();
            }
        }
    }

    private void ResolveTrenchBroomInstallation()
    {
        bool hasManagedRoot =
            CompanionManagedDataRootService
                .TryGetConfiguredRoot(
                    _settings,
                    out string managedDataRoot);

        string managedExecutablePath =
            hasManagedRoot
                ? CompanionManagedDataRootService
                    .GetTrenchBroomExecutablePath(
                        managedDataRoot)
                : string.Empty;

        TrenchBroomInstallationResolution resolution =
            TrenchBroomInstallationResolver.Resolve(
                _settings.TrenchBroomExecutablePath,
                AppContext.BaseDirectory,
                managedExecutablePath,
                TrenchBroomInstallationResolver
                    .EnumerateDefaultDiscoveryCandidates());

        if (hasManagedRoot &&
            resolution.Installation is not null &&
            resolution.Installation.IsValid &&
            resolution.Installation.IsWadForgeCompatible)
        {
            if (TryUseManagedTrenchBroom(
                    resolution.Installation,
                    resolution.Source,
                    showErrorDialog: false))
            {
                return;
            }
        }

        _installation =
            resolution.Installation;

        if (_installation is not null &&
            _installation.IsValid &&
            !string.Equals(
                _settings.TrenchBroomExecutablePath,
                _installation.ExecutablePath,
                StringComparison.OrdinalIgnoreCase))
        {
            _settings.TrenchBroomExecutablePath =
                _installation.ExecutablePath;

            SaveSettings();
        }

        if (!hasManagedRoot &&
            _installation is not null &&
            _installation.IsValid &&
            _installation.IsWadForgeCompatible)
        {
            StatusText.Text =
                "Compatible TrenchBroom found. Create or open a project to choose Companion's managed data drive.";

            return;
        }

        if (!string.IsNullOrWhiteSpace(
                resolution.Status))
        {
            StatusText.Text =
                resolution.Status;
        }
    }

    private bool TryUseManagedTrenchBroom(
        TrenchBroomInstallationInfo compatibleInstallation,
        string source,
        bool showErrorDialog)
    {
        try
        {
            string sourceExecutablePath =
                Path.GetFullPath(
                    compatibleInstallation.ExecutablePath);

            string managedDataRoot =
                CompanionManagedDataRootService
                    .GetRequiredRoot(
                        _settings);

            string managedExecutablePath =
                Path.GetFullPath(
                    CompanionManagedDataRootService
                        .GetTrenchBroomExecutablePath(
                            managedDataRoot));

            bool alreadyManaged =
                string.Equals(
                    sourceExecutablePath,
                    managedExecutablePath,
                    StringComparison.OrdinalIgnoreCase);

            TrenchBroomManagedInstallationResult result =
                TrenchBroomManagedInstallationService.Provision(
                    sourceExecutablePath,
                    managedExecutablePath);

            _installation =
                result.Installation;

            _settings.TrenchBroomExecutablePath =
                result.Installation.ExecutablePath;

            SaveSettings();

            if (alreadyManaged)
            {
                StatusText.Text =
                    "Companion-managed TrenchBroom is ready.";
            }
            else
            {
                StatusText.Text =
                    source switch
                    {
                        TrenchBroomInstallationResolver.BundledSource =>
                            "Bundled compatible TrenchBroom was installed into Companion-managed storage.",

                        TrenchBroomInstallationResolver.DiscoveredSource =>
                            "An existing compatible TrenchBroom was detected and copied into Companion-managed storage.",

                        _ =>
                            "Your compatible TrenchBroom was copied into Companion-managed storage."
                    };
            }

            return true;
        }
        catch (Exception exception)
        {
            _installation =
                compatibleInstallation;

            _settings.TrenchBroomExecutablePath =
                compatibleInstallation.ExecutablePath;

            SaveSettings();

            StatusText.Text =
                "A compatible TrenchBroom was found, but Companion could not create its managed copy. " +
                "The original installation is still selected.";

            if (showErrorDialog)
            {
                MessageBox.Show(
                    this,
                    "Companion could not create its managed TrenchBroom installation." +
                    Environment.NewLine +
                    Environment.NewLine +
                    exception.Message +
                    Environment.NewLine +
                    Environment.NewLine +
                    "The original TrenchBroom installation was not changed and remains selected.",
                    "Managed TrenchBroom Setup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }
    }
    private void ConfigureTrenchBroomPresentation()
    {
        InstallationPathTextBox.Visibility =
            Visibility.Collapsed;

        InstallationPathTextBox.IsTabStop =
            false;

        if (InstallationPathTextBox.Parent is not
            Grid installationGrid)
        {
            return;
        }

        if (installationGrid.RowDefinitions.Count >= 3)
        {
            installationGrid.RowDefinitions[1].Height =
                new GridLength(0);

            installationGrid.RowDefinitions[2].Height =
                new GridLength(0);
        }

        foreach (UIElement child in
                 installationGrid.Children)
        {
            if (child is not Grid headerGrid ||
                Grid.GetRow(headerGrid) != 0)
            {
                continue;
            }

            foreach (UIElement headerChild in
                     headerGrid.Children)
            {
                if (headerChild is Button button)
                {
                    _installationActionButton =
                        button;

                    continue;
                }

                if (headerChild is not
                    StackPanel informationPanel)
                {
                    continue;
                }

                foreach (UIElement informationChild in
                         informationPanel.Children)
                {
                    if (informationChild is
                        TextBlock heading &&
                        string.Equals(
                            heading.Text,
                            "TrenchBroom installation",
                            StringComparison.Ordinal))
                    {
                        heading.Text =
                            "TrenchBroom";
                    }
                }
            }
        }
    }

    private bool IsManagedTrenchBroom()
    {
        if (_installation is null ||
            !_installation.IsValid)
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(
                    _installation.ExecutablePath),
                Path.GetFullPath(
                    CompanionManagedDataRootService
                        .GetTrenchBroomExecutablePath(
                            CompanionManagedDataRootService
                                .GetRequiredRoot(
                                    _settings))),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void SetInstallationActionText(
        string text)
    {
        if (_installationActionButton is not null)
        {
            _installationActionButton.Content =
                text;
        }
    }

    private void SelectInstallation_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_installation is null ||
            !_installation.IsValid ||
            !_installation.IsWadForgeCompatible)
        {
            ResolveTrenchBroomInstallation();
            RefreshInterface();

            if (_installation is not null &&
                _installation.IsValid &&
                _installation.IsWadForgeCompatible)
            {
                StatusText.Text =
                    "Companion-compatible TrenchBroom is ready.";

                return;
            }
        }

        OpenFileDialog dialog = new()
        {
            Title = "Select TrenchBroom.exe",
            Filter =
                "TrenchBroom executables|*TrenchBroom*.exe|" +
                "Windows executables|*.exe|" +
                "All files|*.*",
            Multiselect = false,
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (_installation is not null &&
            File.Exists(_installation.ExecutablePath))
        {
            dialog.InitialDirectory =
                Path.GetDirectoryName(
                    _installation.ExecutablePath);
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        TrenchBroomInstallationInfo inspection =
            TrenchBroomInstallationService.Inspect(
                dialog.FileName);

        if (!inspection.IsValid)
        {
            _installation =
                inspection;

            StatusText.Text =
                inspection.Status;

            MessageBox.Show(
                this,
                inspection.Status,
                "Invalid TrenchBroom Installation",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            RefreshInterface();
            return;
        }

        if (inspection.IsWadForgeCompatible)
        {
            if (!CompanionManagedDataRootService
                    .TryGetConfiguredRoot(
                        _settings,
                        out _))
            {
                _installation =
                    inspection;

                _settings.TrenchBroomExecutablePath =
                    inspection.ExecutablePath;

                SaveSettings();

                StatusText.Text =
                    "Compatible TrenchBroom selected. It will be copied into Companion-managed storage after a project drive is chosen.";

                RefreshInterface();
                return;
            }

            TryUseManagedTrenchBroom(
                inspection,
                "selected",
                showErrorDialog: true);

            RefreshInterface();
            return;
        }

        _installation =
            inspection;

        _settings.TrenchBroomExecutablePath =
            inspection.ExecutablePath;

        SaveSettings();

        StatusText.Text =
            "Standard TrenchBroom selected as a limited fallback. " +
            "Companion did not modify it.";

        MessageBox.Show(
            this,
            "This is a standard TrenchBroom build." +
            Environment.NewLine +
            Environment.NewLine +
            "Companion will not patch or overwrite it. " +
            "Use the compatible TrenchBroom build included with the full Companion suite " +
            "for long texture aliases and full Companion support.",
            "Compatible TrenchBroom Recommended",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        RefreshInterface();
    }

    private bool TryEnsureWadLibraryRoot(
        out string managedDataRoot)
    {
        if (CompanionManagedDataRootService
            .TryGetConfiguredRoot(
                _settings,
                out managedDataRoot))
        {
            return true;
        }

        OpenFolderDialog dialog =
            new()
            {
                Title =
                    "Choose a drive for Companion assets and projects",

                Multiselect =
                    false
            };

        if (dialog.ShowDialog(
                this) != true)
        {
            managedDataRoot =
                string.Empty;

            StatusText.Text =
                "WAD import canceled because Companion storage has not been chosen yet.";

            return false;
        }

        string selectedRoot =
            CompanionManagedDataRootService
                .GetDataRootForDrive(
                    dialog.FolderName);

        _settings.ManagedDataRootPath =
            selectedRoot;

        if (!CompanionManagedDataRootService
                .TryGetConfiguredRoot(
                    _settings,
                    out managedDataRoot))
        {
            _settings.ManagedDataRootPath =
                null;

            throw new InvalidOperationException(
                "Companion could not initialize managed asset storage on the selected drive.");
        }

        SaveSettings();

        StatusText.Text =
            $"Companion reusable assets will be stored under '{managedDataRoot}'.";

        return true;
    }

    private bool SynchronizeRegisteredWadsWithLibrary(
        string managedDataRoot,
        bool adoptExternalWads)
    {
        HashSet<string> previousPaths =
            RegisteredWads
                .Select(
                    item =>
                        item.WadPath)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        List<WadRegistrationResult> retainedExternal =
            new();

        foreach (WadRegistrationResult item in
                 RegisteredWads.ToArray())
        {
            if (_wadLibraryService.IsLibraryWad(
                    managedDataRoot,
                    item.WadPath))
            {
                continue;
            }

            if (!adoptExternalWads ||
                !File.Exists(
                    item.WadPath) ||
                !item.WadIsValid)
            {
                retainedExternal.Add(
                    item);

                continue;
            }

            try
            {
                _wadLibraryService.Import(
                    managedDataRoot,
                    item.WadPath);
            }
            catch
            {
                retainedExternal.Add(
                    item);
            }
        }

        RegisteredWads.Clear();

        HashSet<string> loaded =
            new(
                StringComparer.OrdinalIgnoreCase);

        foreach (string libraryWadPath in
                 _wadLibraryService.GetWadPaths(
                     managedDataRoot))
        {
            if (!loaded.Add(
                    libraryWadPath))
            {
                continue;
            }

            RegisteredWads.Add(
                WadRegistrationService.Inspect(
                    libraryWadPath));
        }

        foreach (WadRegistrationResult retained in
                 retainedExternal)
        {
            string normalizedPath;

            try
            {
                normalizedPath =
                    Path.GetFullPath(
                        retained.WadPath);
            }
            catch
            {
                normalizedPath =
                    retained.WadPath;
            }

            if (!loaded.Add(
                    normalizedPath))
            {
                continue;
            }

            RegisteredWads.Add(
                retained);
        }

        HashSet<string> currentPaths =
            RegisteredWads
                .Select(
                    item =>
                        item.WadPath)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        return !previousPaths.SetEquals(
            currentPaths);
    }

    private void AddWads_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Import WAD2 or WAD3 archives into the Companion library",
            Filter =
                "WAD archives|*.wad|" +
                "All files|*.*",
            Multiselect = true,
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        AddWadPaths(
            dialog.FileNames,
            "file picker");
    }

    private void FindOnlineWads_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryEnsureWadLibraryRoot(
                out string managedDataRoot))
        {
            return;
        }

        string? activeGameId =
            _projectSession?.Project.GameId;

        string? duskPalettePath =
            TryGetActiveDuskPalettePath();

        IReadOnlyList<string> quakeInstallations =
            _gameInstallationLocator.FindInstallations(
                CompanionGameProfiles.Quake);

        CompanionOnlineWadBrowserWindow dialog =
            new(
                managedDataRoot,
                _wadLibraryService,
                _paletteLibraryService,
                activeGameId,
                _gameInstallationDirectory,
                duskPalettePath,
                quakeInstallations)
            {
                Owner =
                    this
            };

        dialog.ShowDialog();

        if (dialog.ImportedCount <=
            0)
        {
            return;
        }

        SynchronizeRegisteredWadsWithLibrary(
            managedDataRoot,
            adoptExternalWads:
                false);

        SaveSettings();
        RefreshInterface();

        StatusText.Text =
            dialog.ImportedCount ==
                1
                ? "Online WAD added to the global Companion library."
                : $"{dialog.ImportedCount:N0} online WAD imports completed.";
    }

    private void Window_DragEnter(
        object sender,
        DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(
                DataFormats.FileDrop))
        {
            e.Effects =
                DragDropEffects.None;

            e.Handled = true;
            return;
        }

        string[]? paths =
            e.Data.GetData(
                DataFormats.FileDrop)
            as string[];

        e.Effects =
            paths is not null &&
            ContainsPotentialWadInput(paths)
                ? DragDropEffects.Copy
                : DragDropEffects.None;

        if (e.Effects == DragDropEffects.Copy)
        {
            StatusText.Text =
                "Release to import the dropped WAD files into the Companion library.";
        }

        e.Handled = true;
    }

    private void Window_Drop(
        object sender,
        DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(
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

        AddWadPaths(
            paths,
            "drag and drop");

        e.Handled = true;
    }

    private static bool ContainsPotentialWadInput(
        IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
            {
                return true;
            }

            if (File.Exists(path) &&
                string.Equals(
                    Path.GetExtension(path),
                    ".wad",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void AddWadPaths(
        IEnumerable<string> inputPaths,
        string sourceDescription)
    {
        if (!TryEnsureWadLibraryRoot(
                out string managedDataRoot))
        {
            return;
        }

        SynchronizeRegisteredWadsWithLibrary(
            managedDataRoot,
            adoptExternalWads: true);

        int addedCount =
            0;

        int reusedCount =
            0;

        int unsupportedCount =
            0;

        foreach (string candidatePath in
                 ExpandWadPaths(
                     inputPaths)
                 .Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            string fullPath;

            try
            {
                fullPath =
                    Path.GetFullPath(
                        candidatePath);
            }
            catch
            {
                unsupportedCount++;
                continue;
            }

            if (!File.Exists(
                    fullPath) ||
                !string.Equals(
                    Path.GetExtension(
                        fullPath),
                    ".wad",
                    StringComparison.OrdinalIgnoreCase))
            {
                unsupportedCount++;
                continue;
            }

            try
            {
                CompanionWadLibraryImportResult result =
                    _wadLibraryService.Import(
                        managedDataRoot,
                        fullPath);
                _paletteLibraryService.ImportEvidenceForSourceWad(
                    managedDataRoot,
                    fullPath);


                if (result.CopiedIntoLibrary)
                {
                    addedCount++;
                }
                else
                {
                    reusedCount++;
                }
            }
            catch
            {
                unsupportedCount++;
            }
        }

        SynchronizeRegisteredWadsWithLibrary(
            managedDataRoot,
            adoptExternalWads: false);

        SaveSettings();
        RefreshInterface();

        List<string> statusParts =
            new();

        statusParts.Add(
            addedCount ==
                1
                ? "1 WAD imported into the Companion library"
                : $"{addedCount:N0} WADs imported into the Companion library");

        if (reusedCount >
            0)
        {
            statusParts.Add(
                $"{reusedCount:N0} existing library item(s) reused");
        }

        if (unsupportedCount >
            0)
        {
            statusParts.Add(
                $"{unsupportedCount:N0} unsupported item(s) skipped");
        }

        StatusText.Text =
            string.Join(
                "; ",
                statusParts) +
            $" ({sourceDescription}).";
    }

    private static IEnumerable<string> ExpandWadPaths(
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
                if (string.Equals(
                        Path.GetExtension(inputPath),
                        ".wad",
                        StringComparison.OrdinalIgnoreCase))
                {
                    yield return inputPath;
                }

                continue;
            }

            if (!Directory.Exists(inputPath))
            {
                continue;
            }

            IEnumerable<string> files;

            try
            {
                files =
                    Directory.EnumerateFiles(
                        inputPath,
                        "*.wad",
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
                string currentPath;

                try
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }

                    currentPath =
                        enumerator.Current;
                }
                catch
                {
                    break;
                }

                yield return currentPath;
            }
        }
    }

    private bool FilterWadLibraryItem(
        object item)
    {
        if (item is not WadRegistrationResult wad)
        {
            return false;
        }

        string search =
            WadSearchTextBox.Text.Trim();

        if (!string.Equals(
                wad.WadFormat,
                _wadLibraryFormat,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (search.Length == 0)
        {
            return true;
        }

        return ContainsSearch(wad.WadFileName,search) ||
            ContainsSearch(wad.WadPath,search) ||
            ContainsSearch(wad.WadFormat,search) ||
            ContainsSearch(wad.ManifestFileName,search) ||
            ContainsSearch(wad.Validation,search);
    }
    private static bool ContainsSearch(
        string? value,
        string search)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value.Contains(search,StringComparison.OrdinalIgnoreCase);
    }

    private void WadSearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        RefreshWadLibraryBrowser();
    }

    private void WadLibraryFormatTab_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not string format ||
            (format != "WAD2" &&
             format != "WAD3"))
        {
            return;
        }

        if (string.Equals(
                _wadLibraryFormat,
                format,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _wadLibraryFormat =
            format;

        RegistrationGrid.SelectedItem =
            null;

        UpdateWadLibraryTabPresentation();
        RefreshWadLibraryBrowser();

        StatusText.Text =
            $"Showing the global {_wadLibraryFormat} library.";
    }

    private void UpdateWadLibraryTabPresentation()
    {
        bool wad2Selected =
            string.Equals(
                _wadLibraryFormat,
                "WAD2",
                StringComparison.OrdinalIgnoreCase);

        Wad2LibraryTabButton.Style =
            (Style)FindResource(
                wad2Selected
                    ? "PrimaryButtonStyle"
                    : "DarkButtonStyle");

        Wad3LibraryTabButton.Style =
            (Style)FindResource(
                wad2Selected
                    ? "DarkButtonStyle"
                    : "PrimaryButtonStyle");
    }
    private void RefreshWadLibraryBrowser()
    {
        if (_wadLibraryView is null)
        {
            return;
        }

        _wadLibraryView.Refresh();

        int formatCount =
            RegisteredWads.Count(
                wad =>
                    string.Equals(
                        wad.WadFormat,
                        _wadLibraryFormat,
                        StringComparison.OrdinalIgnoreCase));

        int visibleCount =
            _wadLibraryView.Cast<object>().Count();

        WadVisibleCountText.Text =
            visibleCount == formatCount
                ? $"{visibleCount:N0} {_wadLibraryFormat} shown"
                : $"{visibleCount:N0} of {formatCount:N0} {_wadLibraryFormat} shown";

        if (formatCount == 0)
        {
            EmptyRegistrationTitleText.Text =
                $"No {_wadLibraryFormat} WADs in the library yet";

            EmptyRegistrationDetailText.Text =
                _wadLibraryFormat == "WAD2"
                    ? "Import a WAD2 archive, drag one here, or find Quake WADs online."
                    : "Import a WAD3 archive or create a compatible WAD3 version from a WAD2 source.";

            EmptyRegistrationPanel.Visibility =
                Visibility.Visible;
        }
        else if (visibleCount == 0)
        {
            EmptyRegistrationTitleText.Text =
                $"No {_wadLibraryFormat} WADs match your search";

            EmptyRegistrationDetailText.Text =
                "Clear or change the search term.";

            EmptyRegistrationPanel.Visibility =
                Visibility.Visible;
        }
        else
        {
            EmptyRegistrationPanel.Visibility =
                Visibility.Collapsed;
        }
    }
    private void BrowseSelectedWad_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (RegistrationGrid.SelectedItem is not
            WadRegistrationResult selected)
        {
            return;
        }

        if (!TryEnsureWadLibraryRoot(
                out string managedDataRoot))
        {
            return;
        }

        try
        {
            string? activeGameId =
                _projectSession?.Project.GameId;

            string? duskPalettePath =
                TryGetActiveDuskPalettePath();

            IReadOnlyList<string> quakeInstallations =
                _gameInstallationLocator.FindInstallations(
                    CompanionGameProfiles.Quake);

            CompanionPaletteResolution paletteResolution =
                _paletteLibraryService.PrepareForWad(
                    managedDataRoot,
                    selected.WadPath,
                    selected.ManifestPath,
                    activeGameId,
                    _gameInstallationDirectory,
                    duskPalettePath,
                    quakeInstallations);

            CompanionWadBrowserWindow dialog =
                new(
                    selected,
                    managedDataRoot,
                    paletteResolution)
                {
                    Owner =
                        this
                };

            dialog.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "WAD Browser",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RegistrationGrid_MouseDoubleClick(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RegistrationGrid.SelectedItem is not
            WadRegistrationResult)
        {
            return;
        }

        BrowseSelectedWad_Click(
            sender,
            e);
    }

    private string? TryGetActiveDuskPalettePath()
    {
        if (_projectSession is null ||
            !string.Equals(
                _projectSession.Project.GameId,
                CompanionGameProfiles.Dusk.Id,
                StringComparison.OrdinalIgnoreCase) ||
            _installation is null ||
            !_installation.IsValid)
        {
            return null;
        }

        try
        {
            CompanionDuskAuthoringResourceStatus status =
                CompanionDuskAuthoringResourceService.GetStatus(
                    _installation.ExecutablePath);

            if (!status.IsReady)
            {
                return null;
            }

            string candidate =
                Path.Combine(
                    status.ManagedId1Directory,
                    "gfx",
                    "palette.lmp");

            return File.Exists(
                    candidate) &&
                new FileInfo(
                    candidate).Length ==
                    768
                ? candidate
                : null;
        }
        catch
        {
            return null;
        }
    }

    private void RefreshRegistrations_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (CompanionManagedDataRootService
            .TryGetConfiguredRoot(
                _settings,
                out string managedDataRoot))
        {
            SynchronizeRegisteredWadsWithLibrary(
                managedDataRoot,
                adoptExternalWads: true);

            SaveSettings();
            RefreshInterface();

            StatusText.Text =
                $"{RegisteredWads.Count:N0} WAD library item(s) refreshed.";

            return;
        }

        if (RegisteredWads.Count ==
            0)
        {
            return;
        }

        string[] paths =
            RegisteredWads
                .Select(
                    item =>
                        item.WadPath)
                .ToArray();

        RegisteredWads.Clear();

        foreach (string path in
                 paths)
        {
            RegisteredWads.Add(
                WadRegistrationService.Inspect(
                    path));
        }

        SaveSettings();
        RefreshInterface();

        StatusText.Text =
            $"{RegisteredWads.Count:N0} legacy WAD registration(s) revalidated.";
    }

    private void WadLibraryMoreButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (WadLibraryMoreButton.ContextMenu is not
            ContextMenu menu)
        {
            return;
        }

        menu.PlacementTarget =
            WadLibraryMoreButton;

        menu.Placement =
            System.Windows.Controls.Primitives.PlacementMode.Bottom;

        menu.IsOpen =
            true;
    }

    private void RemoveSelected_Click(
        object sender,
        RoutedEventArgs e)
    {
        WadRegistrationResult[] selected =
            RegistrationGrid
                .SelectedItems
                .Cast<WadRegistrationResult>()
                .ToArray();

        if (selected.Length ==
            0)
        {
            return;
        }

        bool hasManagedRoot =
            CompanionManagedDataRootService
                .TryGetConfiguredRoot(
                    _settings,
                    out string managedDataRoot);

        int managedDeleteCount =
            hasManagedRoot
                ? selected.Count(
                    item =>
                        _wadLibraryService.IsLibraryWad(
                            managedDataRoot,
                            item.WadPath))
                : 0;

        if (managedDeleteCount >
            0)
        {
            MessageBoxResult confirmation =
                MessageBox.Show(
                    this,
                    managedDeleteCount ==
                        1
                        ? "Remove this WAD from the Companion library?" +
                          Environment.NewLine +
                          Environment.NewLine +
                          "The managed library copy will be deleted. The original file it was imported from will not be changed."
                        : $"Remove {managedDeleteCount:N0} WADs from the Companion library?" +
                          Environment.NewLine +
                          Environment.NewLine +
                          "The managed library copies will be deleted. Original imported files will not be changed.",
                    "Remove WAD from Library",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (confirmation !=
                MessageBoxResult.Yes)
            {
                return;
            }
        }

        int deletedCount =
            0;

        foreach (WadRegistrationResult item in
                 selected)
        {
            if (hasManagedRoot &&
                _wadLibraryService.IsLibraryWad(
                    managedDataRoot,
                    item.WadPath))
            {
                _wadLibraryService.Remove(
                    managedDataRoot,
                    item.WadPath);

                deletedCount++;
            }

            RegisteredWads.Remove(
                item);
        }

        if (hasManagedRoot)
        {
            SynchronizeRegisteredWadsWithLibrary(
                managedDataRoot,
                adoptExternalWads: false);
        }

        SaveSettings();
        RefreshInterface();

        StatusText.Text =
            deletedCount ==
                1
                ? "1 WAD removed from the Companion library."
                : deletedCount >
                    1
                    ? $"{deletedCount:N0} WADs removed from the Companion library."
                    : $"{selected.Length:N0} legacy WAD registration(s) removed. External files were not deleted.";
    }

    private void OpenSelectedFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        WadRegistrationResult? selected =
            RegistrationGrid.SelectedItem
            as WadRegistrationResult;

        if (selected is null)
        {
            return;
        }

        string? directory =
            Path.GetDirectoryName(
                selected.WadPath);

        if (string.IsNullOrWhiteSpace(directory) ||
            !Directory.Exists(directory))
        {
            MessageBox.Show(
                this,
                "The selected WAD directory no longer exists.",
                "Directory Missing",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        Process.Start(
            new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
    }

    private void RegistrationGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        int selectedCount =
            RegistrationGrid.SelectedItems.Count;

        RemoveWadMenuItem.IsEnabled =
            selectedCount > 0;

        OpenWadFolderMenuItem.IsEnabled =
            selectedCount == 1;

        BrowseWadButton.IsEnabled =
            selectedCount == 1;
    }
    private void LaunchTrenchBroom_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_installation is null ||
            !_installation.IsValid ||
            !File.Exists(
                _installation.ExecutablePath))
        {
            MessageBox.Show(
                this,
                "Select a valid TrenchBroom executable first.",
                "TrenchBroom Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        string workingDirectory =
            Path.GetDirectoryName(
                _installation.ExecutablePath) ??
            Environment.CurrentDirectory;

        Process.Start(
            new ProcessStartInfo
            {
                FileName =
                    _installation.ExecutablePath,

                WorkingDirectory =
                    workingDirectory,

                UseShellExecute = true
            });

        StatusText.Text =
            _installation.IsWadForgeCompatible
                ? "WadForge-compatible TrenchBroom launched."
                : "Standard TrenchBroom launched. Long aliases are not active in this build.";
    }

    private void OpenWadForge_Click(
        object sender,
        RoutedEventArgs e)
    {
        string? wadForgePath =
            FindWadForgeExecutable();

        if (string.IsNullOrWhiteSpace(wadForgePath))
        {
            MessageBox.Show(
                this,
                "WadForge could not be found in the Companion suite or the current development build.",
                "WadForge Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        Process.Start(
            new ProcessStartInfo
            {
                FileName = wadForgePath,

                WorkingDirectory =
                    Path.GetDirectoryName(
                        wadForgePath) ??
                    Environment.CurrentDirectory,

                UseShellExecute = true
            });
    }

    private static string? FindWadForgeExecutable()
    {
        string baseDirectory =
            Path.GetFullPath(
                AppContext.BaseDirectory);

        string[] candidates =
        {
            Path.Combine(
                baseDirectory,
                "WadForge.exe"),

            Path.GetFullPath(
                Path.Combine(
                    baseDirectory,
                    "..",
                    "WadForge",
                    "WadForge.exe")),

            Path.GetFullPath(
                Path.Combine(
                    baseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "WadForge.App",
                    "bin",
                    "Release",
                    "net9.0-windows",
                    "WadForge.exe"))
        };

        foreach (string candidate in
                 candidates.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private void SaveSettings()
    {
        _settings.RegisteredWadPaths =
            RegisteredWads
                .Select(item => item.WadPath)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        CompanionSettingsStore.Save(
            _settings);
    }

    private void RefreshInterface()
    {
        InstallationPathTextBox.Visibility =
            Visibility.Collapsed;

        if (_installation is null ||
            !_installation.IsValid)
        {
            InstallationPathTextBox.Text =
                "No installation selected";

            InstallationStatusText.Text =
                "Setup required — no compatible TrenchBroom build is ready.";

            InstallationStatusText.ToolTip =
                null;

            SetInstallationActionText(
                "Set Up TrenchBroom");

            LaunchButton.IsEnabled =
                false;
        }
        else
        {
            InstallationPathTextBox.Text =
                _installation.ExecutablePath;

            InstallationStatusText.ToolTip =
                _installation.ExecutablePath;

            string versionText =
                string.IsNullOrWhiteSpace(
                    _installation.Version)
                    ? string.Empty
                    : $" {_installation.Version}";

            if (_installation.IsWadForgeCompatible)
            {
                InstallationStatusText.Text =
                    IsManagedTrenchBroom()
                        ? $"✓ Ready — Companion-managed compatible build{versionText}"
                        : $"✓ Ready — compatible TrenchBroom{versionText}";

                SetInstallationActionText(
                    "Change");
            }
            else
            {
                InstallationStatusText.Text =
                    $"Limited — standard TrenchBroom{versionText}. " +
                    "Set up the compatible build for full Companion support.";

                SetInstallationActionText(
                    "Set Up Compatible Build");
            }

            LaunchButton.IsEnabled =
                File.Exists(
                    _installation.ExecutablePath);
        }

        int count =
            RegisteredWads.Count;

        int verifiedManifestCount =
            RegisteredWads.Count(
                item => item.ManifestIsValid);

        int invalidCount =
            RegisteredWads.Count(
                item =>
                    !item.WadIsValid ||
                    (
                        item.ManifestExists &&
                        !item.ManifestIsValid
                    ));

        RegistrationCountText.Text =
            count == 1
                ? "1 library WAD"
                : $"{count:N0} library WADs";

        if (count > 0)
        {
            RegistrationCountText.Text +=
                $" | {verifiedManifestCount:N0} verified manifest(s)";

            if (invalidCount > 0)
            {
                RegistrationCountText.Text +=
                    $" | {invalidCount:N0} warning(s)";
            }
        }

        RefreshWadLibraryBrowser();

        RefreshWadsMenuItem.IsEnabled =
            count > 0;

        int selectedCount =
            RegistrationGrid.SelectedItems.Count;

        RemoveWadMenuItem.IsEnabled =
            selectedCount > 0;

        OpenWadFolderMenuItem.IsEnabled =
            selectedCount == 1;
    }
}
