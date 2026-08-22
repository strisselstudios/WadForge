using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using TrenchBroom.Companion.Core;

namespace TrenchBroom.Companion.App;

public partial class MainWindow
{
    private readonly CompanionProjectManager _projectManager =
        new();

    private readonly CompanionGameInstallationLocator
        _gameInstallationLocator =
            new();

    private readonly CompanionProjectCreationService
        _projectCreationService =
            new();

    private readonly CompanionProjectMapImportService
        _mapImportService =
            new();

    private readonly CompanionProjectMapCreationService
        _mapCreationService =
            new();

    private readonly CompanionProjectMapLifecycleService
        _mapLifecycleService =
            new();

    private readonly CompanionProjectLayout
        _projectLayout =
            new();

    private CompanionProjectSession? _projectSession;

    private string? _gameInstallationDirectory;

    private Button? _gameFolderButton;

    private Button? _newMapButton;

    private Button? _mapActionsButton;

    private ComboBox? _mapSelectorComboBox;

    private bool _refreshingMapSelector;

    protected override void OnContentRendered(
        EventArgs e)
    {
        base.OnContentRendered(
            e);

        EnsureProjectControls();
        RefreshProjectInterface();
    }

    private void EnsureProjectControls()
    {
        ImportMapButton.Content =
            "Add Map";

        OpenProjectFolderButton.Content =
            "Project Files";

        OpenCurrentMapButton.Content =
            "Open Map in TrenchBroom";

        if (ImportMapButton.Parent is not
            Panel buttonPanel)
        {
            return;
        }

        if (_mapSelectorComboBox is null)
        {
            _mapSelectorComboBox =
                new ComboBox
                {
                    Width =
                        230,

                    MinWidth =
                        210,

                    Height =
                        38,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            10,
                            10),

                    HorizontalContentAlignment =
                        HorizontalAlignment.Stretch,

                    VerticalContentAlignment =
                        VerticalAlignment.Center,

                    ItemTemplate =
                        FindResource(
                            "MapChoiceTemplate")
                        as DataTemplate,

                    IsEnabled =
                        false,

                    ToolTip =
                        "Current project map"
                };

            _mapSelectorComboBox.SelectionChanged +=
                ProjectMapComboBox_SelectionChanged;

            int insertionIndex =
                buttonPanel.Children.IndexOf(
                    ImportMapButton);

            if (insertionIndex < 0)
            {
                insertionIndex = 0;
            }

            buttonPanel.Children.Insert(
                insertionIndex,
                _mapSelectorComboBox);
        }

        if (_mapActionsButton is null)
        {
            _mapActionsButton =
                new Button
                {
                    Content =
                        "Map Actions",

                    Style =
                        FindResource(
                            "DarkButtonStyle")
                        as Style,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            10,
                            10),

                    IsEnabled =
                        false,

                    ToolTip =
                        "Remove the selected map from the project or move it safely to project backups"
                };

            ContextMenu mapActionsMenu =
                new()
                {
                    Style =
                        FindResource(
                            "DarkContextMenuStyle")
                        as Style,

                    Placement =
                        System.Windows.Controls.Primitives.PlacementMode.Bottom
                };

            MenuItem removeFromProjectItem =
                new()
                {
                    Header =
                        "Remove from Project",

                    Style =
                        FindResource(
                            "DarkMenuItemStyle")
                        as Style
                };

            removeFromProjectItem.Click +=
                RemoveMapFromProject_Click;

            MenuItem deleteMapSafelyItem =
                new()
                {
                    Header =
                        "Delete Map Safely",

                    Style =
                        FindResource(
                            "DarkMenuItemStyle")
                        as Style
                };

            deleteMapSafelyItem.Click +=
                DeleteMapSafely_Click;

            mapActionsMenu.Items.Add(
                removeFromProjectItem);

            mapActionsMenu.Items.Add(
                deleteMapSafelyItem);

            _mapActionsButton.ContextMenu =
                mapActionsMenu;

            _mapActionsButton.Click +=
                MapActions_Click;

