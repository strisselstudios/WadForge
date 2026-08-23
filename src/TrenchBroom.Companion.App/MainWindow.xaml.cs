using System.Collections.ObjectModel;
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

    public MainWindow()
    {
        InitializeComponent();

        RegisteredWads =
            new ObservableCollection<WadRegistrationResult>();

        DataContext = this;
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

    private void LoadSavedState()
    {
        if (!string.IsNullOrWhiteSpace(
                _settings.TrenchBroomExecutablePath))
        {
            _installation =
                TrenchBroomInstallationService.Inspect(
                    _settings.TrenchBroomExecutablePath);
        }

        HashSet<string> loadedPaths = new(
            StringComparer.OrdinalIgnoreCase);

        foreach (string wadPath in
                 _settings.RegisteredWadPaths)
        {
            string normalizedPath;

            try
            {
                normalizedPath =
                    Path.GetFullPath(wadPath);
            }
            catch
            {
                normalizedPath = wadPath;
            }

            if (!loadedPaths.Add(normalizedPath))
            {
                continue;
            }

            RegisteredWads.Add(
                WadRegistrationService.Inspect(
                    normalizedPath));
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

    private void AddWads_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Register WAD2 or WAD3 archives",
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
                "Release to register the dropped WAD files.";
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
        HashSet<string> existingPaths =
            RegisteredWads
                .Select(item => item.WadPath)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        bool addToCurrentProject =
            _projectSession is not null;

        int addedCount = 0;
        int duplicateCount = 0;
        int unsupportedCount = 0;

        foreach (string candidatePath in
                 ExpandWadPaths(inputPaths)
                     .Distinct(
                         StringComparer.OrdinalIgnoreCase))
        {
            string fullPath;

            try
            {
                fullPath =
                    Path.GetFullPath(candidatePath);
            }
            catch
            {
                unsupportedCount++;
                continue;
            }

            if (!File.Exists(fullPath) ||
                !string.Equals(
                    Path.GetExtension(fullPath),
                    ".wad",
                    StringComparison.OrdinalIgnoreCase))
            {
                unsupportedCount++;
                continue;
            }

            string registrationPath =
                fullPath;

            if (addToCurrentProject)
            {
                try
                {
                    registrationPath =
                        _projectWadService
                            .ImportIntoProject(
                                _projectSession!,
                                fullPath)
                            .WadPath;
                }
                catch
                {
                    unsupportedCount++;
                    continue;
                }
            }

            if (!existingPaths.Add(
                    registrationPath))
            {
                duplicateCount++;
                continue;
            }

            RegisteredWads.Add(
                WadRegistrationService.Inspect(
                    registrationPath));

            addedCount++;
        }

        if (addedCount > 0)
        {
            SaveSettings();
        }

        RefreshInterface();

        List<string> statusParts = new();

        statusParts.Add(
            addToCurrentProject
                ? $"{addedCount:N0} WAD archive(s) added to the current project by {sourceDescription}"
                : $"{addedCount:N0} WAD archive(s) registered by {sourceDescription}");

        if (duplicateCount > 0)
        {
            statusParts.Add(
                $"{duplicateCount:N0} duplicate(s) skipped");
        }

        if (unsupportedCount > 0)
        {
            statusParts.Add(
                $"{unsupportedCount:N0} unsupported or conflicting item(s) skipped");
        }

        StatusText.Text =
            string.Join(
                "; ",
                statusParts) +
            ".";
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

    private void RefreshRegistrations_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (RegisteredWads.Count == 0)
        {
            return;
        }

        string[] paths =
            RegisteredWads
                .Select(item => item.WadPath)
                .ToArray();

        RegisteredWads.Clear();

        foreach (string path in paths)
        {
            RegisteredWads.Add(
                WadRegistrationService.Inspect(
                    path));
        }

        SaveSettings();
        RefreshInterface();

        StatusText.Text =
            $"{RegisteredWads.Count:N0} registration(s) revalidated.";
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

        if (selected.Length == 0)
        {
            return;
        }

        foreach (WadRegistrationResult item in selected)
        {
            RegisteredWads.Remove(item);
        }

        SaveSettings();
        RefreshInterface();

        StatusText.Text =
            $"{selected.Length:N0} registration(s) removed.";
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

        RemoveButton.IsEnabled =
            selectedCount > 0;

        OpenFolderButton.IsEnabled =
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
                ? "1 registered WAD"
                : $"{count:N0} registered WADs";

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

        EmptyRegistrationPanel.Visibility =
            count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        RefreshButton.IsEnabled =
            count > 0;

        int selectedCount =
            RegistrationGrid.SelectedItems.Count;

        RemoveButton.IsEnabled =
            selectedCount > 0;

        OpenFolderButton.IsEnabled =
            selectedCount == 1;
    }
}
