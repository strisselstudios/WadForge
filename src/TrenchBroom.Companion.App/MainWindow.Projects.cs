using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

    private readonly CompanionProjectWadSelectionService
        _projectWadSelectionService =
            new();
    private readonly CompanionProjectWadLibraryBindingService
        _projectWadLibraryBindingService =
            new();
    private bool _legacyWadCleanupRunning;

    private readonly CompanionProjectLayout
        _projectLayout =
            new();

    private CompanionProjectSession? _projectSession;

    private string? _gameInstallationDirectory;

    private bool _refreshingMapList;
    private string? _projectReadinessPreparationProblem;

    private bool _projectGameSelectionHooked;

    private const int RecentProjectLimit = 5;

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

        ManageAssetsButton.Content =
            "Map WADs";

        ManageAssetsButton.ToolTip =
            "Choose WADs for the selected map";

        ManageAssetsButton.Click -=
            ShellNavigation_Click;

        ManageAssetsButton.Click -=
            ManageMapWads_Click;

        ManageAssetsButton.Click +=
            ManageMapWads_Click;}

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
                _projectWadSelectionService.ApplyProjectDefaultsToMap(
                    _projectSession,
                    createdMap);

                ShowMapWadSelectionDialog(
                    createdMap,
                    preferProjectDefault: true,
                    showWhenEmpty: false);
            }

            TryPrepareCurrentProjectReadiness();

            RegisterRecentProject(
                _projectSession.ProjectDirectory);

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
            OpenProjectDirectory(
                dialog.FolderName);

            StatusText.Text =
                $"Opened project '{_projectSession!.Project.Name}'.";
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

    private void OpenRecentProject_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (RecentProjectsListBox.SelectedItem is not
                RecentProjectChoice recentProject)
        {
            return;
        }

        try
        {
            OpenProjectDirectory(
                recentProject.DirectoryPath);

            ShowShellSection(
                ProjectSection);

            RefreshShellContext();

            StatusText.Text =
                $"Opened project '{_projectSession!.Project.Name}'.";
        }
        catch (Exception exception)
        {
            _projectSession =
                null;

            _gameInstallationDirectory =
                null;

            RefreshProjectInterface();
            RefreshShellContext();

            ShowProjectError(
                exception.Message);
        }
    }

    private void RecentProjectsListBox_MouseDoubleClick(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RecentProjectsListBox.SelectedItem is null)
        {
            return;
        }

        OpenRecentProject_Click(
            sender,
            e);
    }

    private void RecentProjectsListBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        OpenRecentProjectButton.IsEnabled =
            RecentProjectsListBox.SelectedItem is
                RecentProjectChoice;
    }

    private void OpenProjectDirectory(
        string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                projectDirectory))
        {
            throw new ArgumentException(
                "Project folder cannot be empty.",
                nameof(projectDirectory));
        }

        string selectedProjectDirectory =
            Path.GetFullPath(
                projectDirectory);

        if (!Directory.Exists(
                selectedProjectDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Project folder was not found: '{selectedProjectDirectory}'.");
        }

        string[] projectFiles =
            Directory.GetFiles(
                selectedProjectDirectory,
                "*.tbproject",
                SearchOption.TopDirectoryOnly);

        if (projectFiles.Length == 0)
        {
            throw new InvalidOperationException(
                "That folder is not a TrenchBroom Companion project. " +
                "Select a project folder that contains its .tbproject file.");
        }

        if (projectFiles.Length > 1)
        {
            throw new InvalidOperationException(
                "That folder contains more than one .tbproject file. " +
                "A Companion project folder must contain exactly one .tbproject file.");
        }

        _projectSession =
            _projectManager.Open(
                projectFiles[0]);

        EnsureManagedDataRootForProject(
            _projectSession.ProjectDirectory);
        MigrateProjectWadSelections();

        _projectSession.Save();

        SelectProjectGame(
            _projectSession.Project.GameId);

        CompanionGameProfile gameProfile =
            CompanionGameProfiles.GetRequired(
                _projectSession.Project.GameId);

        _gameInstallationDirectory =
            ResolveProjectGameInstallation(
                gameProfile);

        TryPrepareCurrentProjectReadiness();

        RegisterRecentProject(
            _projectSession.ProjectDirectory);

        RefreshProjectInterface();
        QueueLegacyProjectWadCleanup();
    }

    private void RegisterRecentProject(
        string projectDirectory)
    {
        string fullProjectDirectory =
            Path.GetFullPath(
                projectDirectory);

        List<string> updated =
            new()
            {
                fullProjectDirectory
            };

        foreach (string recentPath in
                 _settings.RecentProjectDirectories)
        {
            if (string.IsNullOrWhiteSpace(
                    recentPath))
            {
                continue;
            }

            string fullRecentPath;

            try
            {
                fullRecentPath =
                    Path.GetFullPath(
                        recentPath);
            }
            catch
            {
                continue;
            }

            if (updated.Any(
                    existing =>
                        string.Equals(
                            existing,
                            fullRecentPath,
                            StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            updated.Add(
                fullRecentPath);

            if (updated.Count >=
                RecentProjectLimit)
            {
                break;
            }
        }

        _settings.RecentProjectDirectories =
            updated;

        SaveSettings();
    }

    private void RefreshRecentProjects()
    {
        RecentProjectsListBox.Items.Clear();

        OpenRecentProjectButton.IsEnabled =
            false;

        List<string> validDirectories =
            new();

        foreach (string projectDirectory in
                 _settings.RecentProjectDirectories)
        {
            if (validDirectories.Count >=
                RecentProjectLimit)
            {
                break;
            }

            if (!TryCreateRecentProjectChoice(
                    projectDirectory,
                    out RecentProjectChoice? choice) ||
                choice is null)
            {
                continue;
            }

            if (validDirectories.Any(
                    existing =>
                        string.Equals(
                            existing,
                            choice.DirectoryPath,
                            StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            validDirectories.Add(
                choice.DirectoryPath);

            RecentProjectsListBox.Items.Add(
                choice);
        }

        bool settingsChanged =
            _settings.RecentProjectDirectories.Count !=
                validDirectories.Count ||
            !_settings.RecentProjectDirectories.SequenceEqual(
                validDirectories,
                StringComparer.OrdinalIgnoreCase);

        if (settingsChanged)
        {
            _settings.RecentProjectDirectories =
                validDirectories;

            SaveSettings();
        }

        bool hasRecentProjects =
            RecentProjectsListBox.Items.Count > 0;

        RecentProjectsEmptyText.Visibility =
            hasRecentProjects
                ? Visibility.Collapsed
                : Visibility.Visible;

        RecentProjectsListBox.Visibility =
            hasRecentProjects
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (hasRecentProjects)
        {
            RecentProjectsListBox.SelectedIndex =
                0;

            OpenRecentProjectButton.IsEnabled =
                true;
        }
    }

    private bool TryCreateRecentProjectChoice(
        string projectDirectory,
        out RecentProjectChoice? choice)
    {
        choice =
            null;

        if (string.IsNullOrWhiteSpace(
                projectDirectory))
        {
            return false;
        }

        try
        {
            string fullProjectDirectory =
                Path.GetFullPath(
                    projectDirectory);

            if (!Directory.Exists(
                    fullProjectDirectory))
            {
                return false;
            }

            string[] projectFiles =
                Directory.GetFiles(
                    fullProjectDirectory,
                    "*.tbproject",
                    SearchOption.TopDirectoryOnly);

            if (projectFiles.Length != 1)
            {
                return false;
            }

            CompanionProjectSession recentSession =
                _projectManager.Open(
                    projectFiles[0]);

            CompanionProjectManifest project =
                recentSession.Project;

            string gameDisplayName;

            if (CompanionGameProfiles.TryGet(
                    project.GameId,
                    out CompanionGameProfile? gameProfile) &&
                gameProfile is not null)
            {
                gameDisplayName =
                    gameProfile.DisplayName;
            }
            else
            {
                gameDisplayName =
                    project.GameId;
            }

            choice =
                new RecentProjectChoice(
                    project.Name,
                    gameDisplayName,
                    fullProjectDirectory);

            return true;
        }
        catch
        {
            return false;
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
            _projectWadSelectionService.ApplyProjectDefaultsToMap(
                _projectSession,
                createdMap);

            ShowMapWadSelectionDialog(
                createdMap,
                preferProjectDefault:
                    _projectSession.Project.Maps.Count ==
                    1,
                showWhenEmpty: false);

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
            MigrateProjectWadSelections();

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

    private void ManageMapWads_Click(
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
                "Select a map before choosing its WADs.",
                "Map Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        try
        {
            ShowMapWadSelectionDialog(
                activeMapPath,
                preferProjectDefault: false,
                showWhenEmpty: true);
        }
        catch (Exception exception)
        {
            ShowProjectError(
                exception.Message);
        }
    }

    private bool ShowMapWadSelectionDialog(
        string mapPath,
        bool preferProjectDefault,
        bool showWhenEmpty)
    {
        if (_projectSession is null)
        {
            return false;
        }

        string managedDataRoot =
            CompanionManagedDataRootService.GetRequiredRoot(
                _settings);

        CompanionProjectMap map =
            _projectWadSelectionService.GetMap(
                _projectSession,
                mapPath);

        IReadOnlyList<CompanionWadLibraryAsset> assets =
            _projectWadSelectionService.GetSelectableAssets(
                _projectSession.Project,
                managedDataRoot,
                _wadLibraryService,
                map.WadAssetIds);

        if (assets.Count ==
                0 &&
            !showWhenEmpty)
        {
            return false;
        }

        string formatDisplay =
            string.IsNullOrWhiteSpace(
                _projectSession.Project.PreferredTextureArchiveFormat)
                ? CompanionTextureArchiveFormats.GetDisplayName(
                    CompanionGameProfiles
                        .GetRequired(
                            _projectSession.Project.GameId)
                        .DefaultTextureArchiveFormat)
                : CompanionTextureArchiveFormats.GetDisplayName(
                    _projectSession.Project.PreferredTextureArchiveFormat);

        CompanionWadSelectionDialog dialog =
            new(
                map.DisplayName,
                formatDisplay,
                assets,
                map.WadAssetIds,
                preferProjectDefault)
            {
                Owner =
                    this
            };

        if (dialog.ShowDialog() !=
            true)
        {
            return false;
        }

        CompanionProjectWadSelectionResult result =
            _projectWadSelectionService.SetMapSelection(
                _projectSession,
                mapPath,
                dialog.SelectedAssetIds,
                dialog.UseAsProjectDefault,
                managedDataRoot,
                _wadLibraryService,
                _projectWadService);

        QueueLegacyProjectWadCleanup();

        RefreshProjectInterface();

        StatusText.Text =
            result.SelectedWadCount ==
                1
                ? $"1 library WAD selected for '{map.DisplayName}'."
                : $"{result.SelectedWadCount:N0} library WADs selected for '{map.DisplayName}'.";

        return true;
    }

    private void MigrateProjectWadSelections()
    {
        if (_projectSession is null)
        {
            return;
        }

        string managedDataRoot =
            CompanionManagedDataRootService.GetRequiredRoot(
                _settings);

        CompanionProjectWadSelectionMigrationResult migration =
            _projectWadSelectionService.MigrateLegacyProjectSelections(
                _projectSession,
                managedDataRoot,
                _wadLibraryService,
                _projectWadService);

        if (migration.ImportedToLibraryCount >
            0)
        {
            SynchronizeRegisteredWadsWithLibrary(
                managedDataRoot,
                adoptExternalWads: false);

            SaveSettings();
        }

        if (migration.Issues.Count >
            0)
        {
            StatusText.Text =
                $"WAD selection migration completed with {migration.Issues.Count:N0} item(s) needing review.";
        }
    }
    private void QueueLegacyProjectWadCleanup()
    {
        if (_legacyWadCleanupRunning ||
            _projectSession is null ||
            IsManagedTrenchBroomProcessRunning())
        {
            return;
        }

        try
        {
            string projectFilePath =
                _projectSession.ProjectFilePath;

            string projectDirectory =
                Path.GetFullPath(
                    _projectSession.ProjectDirectory);

            string managedDataRoot =
                CompanionManagedDataRootService.GetRequiredRoot(
                    _settings);

            _legacyWadCleanupRunning =
                true;

            _ =
                RunLegacyProjectWadCleanupAsync(
                    projectFilePath,
                    projectDirectory,
                    managedDataRoot);
        }
        catch (Exception exception)
        {
            _legacyWadCleanupRunning =
                false;

            StatusText.Text =
                "Legacy WAD cleanup was deferred: " +
                exception.Message;
        }
    }

    private async Task RunLegacyProjectWadCleanupAsync(
        string projectFilePath,
        string projectDirectory,
        string managedDataRoot)
    {
        try
        {
            CompanionLegacyProjectWadCleanupResult result =
                await Task.Run(
                    () =>
                    {
                        CompanionProjectManager manager =
                            new();

                        CompanionProjectSession cleanupSession =
                            manager.Open(
                                projectFilePath);

                        CompanionProjectWadLibraryBindingService bindingService =
                            new();

                        return bindingService.CleanupLegacyProjectWads(
                            cleanupSession,
                            managedDataRoot,
                            new CompanionWadLibraryService(),
                            new CompanionProjectWadService());
                    });

            bool sameProject =
                _projectSession is not null &&
                string.Equals(
                    Path.GetFullPath(
                        _projectSession.ProjectDirectory),
                    projectDirectory,
                    StringComparison.OrdinalIgnoreCase);

            if (!sameProject)
            {
                return;
            }

            if (result.DeletedWadCount >
                0)
            {
                StatusText.Text =
                    result.DeletedWadCount ==
                    1
                        ? "Removed 1 verified redundant project WAD copy. The central-library copy remains."
                        : $"Removed {result.DeletedWadCount:N0} verified redundant project WAD copies. Central-library copies remain.";
            }
            else if (result.HasIssues)
            {
                StatusText.Text =
                    $"Legacy WAD cleanup was deferred because {result.Issues.Count:N0} safety check(s) are not yet satisfied.";
            }
            else if (result.RemovedLegacyDirectory)
            {
                StatusText.Text =
                    "Removed the empty legacy project WAD folder.";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text =
                "Legacy WAD cleanup was deferred: " +
                exception.Message;
        }
        finally
        {
            _legacyWadCleanupRunning =
                false;
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

        MenuItem wadItem =
            new()
            {
                Header =
                    "Choose WADs",

                Style =
                    FindResource(
                        "DarkMenuItemStyle")
                    as Style
            };

        wadItem.Click +=
            ManageMapWads_Click;
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

        menu.Items.Insert(
            1,
            wadItem);
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

        StartTrenchBroomWithProjectAssetWatch(
            startInfo,
            activeMapPath);

        StatusText.Text =
            $"Opened '{Path.GetFileName(activeMapPath)}' in TrenchBroom.";
    }

    private void StartTrenchBroomWithProjectAssetWatch(
        ProcessStartInfo startInfo,
        string mapPath)
    {
        if (_projectSession is null)
        {
            throw new InvalidOperationException(
                "A Companion project is required.");
        }

        string projectDirectory =
            Path.GetFullPath(
                _projectSession.ProjectDirectory);

        string fullMapPath =
            Path.GetFullPath(
                mapPath);

        Process? process =
            Process.Start(
                startInfo);

        if (process is null)
        {
            throw new InvalidOperationException(
                "TrenchBroom did not start.");
        }

        process.EnableRaisingEvents =
            true;

        process.Exited +=
            (_, _) =>
            {
                try
                {
                    Dispatcher.BeginInvoke(
                        new Action(
                            () =>
                                ReconcileProjectAssetsAfterTrenchBroomExit(
                                    projectDirectory,
                                    fullMapPath)));
                }
                catch
                {
                    // Companion may already be shutting down.
                }
                finally
                {
                    process.Dispose();
                }
            };
    }

    private void ReconcileProjectAssetsAfterTrenchBroomExit(
        string projectDirectory,
        string mapPath)
    {
        try
        {
            if (_projectSession is null ||
                !string.Equals(
                    Path.GetFullPath(
                        _projectSession.ProjectDirectory),
                    projectDirectory,
                    StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(
                    mapPath))
            {
                return;
            }

            string managedDataRoot =
                CompanionManagedDataRootService.GetRequiredRoot(
                    _settings);

            CompanionProjectWadLibraryReconciliationResult result =
                _projectWadLibraryBindingService.ReconcileMapReferencesToLibrary(
                    _projectSession,
                    mapPath,
                    managedDataRoot,
                    _wadLibraryService,
                    _projectWadService);

            if (result.ImportedToLibraryCount >
                0)
            {
                SynchronizeRegisteredWadsWithLibrary(
                    managedDataRoot,
                    adoptExternalWads: false);

                SaveSettings();
            }

            if (result.Changed ||
                result.ImportedToLibraryCount >
                    0)
            {
                TryPrepareCurrentProjectReadiness();

                RefreshProjectInterface();

                StatusText.Text =
                    result.ImportedToLibraryCount ==
                    1
                        ? "Imported 1 WAD from TrenchBroom into the central library and updated this map's selection."
                        : result.ImportedToLibraryCount >
                            1
                            ? $"Imported {result.ImportedToLibraryCount:N0} WADs from TrenchBroom into the central library and updated this map's selection."
                            : "Updated this map to use its canonical central-library WAD selection.";
            }

            if (result.HasIssues)
            {
                MessageBox.Show(
                    this,
                    BuildWadLibraryReconciliationWarning(
                        result),
                    "Map Asset Check",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            StatusText.Text =
                "Companion could not reconcile map assets after TrenchBroom closed: " +
                exception.Message;
        }
    }
    private static string BuildWadLibraryReconciliationWarning(
        CompanionProjectWadLibraryReconciliationResult result)
    {
        List<string> lines =
            new()
            {
                "Companion found WAD references that could not be adopted into this map's central-library selection automatically.",
                string.Empty
            };

        foreach (string issue in
                 result.Issues.Take(
                     6))
        {
            lines.Add(
                "- " +
                issue);
        }

        if (result.Issues.Count >
            6)
        {
            lines.Add(
                $"- ...and {result.Issues.Count - 6:N0} more.");
        }

        lines.Add(
            string.Empty);

        lines.Add(
            "External source WAD files were not changed.");

        return string.Join(
            Environment.NewLine,
            lines);
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

        string managedDataRoot =
            CompanionManagedDataRootService.GetRequiredRoot(
                _settings);

        CompanionProjectMap wadMap =
            _projectWadSelectionService.GetMap(
                _projectSession,
                activeMapPath);

        if (wadMap.WadAssetIds.Count ==
            0)
        {
            CompanionProjectWadLibraryReconciliationResult wadReconciliation =
                _projectWadLibraryBindingService.ReconcileMapReferencesToLibrary(
                    _projectSession,
                    activeMapPath,
                    managedDataRoot,
                    _wadLibraryService,
                    _projectWadService);

            if (wadReconciliation.HasIssues)
            {
                MessageBox.Show(
                    this,
                    BuildWadLibraryReconciliationWarning(
                        wadReconciliation),
                    "Map Asset Check",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return false;
            }
        }

        IReadOnlyList<string> selectedWadPaths =
            _projectWadLibraryBindingService.ResolveSelectedWadPaths(
                _projectSession,
                activeMapPath,
                managedDataRoot,
                _wadLibraryService);

        _projectWadService.SynchronizeMapWorldspawnWads(
            activeMapPath,
            selectedWadPaths);
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

            CompanionDuskAuthoringResourceStatus authoringStatus =
                CompanionDuskAuthoringResourceService.GetStatus(
                    _installation.ExecutablePath);

            if (!authoringStatus.IsReady)
            {
                throw new InvalidOperationException(
                    authoringStatus.Problem ??
                    "The managed DUSK authoring resources are not ready.");
            }

            string duskPalettePath =
                Path.Combine(
                    authoringStatus.ManagedId1Directory,
                    "gfx",
                    "palette.lmp");

            CompanionImportedMapAssetNormalizationResult assetNormalization =
                CompanionImportedMapAssetService.NormalizeForDusk(
                    _projectSession,
                    activeMapPath,
                    selectedWadPaths,
                    duskPalettePath);
            _projectWadService.SynchronizeMapWorldspawnWads(
                activeMapPath,
                selectedWadPaths);

            if (assetNormalization.NormalizationChanged &&
                assetNormalization.HasWarnings)
            {
                MessageBox.Show(
                    this,
                    BuildImportedMapAssetWarning(
                        assetNormalization),
                    "Imported Map Asset Check",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            PrepareDuskCompilerProfile();
        }

        CompanionTrenchBroomMapIdentityService.EnsureMapIdentity(
            activeMapPath,
            gameId);

        _projectReadinessPreparationProblem = null;
        RefreshProjectReadinessIndicator();

        return true;
    }

    private static string BuildImportedMapAssetWarning(
        CompanionImportedMapAssetNormalizationResult result)
    {
        List<string> lines =
            new();

        if (result.ImportedWadCount > 0)
        {
            lines.Add(
                $"{result.ImportedWadCount:N0} referenced WAD(s) were copied into this project.");
        }

        if (result.MissingWadReferences.Count > 0)
        {
            lines.Add(
                $"{result.MissingWadReferences.Count:N0} referenced WAD(s) could not be resolved.");
        }

        if (result.InvalidWadReferences.Count > 0)
        {
            lines.Add(
                $"{result.InvalidWadReferences.Count:N0} referenced WAD(s) were invalid or conflicted with a project WAD.");
        }

        if (result.MissingTextureNames.Count > 0)
        {
            lines.Add(
                $"{result.MissingTextureNames.Count:N0} used texture(s) are still missing.");
        }

        if (result.DuplicateTextureProviders.Count > 0)
        {
            lines.Add(
                $"{result.DuplicateTextureProviders.Count:N0} used texture(s) exist in more than one managed WAD.");
        }


        lines.Add(
            string.Empty);

        lines.Add(
            "Companion will still open the map. A detailed asset report was written to:");

        lines.Add(
            result.ReportPath);

        return string.Join(
            Environment.NewLine,
            lines);
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

        CompanionEricwToolchainStatus stableToolchain =
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
                stableToolchain.Version);

        string wadLibraryDirectory =
            CompanionManagedDataRootService.GetWadLibraryDirectory(
                managedDataRoot);

        Directory.CreateDirectory(
            wadLibraryDirectory);

        string? activeMapPath =
            _projectSession.GetActiveMapFullPath();

        CompanionDuskCompileModeDecision modeDecision =
            !string.IsNullOrWhiteSpace(
                activeMapPath) &&
            File.Exists(
                activeMapPath)
                ? CompanionDuskCompileModeService.Determine(
                    activeMapPath,
                    wadLibraryDirectory)
                : new CompanionDuskCompileModeDecision(
                    CompanionDuskCompileMode.QuakeBsp,
                    Array.Empty<string>());

        CompanionDuskCompilationProfileContext compileContext =
            CompanionDuskCompilationProfileContext.CreateQuake(
                wadLibraryDirectory);

        CompanionEricwToolchainStatus? halfLifeToolchain =
            null;

        if (modeDecision.UsesHalfLifeBsp)
        {
            CompanionDuskAuthoringResourceStatus authoringStatus =
                CompanionDuskAuthoringResourceService.GetStatus(
                    _installation.ExecutablePath);

            if (!authoringStatus.IsReady)
            {
                throw new InvalidOperationException(
                    authoringStatus.Problem ??
                    "The managed DUSK authoring resources are not ready.");
            }

            string duskPalettePath =
                Path.Combine(
                    authoringStatus.ManagedId1Directory,
                    "gfx",
                    "palette.lmp");

            string companionExecutablePath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "TrenchBroom-Companion.exe");

            if (!File.Exists(
                    companionExecutablePath))
            {
                throw new FileNotFoundException(
                    "Companion could not locate its compile preparation executable.",
                    companionExecutablePath);
            }

            halfLifeToolchain =
                CompanionDuskHlbspToolchainService.EnsureProvisioned(
                    AppContext.BaseDirectory,
                    managedDataRoot);

            compileContext =
                new CompanionDuskCompilationProfileContext(
                    CompanionDuskCompileMode.HalfLifeBsp,
                    halfLifeToolchain,
                    companionExecutablePath,
                    duskPalettePath,
                    wadLibraryDirectory);
        }

        CompanionTrenchBroomCompilationProfileResult result =
            CompanionTrenchBroomCompilationProfileService.EnsureDuskProfile(
                _installation.ExecutablePath,
                stableToolchain,
                runtimeModDirectory,
                buildSettings,
                compileContext);

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

        string compileModeText =
            modeDecision.UsesHalfLifeBsp
                ? $"WAD3/Half-Life BSP mode with ericw-tools {halfLifeToolchain!.Version}"
                : $"Quake BSP mode with ericw-tools {stableToolchain.Version}";

        StatusText.Text =
            $"DUSK tooling ready - {compileModeText}, compile profile '{result.ProfileName}', and launch profile '{engineProfile.ProfileName}' are configured.";
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

            TryPrepareCurrentProjectReadiness();

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

    private void TryPrepareCurrentProjectReadiness()
    {
        _projectReadinessPreparationProblem =
            null;

        if (_projectSession is null)
        {
            return;
        }

        try
        {
            ResolveTrenchBroomInstallation();

            if (!string.Equals(
                    _projectSession.Project.GameId,
                    CompanionGameProfiles.Dusk.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_installation is null ||
                !_installation.IsValid ||
                !_installation.IsWadForgeCompatible ||
                !IsManagedTrenchBroom())
            {
                return;
            }

            string managedDataRoot =
                CompanionManagedDataRootService.GetRequiredRoot(
                    _settings);

            CompanionTrenchBroomGameConfigService.EnsureDuskGameConfig(
                _installation.ExecutablePath);

            CompanionDuskTrenchBroomEnvironmentService.Ensure(
                _installation.ExecutablePath,
                managedDataRoot);

            CompanionEricwToolchainService.EnsureProvisioned(
                AppContext.BaseDirectory,
                managedDataRoot);

            CompanionDuskAuthoringResourceStatus authoringStatus =
                CompanionDuskAuthoringResourceService.GetStatus(
                    _installation.ExecutablePath);

            if (!authoringStatus.IsReady)
            {
                return;
            }

            string? activeMapPath =
                null;

            if (!string.IsNullOrWhiteSpace(
                    _projectSession.Project.ActiveMapPath))
            {
                try
                {
                    activeMapPath =
                        _projectSession.GetActiveMapFullPath();
                }
                catch
                {
                    activeMapPath =
                        null;
                }
            }

            if (!string.IsNullOrWhiteSpace(
                    activeMapPath) &&
                File.Exists(
                    activeMapPath))
            {
                PrepareDuskCompilerProfile();
            }
        }
        catch (Exception exception)
        {
            _projectReadinessPreparationProblem =
                exception.Message;
        }
    }

    private ProjectReadinessSummary EvaluateProjectReadiness()
    {
        if (_projectSession is null)
        {
            return new ProjectReadinessSummary(
                true,
                "Companion ready",
                "Create or open a project to begin.");
        }

        CompanionGameProfile gameProfile;

        try
        {
            gameProfile =
                CompanionGameProfiles.GetRequired(
                    _projectSession.Project.GameId);
        }
        catch (Exception exception)
        {
            return new ProjectReadinessSummary(
                false,
                "Setup needs attention",
                exception.Message);
        }

        if (string.IsNullOrWhiteSpace(
                _gameInstallationDirectory) ||
            !_gameInstallationLocator.IsInstallationDirectory(
                gameProfile,
                _gameInstallationDirectory))
        {
            return new ProjectReadinessSummary(
                false,
                "Game setup needed",
                $"Companion needs a valid {gameProfile.DisplayName} installation for this project.");
        }

        if (_installation is null ||
            !_installation.IsValid ||
            !File.Exists(
                _installation.ExecutablePath))
        {
            return new ProjectReadinessSummary(
                false,
                "TrenchBroom setup needed",
                "Open Settings and set up a valid TrenchBroom installation.");
        }

        bool isDusk =
            string.Equals(
                gameProfile.Id,
                CompanionGameProfiles.Dusk.Id,
                StringComparison.OrdinalIgnoreCase);

        if (isDusk)
        {
            if (!_installation.IsWadForgeCompatible ||
                !IsManagedTrenchBroom())
            {
                return new ProjectReadinessSummary(
                    false,
                    "TrenchBroom setup needed",
                    "DUSK projects require the Companion-managed compatible TrenchBroom build.");
            }

            if (!string.IsNullOrWhiteSpace(
                    _projectReadinessPreparationProblem))
            {
                return new ProjectReadinessSummary(
                    false,
                    "Setup needs attention",
                    _projectReadinessPreparationProblem);
            }

            try
            {
                string managedDataRoot =
                    CompanionManagedDataRootService.GetRequiredRoot(
                        _settings);

                CompanionEricwToolchainStatus compilerStatus =
                    CompanionEricwToolchainService.GetStatus(
                        managedDataRoot);

                if (!compilerStatus.IsReady)
                {
                    return new ProjectReadinessSummary(
                        false,
                        "Compiler setup needed",
                        compilerStatus.Problem ??
                        "The managed DUSK compiler is not ready.");
                }

                CompanionDuskAuthoringResourceStatus authoringStatus =
                    CompanionDuskAuthoringResourceService.GetStatus(
                        _installation.ExecutablePath);

                if (!authoringStatus.IsReady)
                {
                    return new ProjectReadinessSummary(
                        false,
                        "DUSK resources needed",
                        "Open a map in TrenchBroom and Companion will guide you to the DUSK mapping resource folder.");
                }
            }
            catch (Exception exception)
            {
                return new ProjectReadinessSummary(
                    false,
                    "Setup needs attention",
                    exception.Message);
            }
        }
        else if (!string.IsNullOrWhiteSpace(
                     _projectReadinessPreparationProblem))
        {
            return new ProjectReadinessSummary(
                false,
                "Setup needs attention",
                _projectReadinessPreparationProblem);
        }

        return new ProjectReadinessSummary(
            true,
            "Project ready",
            $"{gameProfile.DisplayName}, TrenchBroom, and this project's managed workspace are ready.");
    }

    private void RefreshProjectReadinessIndicator()
    {
        ProjectReadinessSummary readiness =
            EvaluateProjectReadiness();

        ProjectReadinessText.Text =
            readiness.BadgeText;

        ProjectReadinessBorder.ToolTip =
            readiness.Detail;

        if (readiness.IsReady)
        {
            ProjectReadinessBorder.Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        0x17,
                        0x26,
                        0x1F));

            ProjectReadinessBorder.BorderBrush =
                new SolidColorBrush(
                    Color.FromRgb(
                        0x31,
                        0x5D,
                        0x46));

            ProjectReadinessDot.Fill =
                new SolidColorBrush(
                    Color.FromRgb(
                        0x6F,
                        0xD1,
                        0x9B));

            ProjectReadinessText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        0xBD,
                        0xEE,
                        0xD2));

            return;
        }

        ProjectReadinessBorder.Background =
            new SolidColorBrush(
                Color.FromRgb(
                    0x2A,
                    0x23,
                    0x15));

        ProjectReadinessBorder.BorderBrush =
            new SolidColorBrush(
                Color.FromRgb(
                    0x6C,
                    0x57,
                    0x30));

        ProjectReadinessDot.Fill =
            new SolidColorBrush(
                Color.FromRgb(
                    0xE5,
                    0xB8,
                    0x5C));

        ProjectReadinessText.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    0xF5,
                    0xDD,
                    0xA8));
    }

    private sealed record ProjectReadinessSummary(
        bool IsReady,
        string BadgeText,
        string Detail);

    private void RefreshProjectInterface()
    {
        EnsureProjectControls();
        RefreshRecentProjects();

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

            ProjectWorkflowPanel.Visibility =
                Visibility.Collapsed;

            ProjectNextStepTitleText.Text =
                string.Empty;

            ProjectNextStepDetailText.Text =
                string.Empty;

            RefreshProjectReadinessIndicator();

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

        RefreshMapList(
            activeMapFullPath);

        RefreshProjectGuidance(
            project,
            activeMapFullPath);

        RefreshProjectReadinessIndicator();
    }

    private void RefreshProjectGuidance(
        CompanionProjectManifest project,
        string? activeMapFullPath)
    {
        ArgumentNullException.ThrowIfNull(
            project);

        string? effectiveMapPath =
            activeMapFullPath;

        if ((string.IsNullOrWhiteSpace(
                 effectiveMapPath) ||
             !File.Exists(
                 effectiveMapPath)) &&
            ProjectMapListBox.SelectedItem is
                CompanionMapChoice selectedMap &&
            File.Exists(
                selectedMap.FullPath))
        {
            effectiveMapPath =
                selectedMap.FullPath;
        }

        bool hasMap =
            !string.IsNullOrWhiteSpace(
                effectiveMapPath) &&
            File.Exists(
                effectiveMapPath);

        ProjectWorkflowPanel.Visibility =
            hasMap
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (!hasMap)
        {
            ProjectNextStepTitleText.Text =
                string.Empty;

            ProjectNextStepDetailText.Text =
                string.Empty;

            return;
        }

        string confirmedMapPath =
            effectiveMapPath!;

        string mapName =
            Path.GetFileNameWithoutExtension(
                confirmedMapPath) ??
            Path.GetFileName(
                confirmedMapPath) ??
            "Map";

        int selectedWadCount =
            0;

        try
        {
            if (_projectSession is not null)
            {
                selectedWadCount =
                    _projectWadSelectionService
                        .GetMap(
                            _projectSession,
                            confirmedMapPath)
                        .WadAssetIds
                        .Count;
            }
        }
        catch
        {
            selectedWadCount =
                0;
        }

        ProjectNextStepTitleText.Text =
            $"Open {mapName} in TrenchBroom";

        bool isDusk =
            string.Equals(
                project.GameId,
                CompanionGameProfiles.Dusk.Id,
                StringComparison.OrdinalIgnoreCase);

        if (isDusk)
        {
            ProjectNextStepDetailText.Text =
                selectedWadCount switch
                {
                    0 =>
                        "No library WADs are selected for this map. Choose Map WADs now or start mapping without project textures.",

                    1 =>
                        "1 library WAD is selected for this map and will be synchronized with TrenchBroom.",

                    _ =>
                        $"{selectedWadCount:N0} library WADs are selected for this map and will be synchronized with TrenchBroom."
                };

            CompileSettingsButton.ToolTip =
                "Configure compiler options for this DUSK project";
        }
        else
        {
            ProjectNextStepDetailText.Text =
                selectedWadCount == 0
                    ? "No library WADs are selected for this map. Choose Map WADs whenever you need project textures."
                    : selectedWadCount == 1
                        ? "1 library WAD is selected for this map."
                        : $"{selectedWadCount:N0} library WADs are selected for this map.";

            CompileSettingsButton.ToolTip =
                "Compile Settings are available for DUSK projects.";
        }

        ManageAssetsButton.ToolTip =
            selectedWadCount == 0
                ? $"Choose WADs for {mapName}"
                : selectedWadCount == 1
                    ? $"Manage 1 WAD selected for {mapName}"
                    : $"Manage {selectedWadCount:N0} WADs selected for {mapName}";

        OpenCurrentMapButton.ToolTip =
            $"Open {mapName} in TrenchBroom";
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

                ProjectMapListBox.Visibility =
                    Visibility.Collapsed;

                EmptyProjectMapsPanel.Visibility =
                    Visibility.Collapsed;

                EmptyCreateFirstMapButton.IsEnabled =
                    false;

                EmptyAddExistingMapButton.IsEnabled =
                    false;

                NewMapButton.Visibility =
                    Visibility.Visible;

                ImportMapButton.Visibility =
                    Visibility.Visible;

                MapsHelpText.Text =
                    "Choose a map to work on, or add another map to this project.";

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

            bool hasMaps =
                ProjectMapListBox.Items.Count > 0;

            ProjectMapListBox.IsEnabled =
                hasMaps;

            ProjectMapListBox.Visibility =
                hasMaps
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            EmptyProjectMapsPanel.Visibility =
                hasMaps
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            EmptyCreateFirstMapButton.IsEnabled =
                !hasMaps;

            EmptyAddExistingMapButton.IsEnabled =
                !hasMaps;

            NewMapButton.Visibility =
                hasMaps
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            ImportMapButton.Visibility =
                hasMaps
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            MapsHelpText.Text =
                hasMaps
                    ? "Choose a map to work on, or add another map to this project."
                    : "Start this project by creating a map or adding an existing .map file.";
        }
        finally
        {
            _refreshingMapList =
                false;
        }
    }
    private sealed class RecentProjectChoice
    {
        public RecentProjectChoice(
            string displayName,
            string gameDisplayName,
            string directoryPath)
        {
            DisplayName =
                displayName;

            GameDisplayName =
                gameDisplayName;

            DirectoryPath =
                directoryPath;
        }

        public string DisplayName { get; }

        public string GameDisplayName { get; }

        public string DirectoryPath { get; }
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