            int insertionIndex =
                buttonPanel.Children.IndexOf(
                    ImportMapButton);

            if (insertionIndex < 0)
            {
                insertionIndex =
                    buttonPanel.Children.Count;
            }

            buttonPanel.Children.Insert(
                insertionIndex,
                _mapActionsButton);
        }

        if (_newMapButton is null)
        {
            _newMapButton =
                new Button
                {
                    Content =
                        "New Map",

                    Style =
                        FindResource(
                            "DarkButtonStyle")
                        as Style,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            10,
                            10),

                    IsEnabled =
                        false,

                    ToolTip =
                        "Create a new empty map in this project"
                };

            _newMapButton.Click +=
                NewMap_Click;

            int insertionIndex =
                buttonPanel.Children.IndexOf(
                    ImportMapButton);

            if (insertionIndex < 0)
            {
                insertionIndex =
                    buttonPanel.Children.Count;
            }

            buttonPanel.Children.Insert(
                insertionIndex,
                _newMapButton);
        }

        if (_gameFolderButton is null)
        {
            _gameFolderButton =
                new Button
                {
                    Content =
                        "Game Folder",

                    Style =
                        FindResource(
                            "DarkButtonStyle")
                        as Style,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            10,
                            10),

                    IsEnabled =
                        false,

                    ToolTip =
                        "Open the runtime mod folder used by the selected game"
                };

            _gameFolderButton.Click +=
                OpenGameFolder_Click;

            int insertionIndex =
                buttonPanel.Children.IndexOf(
                    OpenCurrentMapButton);

            if (insertionIndex < 0)
            {
                insertionIndex =
                    buttonPanel.Children.Count;
            }

            buttonPanel.Children.Insert(
                insertionIndex,
                _gameFolderButton);
        }
    }

    private void NewProject_Click(
        object sender,
        RoutedEventArgs e)
    {
        EnsureProjectControls();

        CompanionGameProfile gameProfile;

        try
        {
            gameProfile =
                CompanionGameProfiles.GetRequired(
                    GetSelectedProjectGameId());
        }
        catch (Exception exception)
        {
            ShowProjectError(
                exception.Message);

            return;
        }

        string? gameInstallation =
            ResolveGameInstallation(
                gameProfile,
                promptIfMissing: true);

        if (string.IsNullOrWhiteSpace(
                gameInstallation))
        {
            return;
        }

        CompanionNewProjectDialog dialog =
            new(
                gameProfile)
            {
                Owner =
                    this
            };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            CompanionProjectCreationResult creation =
                _projectCreationService.Create(
                    gameProfile,
                    dialog.SelectedDriveRoot,
                    gameInstallation,
                    dialog.ProjectName,
                    dialog.SelectedTextureArchiveFormat);

            _projectSession =
                creation.Session;

            _gameInstallationDirectory =
                creation
                    .ProvisionedProject
                    .GameInstallationDirectory;

            SelectProjectGame(
                gameProfile.Id);

            RefreshProjectInterface();

            StatusText.Text =
                $"Created project '{_projectSession.Project.Name}'.";
        }
        catch (Exception exception)
        {
            ShowProjectError(
                exception.Message);
        }
    }

    private void OpenProject_Click(
        object sender,
        RoutedEventArgs e)
    {
        EnsureProjectControls();

        OpenFileDialog dialog =
            new()
            {
                Title =
                    "Open TrenchBroom Companion Project",

                Filter =
                    "TrenchBroom Companion projects|*.tbproject|" +
                    "All files|*.*",

                InitialDirectory =
                    GetDefaultProjectsRoot() ??
                    string.Empty,

                Multiselect =
                    false,

                CheckFileExists =
                    true,

                CheckPathExists =
                    true
            };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _projectSession =
                _projectManager.Open(
                    dialog.FileName);

            SelectProjectGame(
                _projectSession.Project.GameId);

            CompanionGameProfile gameProfile =
                CompanionGameProfiles.GetRequired(
                    _projectSession.Project.GameId);

            _gameInstallationDirectory =
                ResolveProjectGameInstallation(
                    gameProfile);

            RefreshProjectInterface();

            StatusText.Text =
                $"Opened project '{_projectSession.Project.Name}'.";
        }
        catch (Exception exception)
        {
            _projectSession =
                null;

            _gameInstallationDirectory =
                null;

            RefreshProjectInterface();

            ShowProjectError(
                exception.Message);
        }
    }

    private void CloseProject_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_projectSession is null)
        {
            return;
        }

        string projectName =
            _projectSession.Project.Name;

        _projectSession =
            null;

        _gameInstallationDirectory =
            null;

        RefreshProjectInterface();

        StatusText.Text =
            $"Closed project '{projectName}'.";
    }

    private void NewMap_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_projectSession is null)
        {
            return;
        }

        CompanionNewMapDialog dialog =
            new(
                _projectSession.ProjectDirectory)
            {
                Owner =
                    this
            };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            string createdMap =
                _mapCreationService.CreateMap(
                    _projectSession,
                    dialog.MapName);

            RefreshProjectInterface();

            StatusText.Text =
                $"Created map '{Path.GetFileName(createdMap)}'.";
        }
        catch (Exception exception)
        {
            ShowProjectError(
                exception.Message);
        }
    }

    private void ImportMap_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_projectSession is null)
        {
            return;
        }

        OpenFileDialog dialog =
            new()
            {
                Title =
                    "Add Maps to Project",

                Filter =
                    "TrenchBroom map files|*.map|" +
                    "All files|*.*",

                Multiselect =
                    true,

                CheckFileExists =
                    true,

                CheckPathExists =
                    true
            };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            IReadOnlyList<string> imported =
                _mapImportService.ImportMaps(
                    _projectSession,
                    dialog.FileNames);

            RefreshProjectInterface();

            StatusText.Text =
                imported.Count == 1
                    ? $"Added '{Path.GetFileName(imported[0])}' to the project."
                    : $"Added {imported.Count:N0} maps to the project.";
        }
        catch (Exception exception)
        {
            ShowProjectError(
                exception.Message);
        }
    }

    private void MapActions_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_projectSession is null ||
            _mapSelectorComboBox?.SelectedItem is not
                CompanionMapChoice)
        {
            return;
        }

        if (_mapActionsButton?.ContextMenu is not
            ContextMenu menu)
        {
            return;
        }

        menu.PlacementTarget =
            _mapActionsButton;

        menu.Placement =
            System.Windows.Controls.Primitives.PlacementMode.Bottom;

        menu.IsOpen =
            true;
    }

    private void RemoveMapFromProject_Click(
        object sender,
        RoutedEventArgs e)
    {
        CompanionMapChoice? selectedMap =
            GetSelectedMapChoice();

        if (_projectSession is null ||
            selectedMap is null)
        {
            return;
        }

        MessageBoxResult confirmation =
            MessageBox.Show(
                this,
                $"Remove '{Path.GetFileName(selectedMap.FullPath)}' from this Companion project?\r\n\r\n" +
                "The .map file will stay exactly where it is. " +
                "You can add it back to the project later.",
                "Remove Map from Project",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            CompanionProjectMapRemovalResult result =
                _mapLifecycleService.RemoveFromProject(
                    _projectSession,
                    selectedMap.FullPath);

            RefreshProjectInterface();

            StatusText.Text =
                $"Removed '{result.DisplayName}' from the project. The map file was kept.";
        }
        catch (Exception exception)
        {
            ShowProjectError(
                exception.Message);

            RefreshProjectInterface();
        }
    }

    private void DeleteMapSafely_Click(
        object sender,
        RoutedEventArgs e)
    {
        CompanionMapChoice? selectedMap =
            GetSelectedMapChoice();

        if (_projectSession is null ||
            selectedMap is null)
        {
            return;
        }

        MessageBoxResult confirmation =
            MessageBox.Show(
                this,
                $"Delete '{Path.GetFileName(selectedMap.FullPath)}' from this project?\r\n\r\n" +
                "Companion will NOT permanently erase it. " +
                "The source .map will be moved into this project's backups\\Deleted Maps folder, " +
                "then removed from the map list.",
                "Delete Map Safely",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            CompanionProjectMapRemovalResult result =
                _mapLifecycleService.DeleteMapSafely(
                    _projectSession,
                    selectedMap.FullPath);

            RefreshProjectInterface();

            StatusText.Text =
                result.FileMovedToBackup &&
                !string.IsNullOrWhiteSpace(
                    result.BackupPath)
                    ? $"Moved '{result.DisplayName}' to project backups and removed it from the project."
                    : $"Removed missing map '{result.DisplayName}' from the project.";
        }
        catch (Exception exception)
        {
            ShowProjectError(
                exception.Message);

            RefreshProjectInterface();
        }
    }

    private CompanionMapChoice? GetSelectedMapChoice()
    {
        return _mapSelectorComboBox?.SelectedItem as
            CompanionMapChoice;
    }

    private void OpenProjectFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_projectSession is null ||
            !Directory.Exists(
                _projectSession.ProjectDirectory))
        {
            return;
        }

        OpenDirectory(
            _projectSession.ProjectDirectory);
    }

    private void OpenGameFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        string? runtimeDirectory =
            GetRuntimeModDirectory();

        if (string.IsNullOrWhiteSpace(
                runtimeDirectory))
        {
            ShowProjectError(
                "The game installation could not be located for this project.");

            return;
        }

        if (!Directory.Exists(
                runtimeDirectory))
        {
            ShowProjectError(
                "The runtime mod folder does not exist yet.");

            return;
        }

        OpenDirectory(
            runtimeDirectory);
    }

    private static void OpenDirectory(
        string directory)
    {
        Process.Start(
            new ProcessStartInfo
            {
                FileName =
                    directory,

                UseShellExecute =
                    true
            });
    }

    private void OpenCurrentMap_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_projectSession is null)
        {
            return;
        }

        string? activeMapPath =
            _projectSession.GetActiveMapFullPath();

        if (string.IsNullOrWhiteSpace(
                activeMapPath) ||
            !File.Exists(
                activeMapPath))
        {
            MessageBox.Show(
                this,
                "The current project does not have a valid active map.",
                "Map Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            RefreshProjectInterface();
            return;
        }

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

        bool useManagedPortableDusk =
            string.Equals(
                _projectSession.Project.GameId,
                CompanionGameProfiles.Dusk.Id,
                StringComparison.OrdinalIgnoreCase) &&
            IsManagedTrenchBroom();

        if (useManagedPortableDusk &&
            IsManagedTrenchBroomProcessRunning())
        {
            MessageBox.Show(
                this,
                "Companion-managed TrenchBroom is already running." +
                Environment.NewLine +
                Environment.NewLine +
                "Close the existing TrenchBroom window before opening another DUSK map from Companion. " +
                "Companion blocks a second portable instance so TrenchBroom's preferences cannot be locked by two processes.",
                "TrenchBroom Already Open",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        try
        {
            PrepareActiveMapForTrenchBroom(
                activeMapPath);
        }
        catch (Exception exception)
        {
            ShowProjectError(
                exception.Message);

            return;
        }

        string workingDirectory =
            Path.GetDirectoryName(
                _installation.ExecutablePath) ??
            Environment.CurrentDirectory;

        ProcessStartInfo startInfo =
            new()
            {
                FileName =
                    _installation.ExecutablePath,

                WorkingDirectory =
                    workingDirectory,

                UseShellExecute =
                    false
            };

        if (useManagedPortableDusk)
        {
            try
            {
                RemoveStalePortablePreferenceLock(
                    workingDirectory);
            }
            catch (Exception exception)
            {
                ShowProjectError(
                    exception.Message);

                return;
            }

            startInfo.ArgumentList.Add(
                "--portable");
        }

        startInfo.ArgumentList.Add(
            Path.GetFullPath(
                activeMapPath));

        Process.Start(
            startInfo);

        StatusText.Text =
            $"Opened '{Path.GetFileName(activeMapPath)}' in TrenchBroom.";
    }

    private bool IsManagedTrenchBroomProcessRunning()
    {
        if (_installation is null)
        {
            return false;
        }

        string managedExecutablePath;

        try
        {
            managedExecutablePath =
                Path.GetFullPath(
                    _installation.ExecutablePath);
        }
        catch
        {
            return true;
        }

        string processName =
            Path.GetFileNameWithoutExtension(
                managedExecutablePath);

        Process[] processes =
            Process.GetProcessesByName(
                processName);

        try
        {
            foreach (Process process in processes)
            {
                try
                {
                    string? processPath =
                        process.MainModule?.FileName;

                    if (string.IsNullOrWhiteSpace(
                            processPath))
                    {
                        return true;
                    }

                    if (string.Equals(
                            Path.GetFullPath(
                                processPath),
                            managedExecutablePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // A same-name process that cannot be inspected
                    // is treated conservatively as active.
                    return true;
                }
            }

            return false;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static void RemoveStalePortablePreferenceLock(
        string workingDirectory)
    {
        string preferenceLockPath =
            Path.Combine(
                workingDirectory,
                "config",
                "Preferences.json.lck");

        if (!File.Exists(
                preferenceLockPath))
        {
            return;
        }

        try
        {
            File.Delete(
                preferenceLockPath);
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Companion found a TrenchBroom portable preferences lock that could not be cleared. " +
                "Close every Companion-managed TrenchBroom window and try again.",
                exception);
        }
    }

    private void PrepareActiveMapForTrenchBroom(
        string activeMapPath)
    {
        if (_projectSession is null ||
            _installation is null)
        {
            throw new InvalidOperationException(
                "A Companion project and TrenchBroom installation are required.");
        }

        string gameId =
            _projectSession.Project.GameId;

        if (string.Equals(
                gameId,
                CompanionGameProfiles.Dusk.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            if (!_installation.IsWadForgeCompatible ||
                !IsManagedTrenchBroom())
            {
                throw new InvalidOperationException(
                    "DUSK project integration requires the Companion-managed compatible TrenchBroom build. " +
                    "Use the TrenchBroom setup control in Companion before opening this map.");
            }

            CompanionTrenchBroomGameConfigService.EnsureDuskGameConfig(
                _installation.ExecutablePath);

            CompanionDuskTrenchBroomEnvironmentService.Ensure(
                _installation.ExecutablePath);
        }

        CompanionTrenchBroomMapIdentityService.EnsureMapIdentity(
            activeMapPath,
            gameId);
    }

    private void ProjectMapComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_refreshingMapSelector ||
            _projectSession is null ||
            _mapSelectorComboBox?.SelectedItem is not
                CompanionMapChoice selectedMap)
        {
            return;
        }

        try
        {
            _projectSession.SetActiveMap(
                selectedMap.FullPath);

            _projectSession.Save();

            RefreshProjectInterface();

            StatusText.Text =
                $"Current map: {selectedMap.DisplayName}.";
        }
        catch (Exception exception)
        {
            ShowProjectError(
                exception.Message);

            RefreshProjectInterface();
        }
    }

    private string? GetDefaultProjectsRoot()
    {
        if (_projectSession is not null)
        {
            string? currentWorkspaceRoot =
                Path.GetDirectoryName(
                    _projectSession.ProjectDirectory);

            if (!string.IsNullOrWhiteSpace(
                    currentWorkspaceRoot) &&
                Directory.Exists(
                    currentWorkspaceRoot))
            {
                return Path.GetFullPath(
                    currentWorkspaceRoot);
            }
        }

        string? applicationDriveRoot =
            Path.GetPathRoot(
                AppContext.BaseDirectory);

        if (!string.IsNullOrWhiteSpace(
                applicationDriveRoot))
        {
            string applicationWorkspace =
                Path.Combine(
                    applicationDriveRoot,
                    CompanionProjectLayout.WorkspaceDirectoryName);

            if (Directory.Exists(
                    applicationWorkspace))
            {
                return Path.GetFullPath(
                    applicationWorkspace);
            }
        }

        foreach (DriveInfo drive in
                 DriveInfo.GetDrives()
                     .Where(
                         drive =>
                             drive.IsReady &&
                             drive.DriveType is
                                 DriveType.Fixed or
                                 DriveType.Removable)
                     .OrderBy(
                         drive =>
                             drive.Name,
                         StringComparer.OrdinalIgnoreCase))
        {
            string workspaceRoot =
                Path.Combine(
                    drive.RootDirectory.FullName,
                    CompanionProjectLayout.WorkspaceDirectoryName);

            if (Directory.Exists(
                    workspaceRoot))
            {
                return Path.GetFullPath(
                    workspaceRoot);
            }
        }

        return null;
    }

    private string? ResolveProjectGameInstallation(
        CompanionGameProfile gameProfile)
    {
        if (_projectSession is null)
        {
            return null;
        }

        CompanionProjectGameBinding? binding =
            _projectSession.Project.GameBinding;

        if (binding is not null &&
            _gameInstallationLocator.IsInstallationDirectory(
                gameProfile,
                binding.GameInstallationDirectory))
        {
            EnsureProjectRuntimeDirectory(
                binding.RuntimeModDirectory);

            return Path.GetFullPath(
                binding.GameInstallationDirectory);
        }

        string? detected =
            _gameInstallationLocator.FindInstallation(
                gameProfile);

        if (!string.IsNullOrWhiteSpace(
                detected))
        {
            PersistProjectGameBinding(
                gameProfile,
                detected);

            return detected;
        }

        string? selectedInstallation =
            ResolveGameInstallation(
                gameProfile,
                promptIfMissing: true);

        if (string.IsNullOrWhiteSpace(
                selectedInstallation))
        {
            return null;
        }

        PersistProjectGameBinding(
            gameProfile,
            selectedInstallation);

        return selectedInstallation;
    }

    private void PersistProjectGameBinding(
        CompanionGameProfile gameProfile,
        string gameInstallationDirectory)
    {
        if (_projectSession is null)
        {
            return;
        }

        string fullGameInstallationDirectory =
            Path.GetFullPath(
                gameInstallationDirectory);

        string runtimeName =
            string.IsNullOrWhiteSpace(
                _projectSession.Project.ModName)
                ? _projectSession.Project.Name
                : _projectSession.Project.ModName;

        string runtimeModDirectory =
            _projectLayout.GetRuntimeModDirectory(
                gameProfile,
                fullGameInstallationDirectory,
                runtimeName);

        EnsureProjectRuntimeDirectory(
            runtimeModDirectory);

        _projectSession.Project.GameBinding =
            new CompanionProjectGameBinding
            {
                GameInstallationDirectory =
                    fullGameInstallationDirectory,

                RuntimeModDirectory =
                    runtimeModDirectory
            };

        _projectSession.Save();
    }

    private static void EnsureProjectRuntimeDirectory(
        string runtimeModDirectory)
    {
        string fullRuntimeModDirectory =
            Path.GetFullPath(
                runtimeModDirectory);

        Directory.CreateDirectory(
            Path.Combine(
                fullRuntimeModDirectory,
                CompanionProjectLayout.MapsDirectoryName));
    }

    private string? ResolveGameInstallation(
        CompanionGameProfile gameProfile,
        bool promptIfMissing)
    {
        string? detected =
            _gameInstallationLocator.FindInstallation(
                gameProfile);

        if (!string.IsNullOrWhiteSpace(
                detected))
        {
            return detected;
        }

        if (!promptIfMissing)
        {
            return null;
        }

        OpenFolderDialog dialog =
            new()
            {
                Title =
                    $"Locate {gameProfile.DisplayName} installation"
            };

        if (dialog.ShowDialog(this) != true)
        {
            return null;
        }

        if (!_gameInstallationLocator.IsInstallationDirectory(
                gameProfile,
                dialog.FolderName))
        {
            MessageBox.Show(
                this,
                $"That folder does not appear to be the {gameProfile.DisplayName} installation folder.",
                "Game Installation Not Recognized",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return null;
        }

        return Path.GetFullPath(
            dialog.FolderName);
    }

    private string? GetRuntimeModDirectory()
    {
        if (_projectSession is null)
        {
            return null;
        }

        CompanionProjectGameBinding? binding =
            _projectSession.Project.GameBinding;

        if (binding is not null &&
            !string.IsNullOrWhiteSpace(
                binding.RuntimeModDirectory))
        {
            try
            {
                return Path.GetFullPath(
                    binding.RuntimeModDirectory);
            }
            catch
            {
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(
                _gameInstallationDirectory))
        {
            return null;
        }

        CompanionGameProfile gameProfile;

        try
        {
            gameProfile =
                CompanionGameProfiles.GetRequired(
                    _projectSession.Project.GameId);
        }
        catch
        {
            return null;
        }

        string runtimeName =
            string.IsNullOrWhiteSpace(
                _projectSession.Project.ModName)
                ? _projectSession.Project.Name
                : _projectSession.Project.ModName;

        try
        {
            return _projectLayout.GetRuntimeModDirectory(
                gameProfile,
                _gameInstallationDirectory,
                runtimeName);
        }
        catch
        {
            return null;
        }
    }

    private string GetSelectedProjectGameId()
    {
        ComboBoxItem? selected =
            ProjectGameComboBox.SelectedItem
            as ComboBoxItem;

        string? gameId =
            selected?.Tag
            as string;

        return string.IsNullOrWhiteSpace(
                gameId)
            ? "dusk"
            : gameId;
    }

    private void SelectProjectGame(
        string gameId)
    {
        foreach (object item in
                 ProjectGameComboBox.Items)
        {
            if (item is not
                ComboBoxItem comboItem)
            {
                continue;
            }

            if (comboItem.Tag is string tag &&
                string.Equals(
                    tag,
                    gameId,
                    StringComparison.OrdinalIgnoreCase))
            {
                ProjectGameComboBox.SelectedItem =
                    comboItem;

                return;
            }
        }
    }

    private void RefreshProjectInterface()
    {
        EnsureProjectControls();

        if (_projectSession is null)
        {
            ProjectNameText.Text =
                "No project open";

            ProjectDetailsText.Text =
                "Create or open a project to begin.";

            ProjectLocationText.Text =
                string.Empty;

            ProjectLocationText.ToolTip =
                null;

            ProjectGameComboBox.IsEnabled =
                true;

            CloseProjectButton.IsEnabled =
                false;

            ImportMapButton.IsEnabled =
                false;

            if (_newMapButton is not null)
            {
                _newMapButton.IsEnabled =
                    false;
            }

            if (_mapActionsButton is not null)
            {
                _mapActionsButton.IsEnabled =
                    false;
            }

            OpenProjectFolderButton.IsEnabled =
                false;

            OpenCurrentMapButton.IsEnabled =
                false;

            if (_gameFolderButton is not null)
            {
                _gameFolderButton.IsEnabled =
                    false;
            }

            RefreshMapSelector(
                activeMapPath: null);

            return;
        }

        try
        {
            _mapLifecycleService.ReconcileMissingMaps(
                _projectSession);
        }
        catch (Exception exception)
        {
            StatusText.Text =
                $"Could not reconcile missing maps: {exception.Message}";
        }

        CompanionProjectManifest project =
            _projectSession.Project;

        string game =
            project.GameId switch
            {
                "dusk" =>
                    "DUSK",

                "quake" =>
                    "Quake",

                "halflife" =>
                    "Half-Life",

                _ =>
                    project.GameId
            };

        string mapCountText =
            project.Maps.Count == 1
                ? "1 map"
                : $"{project.Maps.Count:N0} maps";

        string? activeMapFullPath =
            null;

        string currentMapText =
            "None";

        if (!string.IsNullOrWhiteSpace(
                project.ActiveMapPath))
        {
            try
            {
                activeMapFullPath =
                    _projectSession.GetActiveMapFullPath();
            }
            catch
            {
                activeMapFullPath =
                    null;
            }

            if (!string.IsNullOrWhiteSpace(
                    activeMapFullPath))
            {
                currentMapText =
                    Path.GetFileNameWithoutExtension(
                        activeMapFullPath);
            }
        }

        ProjectNameText.Text =
            project.Name;

        string textureArchiveFormat =
            string.IsNullOrWhiteSpace(
                project.PreferredTextureArchiveFormat)
                ? "WAD format: Auto"
                : CompanionTextureArchiveFormats.GetDisplayName(
                    project.PreferredTextureArchiveFormat);

        ProjectDetailsText.Text =
            $"{game} | {textureArchiveFormat} | {mapCountText} | Current map: {currentMapText}";

        ProjectLocationText.Text =
            _projectSession.ProjectDirectory;

        ProjectLocationText.ToolTip =
            _projectSession.ProjectDirectory;

        SelectProjectGame(
            project.GameId);

        ProjectGameComboBox.IsEnabled =
            false;

        CloseProjectButton.IsEnabled =
            true;

        ImportMapButton.IsEnabled =
            true;

        if (_newMapButton is not null)
        {
            _newMapButton.IsEnabled =
                true;
        }

        if (_mapActionsButton is not null)
        {
            _mapActionsButton.IsEnabled =
                project.Maps.Count > 0;
        }

        OpenProjectFolderButton.IsEnabled =
            Directory.Exists(
                _projectSession.ProjectDirectory);

        OpenCurrentMapButton.IsEnabled =
            !string.IsNullOrWhiteSpace(
                activeMapFullPath) &&
            File.Exists(
                activeMapFullPath);

        string? runtimeDirectory =
            GetRuntimeModDirectory();

        if (_gameFolderButton is not null)
        {
            _gameFolderButton.IsEnabled =
                !string.IsNullOrWhiteSpace(
                    runtimeDirectory) &&
                Directory.Exists(
                    runtimeDirectory);
        }

        RefreshMapSelector(
            activeMapFullPath);
    }

    private void RefreshMapSelector(
        string? activeMapPath)
    {
        if (_mapSelectorComboBox is null)
        {
            return;
        }

        _refreshingMapSelector =
            true;

        try
        {
            _mapSelectorComboBox.Items.Clear();

            if (_projectSession is null)
            {
                _mapSelectorComboBox.IsEnabled =
                    false;

                return;
            }

            CompanionMapChoice? activeChoice =
                null;

            foreach (CompanionProjectMap map in
                     _projectSession.Project.Maps)
            {
                string fullPath;

                try
                {
                    fullPath =
                        CompanionProjectStore.ResolveMapPath(
                            _projectSession.ProjectFilePath,
                            map.Path);
                }
                catch
                {
                    continue;
                }

                if (!File.Exists(
                        fullPath))
                {
                    continue;
                }

                CompanionMapChoice choice =
                    new(
                        string.IsNullOrWhiteSpace(
                            map.DisplayName)
                            ? Path.GetFileNameWithoutExtension(
                                fullPath)
                            : map.DisplayName,
                        fullPath);

                _mapSelectorComboBox.Items.Add(
                    choice);

                if (!string.IsNullOrWhiteSpace(
                        activeMapPath) &&
                    string.Equals(
                        fullPath,
                        activeMapPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    activeChoice =
                        choice;
                }
            }

            _mapSelectorComboBox.SelectedItem =
                activeChoice ??
                (
                    _mapSelectorComboBox.Items.Count > 0
                        ? _mapSelectorComboBox.Items[0]
                        : null
                );

            if (_mapSelectorComboBox.SelectedItem is
                CompanionMapChoice selectedChoice)
            {
                _mapSelectorComboBox.ToolTip =
                    selectedChoice.DisplayName;
            }
            else
            {
                _mapSelectorComboBox.ToolTip =
                    "Current project map";
            }

            _mapSelectorComboBox.IsEnabled =
                _mapSelectorComboBox.Items.Count > 0;
        }
        finally
        {
            _refreshingMapSelector =
                false;
        }
    }

    private void ShowProjectError(
        string message)
    {
        MessageBox.Show(
            this,
            message,
            "TrenchBroom Companion",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}