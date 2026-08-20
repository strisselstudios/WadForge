using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TrenchBroom.Companion.Core;

namespace TrenchBroom.Companion.App;

public partial class MainWindow
{
    private readonly CompanionProjectManager _projectManager =
        new();

    private CompanionProjectSession? _projectSession;

    private void NewProject_Click(
        object sender,
        RoutedEventArgs e)
    {
        string gameId =
            GetSelectedProjectGameId();

        SaveFileDialog dialog = new()
        {
            Title =
                "Create TrenchBroom Companion Project",

            Filter =
                "TrenchBroom Companion projects|*.tbproject|" +
                "All files|*.*",

            DefaultExt =
                CompanionProjectStore.ProjectExtension,

            AddExtension =
                true,

            OverwritePrompt =
                false,

            FileName =
                "NewProject.tbproject"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string? directory =
            Path.GetDirectoryName(
                dialog.FileName);

        if (string.IsNullOrWhiteSpace(directory))
        {
            ShowProjectError(
                "The selected project location is invalid.");

            return;
        }

        string projectName =
            Path.GetFileNameWithoutExtension(
                dialog.FileName);

        try
        {
            _projectSession =
                _projectManager.Create(
                    directory,
                    projectName,
                    gameId);

            Directory.CreateDirectory(
                Path.Combine(
                    _projectSession.ProjectDirectory,
                    "maps"));

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
        OpenFileDialog dialog = new()
        {
            Title =
                "Open TrenchBroom Companion Project",

            Filter =
                "TrenchBroom Companion projects|*.tbproject|" +
                "All files|*.*",

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

            RefreshProjectInterface();

            StatusText.Text =
                $"Opened project '{_projectSession.Project.Name}'.";
        }
        catch (Exception exception)
        {
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

        RefreshProjectInterface();

        StatusText.Text =
            $"Closed project '{projectName}'.";
    }

    private void ImportMap_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_projectSession is null)
        {
            return;
        }

        OpenFileDialog dialog = new()
        {
            Title =
                "Import Existing Map",

            Filter =
                "TrenchBroom map files|*.map|" +
                "All files|*.*",

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
            string importedMapPath =
                ImportMapIntoCurrentProject(
                    dialog.FileName);

            RefreshProjectInterface();

            StatusText.Text =
                $"Imported '{Path.GetFileName(importedMapPath)}'.";
        }
        catch (Exception exception)
        {
            ShowProjectError(
                exception.Message);
        }
    }

    private string ImportMapIntoCurrentProject(
        string sourceMapPath)
    {
        if (_projectSession is null)
        {
            throw new InvalidOperationException(
                "Open a project before importing a map.");
        }

        string fullSourcePath =
            Path.GetFullPath(
                sourceMapPath);

        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException(
                "The selected map file no longer exists.",
                fullSourcePath);
        }

        if (!string.Equals(
                Path.GetExtension(fullSourcePath),
                ".map",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Only .map files can be imported.");
        }

        string mapsDirectory =
            Path.Combine(
                _projectSession.ProjectDirectory,
                "maps");

        Directory.CreateDirectory(
            mapsDirectory);

        string destinationPath =
            Path.Combine(
                mapsDirectory,
                Path.GetFileName(fullSourcePath));

        bool sameFile =
            string.Equals(
                fullSourcePath,
                Path.GetFullPath(destinationPath),
                StringComparison.OrdinalIgnoreCase);

        if (!sameFile)
        {
            if (File.Exists(destinationPath))
            {
                throw new IOException(
                    $"A map named '{Path.GetFileName(destinationPath)}' " +
                    "already exists in this project.");
            }

            File.Copy(
                fullSourcePath,
                destinationPath,
                overwrite: false);
        }

        _projectSession.AddMap(
            destinationPath,
            makeActive: true);

        _projectSession.Save();

        return destinationPath;
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

        Process.Start(
            new ProcessStartInfo
            {
                FileName =
                    _projectSession.ProjectDirectory,

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

        if (string.IsNullOrWhiteSpace(activeMapPath) ||
            !File.Exists(activeMapPath))
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

        startInfo.ArgumentList.Add(
            Path.GetFullPath(
                activeMapPath));

        Process.Start(
            startInfo);

        StatusText.Text =
            $"Opened '{Path.GetFileName(activeMapPath)}' in TrenchBroom.";
    }

    private string GetSelectedProjectGameId()
    {
        ComboBoxItem? selected =
            ProjectGameComboBox.SelectedItem
            as ComboBoxItem;

        string? gameId =
            selected?.Tag
            as string;

        return string.IsNullOrWhiteSpace(gameId)
            ? "dusk"
            : gameId;
    }

    private void SelectProjectGame(
        string gameId)
    {
        foreach (object item in
                 ProjectGameComboBox.Items)
        {
            if (item is not ComboBoxItem comboItem)
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

            OpenProjectFolderButton.IsEnabled =
                false;

            OpenCurrentMapButton.IsEnabled =
                false;

            return;
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

        string currentMapText =
            "None";

        string? activeMapFullPath =
            null;

        if (!string.IsNullOrWhiteSpace(
                project.ActiveMapPath))
        {
            CompanionProjectMap? activeMap =
                project.Maps.FirstOrDefault(
                    map =>
                        string.Equals(
                            map.Path,
                            project.ActiveMapPath,
                            StringComparison.OrdinalIgnoreCase));

            currentMapText =
                activeMap?.DisplayName ??
                project.ActiveMapPath;

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
        }

        ProjectNameText.Text =
            project.Name;

        ProjectDetailsText.Text =
            $"{game} | {mapCountText} | Current map: {currentMapText}";

        ProjectLocationText.Text =
            _projectSession.ProjectFilePath;

        ProjectLocationText.ToolTip =
            _projectSession.ProjectFilePath;

        SelectProjectGame(
            project.GameId);

        ProjectGameComboBox.IsEnabled =
            false;

        CloseProjectButton.IsEnabled =
            true;

        ImportMapButton.IsEnabled =
            true;

        OpenProjectFolderButton.IsEnabled =
            Directory.Exists(
                _projectSession.ProjectDirectory);

        OpenCurrentMapButton.IsEnabled =
            !string.IsNullOrWhiteSpace(activeMapFullPath) &&
            File.Exists(activeMapFullPath);
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
