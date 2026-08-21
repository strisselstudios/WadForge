using System;
using System.Windows;
using System.Windows.Controls;
using TrenchBroom.Companion.Core;

namespace TrenchBroom.Companion.App;

public partial class MainWindow
{
    private const string ProjectsSection = "Projects";
    private const string ProjectSection = "Project";
    private const string AssetsSection = "Assets";
    private const string ToolsSection = "Tools";
    private const string SettingsSection = "Settings";

    private string _activeShellSection = ProjectsSection;

    private void Window_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        string initialSection =
            _projectSession is null
                ? ProjectsSection
                : ProjectSection;

        ShowShellSection(
            initialSection);

        RefreshShellContext();
    }

    private void ShellNavigation_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not string section)
        {
            return;
        }

        if (string.Equals(
                section,
                ProjectSection,
                StringComparison.Ordinal) &&
            _projectSession is null)
        {
            return;
        }

        ShowShellSection(
            section);
    }

    private void ShellNewProject_Click(
        object sender,
        RoutedEventArgs e)
    {
        NewProject_Click(
            sender,
            e);

        if (_projectSession is not null)
        {
            ShowShellSection(
                ProjectSection);
        }

        RefreshShellContext();
    }

    private void ShellOpenProject_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenProject_Click(
            sender,
            e);

        if (_projectSession is not null)
        {
            ShowShellSection(
                ProjectSection);
        }

        RefreshShellContext();
    }

    private void ShellCloseProject_Click(
        object sender,
        RoutedEventArgs e)
    {
        CloseProject_Click(
            sender,
            e);

        ShowShellSection(
            ProjectsSection);

        RefreshShellContext();
    }

    private void ShowShellSection(
        string section)
    {
        if (!IsKnownShellSection(
                section))
        {
            section =
                ProjectsSection;
        }

        if (string.Equals(
                section,
                ProjectSection,
                StringComparison.Ordinal) &&
            _projectSession is null)
        {
            section =
                ProjectsSection;
        }

        _activeShellSection =
            section;

        ProjectsView.Visibility =
            IsShellSection(
                ProjectsSection)
                ? Visibility.Visible
                : Visibility.Collapsed;

        ProjectView.Visibility =
            IsShellSection(
                ProjectSection)
                ? Visibility.Visible
                : Visibility.Collapsed;

        AssetsView.Visibility =
            IsShellSection(
                AssetsSection)
                ? Visibility.Visible
                : Visibility.Collapsed;

        ToolsView.Visibility =
            IsShellSection(
                ToolsSection)
                ? Visibility.Visible
                : Visibility.Collapsed;

        SettingsView.Visibility =
            IsShellSection(
                SettingsSection)
                ? Visibility.Visible
                : Visibility.Collapsed;

        NavProjectButton.IsEnabled =
            _projectSession is not null;

        SetNavigationStyle(
            NavProjectsButton,
            ProjectsSection);

        SetNavigationStyle(
            NavProjectButton,
            ProjectSection);

        SetNavigationStyle(
            NavAssetsButton,
            AssetsSection);

        SetNavigationStyle(
            NavToolsButton,
            ToolsSection);

        SetNavigationStyle(
            NavSettingsButton,
            SettingsSection);

        RefreshShellContext();
    }

    private bool IsShellSection(
        string section)
    {
        return string.Equals(
            _activeShellSection,
            section,
            StringComparison.Ordinal);
    }

    private static bool IsKnownShellSection(
        string section)
    {
        return
            string.Equals(
                section,
                ProjectsSection,
                StringComparison.Ordinal) ||
            string.Equals(
                section,
                ProjectSection,
                StringComparison.Ordinal) ||
            string.Equals(
                section,
                AssetsSection,
                StringComparison.Ordinal) ||
            string.Equals(
                section,
                ToolsSection,
                StringComparison.Ordinal) ||
            string.Equals(
                section,
                SettingsSection,
                StringComparison.Ordinal);
    }

    private void SetNavigationStyle(
        Button button,
        string section)
    {
        string resourceKey =
            IsShellSection(
                section)
                ? "SidebarButtonSelectedStyle"
                : "SidebarButtonStyle";

        button.Style =
            FindResource(
                resourceKey)
            as Style;
    }

    private void RefreshShellContext()
    {
        bool hasProject =
            _projectSession is not null;

        NavProjectButton.IsEnabled =
            hasProject;

        if (!hasProject)
        {
            ShellContextText.Text =
                "No project open";

            ShellSidebarProjectText.Text =
                "No project";

            ShellSidebarGameText.Text =
                "Choose a project to begin";

            return;
        }

        string projectName =
            _projectSession!.Project.Name;

        string gameDisplayName;

        try
        {
            gameDisplayName =
                CompanionGameProfiles
                    .GetRequired(
                        _projectSession.Project.GameId)
                    .DisplayName;
        }
        catch
        {
            gameDisplayName =
                _projectSession.Project.GameId;
        }

        ShellContextText.Text =
            projectName + "  •  " + gameDisplayName;

        ShellSidebarProjectText.Text =
            projectName;

        ShellSidebarGameText.Text =
            gameDisplayName;
    }
}
