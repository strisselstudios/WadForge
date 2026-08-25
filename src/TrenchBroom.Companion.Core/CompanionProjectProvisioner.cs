using System.IO;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionProjectProvisioner
{
    private readonly CompanionProjectLayout _layout;

    public CompanionProjectProvisioner()
        : this(
            new CompanionProjectLayout())
    {
    }

    public CompanionProjectProvisioner(
        CompanionProjectLayout layout)
    {
        _layout =
            layout ??
            throw new ArgumentNullException(
                nameof(layout));
    }

    public CompanionProvisionedProject Provision(
        CompanionGameProfile gameProfile,
        string selectedDrivePath,
        string gameInstallationDirectory,
        string projectName)
    {
        string workspaceRoot =
            _layout.GetWorkspaceRootForDrive(
                selectedDrivePath);

        return ProvisionAtWorkspaceRoot(
            gameProfile,
            workspaceRoot,
            gameInstallationDirectory,
            projectName);
    }

    public CompanionProvisionedProject ProvisionAtWorkspaceRoot(
        CompanionGameProfile gameProfile,
        string workspaceRoot,
        string gameInstallationDirectory,
        string projectName)
    {
        ArgumentNullException.ThrowIfNull(
            gameProfile);

        if (string.IsNullOrWhiteSpace(
                workspaceRoot))
        {
            throw new ArgumentException(
                "Workspace root cannot be empty.",
                nameof(workspaceRoot));
        }

        if (string.IsNullOrWhiteSpace(
                gameInstallationDirectory))
        {
            throw new ArgumentException(
                "Game installation directory cannot be empty.",
                nameof(gameInstallationDirectory));
        }

        string installationDirectory =
            Path.GetFullPath(
                gameInstallationDirectory.Trim());

        if (!Directory.Exists(
                installationDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Game installation directory was not found: '{installationDirectory}'.");
        }

        string normalizedWorkspaceRoot =
            Path.GetFullPath(
                workspaceRoot.Trim());

        string projectDirectoryName =
            CompanionProjectLayout
                .SanitizeProjectDirectoryName(
                    projectName);

        string projectDirectory =
            _layout.GetProjectDirectory(
                normalizedWorkspaceRoot,
                projectName);

        if (Directory.Exists(
                projectDirectory))
        {
            throw new IOException(
                $"A Companion project directory already exists at '{projectDirectory}'.");
        }

        string sourceMapsDirectory =
            Path.Combine(
                projectDirectory,
                CompanionProjectLayout.MapsDirectoryName);

        string wadsDirectory =
            Path.Combine(
                projectDirectory,
                CompanionProjectLayout.WadsDirectoryName);

        string skyboxesDirectory =
            Path.Combine(
                projectDirectory,
                CompanionProjectLayout.SkyboxesDirectoryName);

        string buildDirectory =
            Path.Combine(
                projectDirectory,
                CompanionProjectLayout.BuildDirectoryName);

        string backupsDirectory =
            Path.Combine(
                projectDirectory,
                CompanionProjectLayout.BackupsDirectoryName);

        string runtimeModDirectory =
            _layout.GetRuntimeModDirectory(
                gameProfile,
                installationDirectory,
                projectName);

        string runtimeMapsDirectory =
            Path.Combine(
                runtimeModDirectory,
                CompanionProjectLayout.MapsDirectoryName);

        Directory.CreateDirectory(
            normalizedWorkspaceRoot);

        Directory.CreateDirectory(
            projectDirectory);

        Directory.CreateDirectory(
            sourceMapsDirectory);


        Directory.CreateDirectory(
            skyboxesDirectory);

        Directory.CreateDirectory(
            buildDirectory);

        Directory.CreateDirectory(
            backupsDirectory);

        Directory.CreateDirectory(
            runtimeMapsDirectory);

        return new CompanionProvisionedProject(
            gameProfile,
            projectName.Trim(),
            projectDirectoryName,
            normalizedWorkspaceRoot,
            projectDirectory,
            sourceMapsDirectory,
            wadsDirectory,
            skyboxesDirectory,
            buildDirectory,
            backupsDirectory,
            installationDirectory,
            runtimeModDirectory,
            runtimeMapsDirectory);
    }
}
