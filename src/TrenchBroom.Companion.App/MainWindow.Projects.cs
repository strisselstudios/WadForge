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

    private readonly CompanionProjectWadService
        _projectWadService =
            new();

    private readonly CompanionProjectLayout
        _projectLayout =
            new();

    private CompanionProjectSession? _projectSession;

    private string? _gameInstallationDirectory;

    private bool _refreshingMapList;

    private bool _projectGameSelectionHooked;

    protected override void OnContentRendered(
        EventArgs e)
    {
        base.OnContentRendered(
            e);

        EnsureProjectControls();
        ApplyRememberedProjectPreferences();
        RefreshProjectInterface();
    }

    private void ApplyRememberedProjectPreferences()
    {
        if (_projectGameSelectionHooked)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(
                _settings.LastProjectGameId))
        {
            SelectProjectGame(
                _settings.LastProjectGameId);
        }

        ProjectGameComboBox.SelectionChanged +=
            ProjectGameComboBox_SelectionChanged;

        _projectGameSelectionHooked =
            true;
    }

    private void ProjectGameComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_projectSession is not null)
        {
            return;
        }

        string gameId =
            GetSelectedProjectGameId();

        if (string.Equals(
                _settings.LastProjectGameId,
                gameId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.LastProjectGameId =
            gameId;

        SaveSettings();
    }

    private void EnsureProjectControls()
    {
        ImportMapButton.Content =
            "Add Existing";

        NewMapButton.Content =
            "+ New Map";

        OpenCurrentMapButton.Content =
            "Open in TrenchBroom";

        CompileSettingsButton.Content =
            "Compile Settings";

        CompileSettingsButton.ToolTip =
            "Configure compiler options for this project";
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
                gameProfile,
                _settings.LastWorkspaceDriveRoot)
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

            EnsureManagedDataRootForProject(
                _projectSession.ProjectDirectory);

            _gameInstallationDirectory =
                creation
                    .ProvisionedProject
                    .GameInstallationDirectory;

            _settings.LastProjectGameId =
                gameProfile.Id;

            _settings.LastWorkspaceDriveRoot =
                dialog.SelectedDriveRoot;

            SaveSettings();

            SelectProjectGame(
                gameProfile.Id);

            string? createdMap =
                null;

            if (dialog.CreateFirstMap)
            {
                createdMap =
                    _mapCreationService.CreateMap(
                        _projectSession,
                        dialog.FirstMapName);
            }

            RefreshProjectInterface();

            StatusText.Text =
                createdMap is null
                    ? $"Created project '{_projectSession.Project.Name}'."
                    : $"Created project '{_projectSession.Project.Name}' with map '{Path.GetFileName(createdMap)}'.";
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

        OpenFolderDialog dialog =
            new()
            {
                Title =
                    "Select TrenchBroom Companion Project Folder",

                InitialDirectory =
                    GetDefaultProjectsRoot() ??
                    string.Empty,

                Multiselect =
                    false
            };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            string selectedProjectDirectory =
                Path.GetFullPath(
                    dialog.FolderName);

            string[] projectFiles =
                Directory.GetFiles(
                    selectedProjectDirectory,
                    "*.tbproject",
                    SearchOption.TopDirectoryOnly);

            if (projectFiles.Length == 0)
            {
                MessageBox.Show(
                    this,
                    "That folder is not a TrenchBroom Companion project." +
                    Environment.NewLine +
                    Environment.NewLine +
                    "Select a project folder that contains its .tbproject file.",
                    "Project Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (projectFiles.Length > 1)
            {
                MessageBox.Show(
                    this,
                    "That folder contains more than one .tbproject file, so Companion cannot determine which project to open." +
                    Environment.NewLine +
                    Environment.NewLine +
                    "A Companion project folder must contain exactly one .tbproject file.",
                    "Ambiguous Project Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            _projectSession =
                _projectManager.Open(
                    projectFiles[0]);

            EnsureManagedDataRootForProject(
                _projectSession.ProjectDirectory);

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

    private void ProjectUtilities_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not
                Button button ||
            button.ContextMenu is not
                ContextMenu menu)
        {
            return;
        }

        menu.PlacementTarget =
            button;

        menu.Placement =
            System.Windows.Controls.Primitives.PlacementMode.Bottom;

        menu.IsOpen =
            true;
    }

    private void ProjectMapOverflow_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_projectSession is null ||
            sender is not
                Button button ||
            button.Tag is not
                CompanionMapChoice selectedMap)
        {
            return;
        }

        ProjectMapListBox.SelectedItem =
            selectedMap;

        ContextMenu menu =
            new()
            {
                Style =
                    FindResource(
                        "DarkContextMenuStyle")
                    as Style,

                Placement =
                    System.Windows.Controls.Primitives.PlacementMode.Bottom
            };

        MenuItem openItem =
            new()
            {
                Header =
                    "Open in TrenchBroom",

                Style =
                    FindResource(
                        "DarkMenuItemStyle")
                    as Style
            };

        openItem.Click +=
            OpenCurrentMap_Click;

        MenuItem compileSettingsItem =
            new()
            {
                Header =
                    "Compile Settings",

                IsEnabled =
                    string.Equals(
                        _projectSession.Project.GameId,
                        CompanionGameProfiles.Dusk.Id,
                        StringComparison.OrdinalIgnoreCase),

                Style =
                    FindResource(
                        "DarkMenuItemStyle")
                    as Style
            };

        compileSettingsItem.Click +=
            CompileSettings_Click;

        MenuItem removeItem =
            new()
            {
                Header =
                    "Remove from Project",

                Style =
                    FindResource(
                        "DarkMenuItemStyle")
                    as Style
            };

        removeItem.Click +=
            RemoveMapFromProject_Click;

        MenuItem deleteItem =
            new()
            {
                Header =
                    "Delete Map Safely",

                Style =
                    FindResource(
                        "DarkMenuItemStyle")
                    as Style
            };

        deleteItem.Click +=
            DeleteMapSafely_Click;

        menu.Items.Add(
            openItem);

        menu.Items.Add(
            compileSettingsItem);

        menu.Items.Add(
            new Separator());

        menu.Items.Add(
            removeItem);

        menu.Items.Add(
            deleteItem);

        menu.PlacementTarget =
            button;

        menu.IsOpen =
            true;

        e.Handled =
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
        return ProjectMapListBox.SelectedItem as
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

    private void CompileSettings_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_projectSession is null)
        {
            return;
        }

        if (!string.Equals(
                _projectSession.Project.GameId,
                CompanionGameProfiles.Dusk.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            ShowProjectError(
                "Compile Settings are currently implemented for DUSK projects only.");

            return;
        }

        if (IsManagedTrenchBroomProcessRunning())
        {
            MessageBox.Show(
                this,
                "Close Companion-managed TrenchBroom before changing Compile Settings. " +
                "Companion regenerates the managed compile profile when settings are saved.",
                "Close TrenchBroom",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        try
        {
            string toolchainVersion =
                CompanionEricwToolchainService.RecommendedVersion;

            CompanionCompilerOptionSchema schema =
                CompanionCompilerOptionSchemaService.GetRequired(
                    CompanionGameProfiles.Dusk.Id,
                    toolchainVersion);

            CompanionBuildSettings settings =
                CompanionBuildSettingsService.Load(
                    _projectSession.ProjectDirectory,
                    CompanionGameProfiles.Dusk.Id,
                    toolchainVersion);

            CompanionBuildSettingsDialog dialog =
                new(
                    _projectSession.Project.Name,
                    schema,
                    settings)
                {
                    Owner =
                        this
                };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            CompanionBuildSettingsService.Save(
                _projectSession.ProjectDirectory,
                CompanionGameProfiles.Dusk.Id,
                toolchainVersion,
                dialog.SelectedSettings);

            if (_installation is not null &&
                IsManagedTrenchBroom() &&
                !string.IsNullOrWhiteSpace(
                    GetRuntimeModDirectory()))
            {
                PrepareDuskCompilerProfile();
            }

            StatusText.Text =
                "Compile Settings saved. The Companion - DUSK compile profile has been refreshed.";
        }
        catch (Exception exception)
        {
            ShowProjectError(
                exception.Message);
        }
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
            if (!PrepareActiveMapForTrenchBroom(
                    activeMapPath))
            {
                return;
            }
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

    private bool PrepareActiveMapForTrenchBroom(
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
                _installation.ExecutablePath,
                CompanionManagedDataRootService
                    .GetRequiredRoot(
                        _settings));

            if (!EnsureDuskAuthoringResources())
            {
                return false;
            }

            _projectWadService.SynchronizeMapWorldspawnWads(
                _projectSession,
                activeMapPath);

            PrepareDuskCompilerProfile();
        }

        CompanionTrenchBroomMapIdentityService.EnsureMapIdentity(
            activeMapPath,
            gameId);

        return true;
    }

    private bool EnsureDuskAuthoringResources()
    {
        if (_installation is null)
        {
            throw new InvalidOperationException(
                "A TrenchBroom installation is required.");
        }

        CompanionDuskAuthoringResourceStatus status =
            CompanionDuskAuthoringResourceService.GetStatus(
                _installation.ExecutablePath);

        if (status.IsReady)
        {
            return true;
        }

        MessageBoxResult choice =
            MessageBox.Show(
                this,
                "DUSK's TrenchBroom authoring resources have not been installed into Companion yet." +
                Environment.NewLine +
                Environment.NewLine +
                "Companion needs the DUSK mapping resource bundle containing dusk4.fgd, dusk.pak, and palette.lmp. " +
                "These are authoring resources; your actual DUSK installation and SDK remain separate." +
                Environment.NewLine +
                Environment.NewLine +
                "Select the folder where you extracted those DUSK mapping resources now?",
                "DUSK Authoring Resources",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

        if (choice !=
            MessageBoxResult.Yes)
        {
            StatusText.Text =
                "DUSK authoring resources are still required before opening this map.";

            return false;
        }

        OpenFolderDialog dialog =
            new()
            {
                Title =
                    "Select DUSK mapping resource folder",

                Multiselect =
                    false
            };

        if (dialog.ShowDialog(
                this) != true)
        {
            StatusText.Text =
                "DUSK authoring resource setup was canceled.";

            return false;
        }

        CompanionDuskAuthoringResourceImportResult result =
            CompanionDuskAuthoringResourceService.Import(
                _installation.ExecutablePath,
                dialog.FolderName);

        StatusText.Text =
            $"Installed DUSK authoring resources from '{result.SourceDirectory}'.";

        return true;
    }

    private void PrepareDuskCompilerProfile()
    {
        if (_installation is null ||
            _projectSession is null)
        {
            throw new InvalidOperationException(
                "A Companion project and TrenchBroom installation are required.");
        }

        string managedDataRoot =
            CompanionManagedDataRootService.GetRequiredRoot(
                _settings);

        CompanionEricwToolchainStatus toolchain =
            CompanionEricwToolchainService.EnsureProvisioned(
                AppContext.BaseDirectory,
                managedDataRoot);

        string? runtimeModDirectory =
            GetRuntimeModDirectory();

        if (string.IsNullOrWhiteSpace(
                runtimeModDirectory))
        {
            throw new InvalidOperationException(
                "Companion could not resolve this DUSK project's runtime folder for compiler deployment.");
        }

        Directory.CreateDirectory(
            Path.Combine(
                _projectSession.ProjectDirectory,
                "build"));

        CompanionBuildSettings buildSettings =
            CompanionBuildSettingsService.Load(
                _projectSession.ProjectDirectory,
                CompanionGameProfiles.Dusk.Id,
                toolchain.Version);

        CompanionTrenchBroomCompilationProfileResult result =
            CompanionTrenchBroomCompilationProfileService.EnsureDuskProfile(
                _installation.ExecutablePath,
                toolchain,
                runtimeModDirectory,
                buildSettings);

        if (string.IsNullOrWhiteSpace(
                _gameInstallationDirectory))
        {
            throw new InvalidOperationException(
                "Companion could not resolve the DUSK installation required for the Moddable launcher profile.");
        }

        CompanionTrenchBroomEngineProfileResult engineProfile =
            CompanionTrenchBroomEngineProfileService.EnsureDuskProfile(
                _installation.ExecutablePath,
                _gameInstallationDirectory);

        StatusText.Text =
            $"DUSK tooling ready — ericw-tools {toolchain.Version}, compile profile '{result.ProfileName}', and launch profile '{engineProfile.ProfileName}' are configured.";
    }

    private void ProjectMapListBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_refreshingMapList ||
            _projectSession is null ||
            ProjectMapListBox.SelectedItem is not
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

    private void ProjectMapListBox_MouseDoubleClick(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (GetSelectedMapChoice() is null)
        {
            return;
        }

        OpenCurrentMap_Click(
            sender,
            e);
    }

    private void EnsureManagedDataRootForProject(
        string projectDirectory)
    {
        bool configured =
            CompanionManagedDataRootService
                .EnsureConfiguredForProject(
                    _settings,
                    projectDirectory);

        if (!configured)
        {
            return;
        }

        SaveSettings();
        ResolveTrenchBroomInstallation();

        StatusText.Text =
            $"Companion managed storage: {_settings.ManagedDataRootPath}.";
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

            NewMapButton.IsEnabled =
                false;

            OpenCurrentMapButton.IsEnabled =
                false;

            CompileSettingsButton.IsEnabled =
                false;

            RefreshMapList(
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

        NewMapButton.IsEnabled =
            true;

        OpenCurrentMapButton.IsEnabled =
            !string.IsNullOrWhiteSpace(
                activeMapFullPath) &&
            File.Exists(
                activeMapFullPath);

        CompileSettingsButton.IsEnabled =
            string.Equals(
                project.GameId,
                CompanionGameProfiles.Dusk.Id,
                StringComparison.OrdinalIgnoreCase);

        string? runtimeDirectory =
            GetRuntimeModDirectory();

        RefreshMapList(
            activeMapFullPath);
    }

    private void RefreshMapList(
        string? activeMapPath)
    {
        _refreshingMapList =
            true;

        try
        {
            ProjectMapListBox.Items.Clear();

            if (_projectSession is null)
            {
                ProjectMapListBox.IsEnabled =
                    false;

                EmptyProjectMapsText.Visibility =
                    Visibility.Visible;

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

                ProjectMapListBox.Items.Add(
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

            ProjectMapListBox.SelectedItem =
                activeChoice ??
                (
                    ProjectMapListBox.Items.Count > 0
                        ? ProjectMapListBox.Items[0]
                        : null
                );

            ProjectMapListBox.IsEnabled =
                ProjectMapListBox.Items.Count > 0;

            EmptyProjectMapsText.Visibility =
                ProjectMapListBox.Items.Count > 0
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }
        finally
        {
            _refreshingMapList =
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