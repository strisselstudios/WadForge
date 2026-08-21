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

    public MainWindow()
    {
        InitializeComponent();

        RegisteredWads =
            new ObservableCollection<WadRegistrationResult>();

        DataContext = this;

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
        TrenchBroomInstallationResolution resolution =
            TrenchBroomInstallationResolver.Resolve(
                _settings.TrenchBroomExecutablePath,
                AppContext.BaseDirectory);

        if (resolution.Installation is not null &&
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

            string managedExecutablePath =
                Path.GetFullPath(
                    TrenchBroomManagedInstallationService
                        .DefaultManagedExecutablePath);

            bool alreadyManaged =
                string.Equals(
                    sourceExecutablePath,
                    managedExecutablePath,
                    StringComparison.OrdinalIgnoreCase);

            TrenchBroomManagedInstallationResult result =
                TrenchBroomManagedInstallationService.Provision(
                    sourceExecutablePath);

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
    private void SelectInstallation_Click(
        object sender,
        RoutedEventArgs e)
    {
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
            "Standard TrenchBroom selected. Companion did not modify it. " +
            "The compatible build still needs to be set up for long texture aliases.";

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

            if (!existingPaths.Add(fullPath))
            {
                duplicateCount++;
                continue;
            }

            RegisteredWads.Add(
                WadRegistrationService.Inspect(
                    fullPath));

            addedCount++;
        }

        if (addedCount > 0)
        {
            SaveSettings();
        }

        RefreshInterface();

        List<string> statusParts = new();

        statusParts.Add(
            $"{addedCount:N0} WAD archive(s) registered by {sourceDescription}");

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
        string wadForgePath =
            Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "WadForge",
                    "WadForge.exe"));

        if (!File.Exists(wadForgePath))
        {
            MessageBox.Show(
                this,
                "WadForge.exe was not found at:" +
                Environment.NewLine +
                wadForgePath,
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
        if (_installation is null)
        {
            InstallationPathTextBox.Text =
                "No installation selected";

            InstallationStatusText.Text =
                "No TrenchBroom executable selected.";

            LaunchButton.IsEnabled = false;
        }
        else
        {
            InstallationPathTextBox.Text =
                _installation.ExecutablePath;

            string versionText =
                string.IsNullOrWhiteSpace(
                    _installation.Version)
                    ? string.Empty
                    : $" Version: {_installation.Version}.";

            InstallationStatusText.Text =
                _installation.Status +
                versionText;

            LaunchButton.IsEnabled =
                _installation.IsValid &&
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
