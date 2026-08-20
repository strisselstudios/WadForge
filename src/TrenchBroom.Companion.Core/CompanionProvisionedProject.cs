namespace TrenchBroom.Companion.Core;

public sealed class CompanionProvisionedProject
{
    internal CompanionProvisionedProject(
        CompanionGameProfile gameProfile,
        string projectName,
        string projectDirectoryName,
        string workspaceRoot,
        string projectDirectory,
        string sourceMapsDirectory,
        string wadsDirectory,
        string skyboxesDirectory,
        string buildDirectory,
        string backupsDirectory,
        string gameInstallationDirectory,
        string runtimeModDirectory,
        string runtimeMapsDirectory)
    {
        GameProfile =
            gameProfile;

        ProjectName =
            projectName;

        ProjectDirectoryName =
            projectDirectoryName;

        WorkspaceRoot =
            workspaceRoot;

        ProjectDirectory =
            projectDirectory;

        SourceMapsDirectory =
            sourceMapsDirectory;

        WadsDirectory =
            wadsDirectory;

        SkyboxesDirectory =
            skyboxesDirectory;

        BuildDirectory =
            buildDirectory;

        BackupsDirectory =
            backupsDirectory;

        GameInstallationDirectory =
            gameInstallationDirectory;

        RuntimeModDirectory =
            runtimeModDirectory;

        RuntimeMapsDirectory =
            runtimeMapsDirectory;
    }

    public CompanionGameProfile GameProfile { get; }

    public string ProjectName { get; }

    public string ProjectDirectoryName { get; }

    public string WorkspaceRoot { get; }

    public string ProjectDirectory { get; }

    public string SourceMapsDirectory { get; }

    public string WadsDirectory { get; }

    public string SkyboxesDirectory { get; }

    public string BuildDirectory { get; }

    public string BackupsDirectory { get; }

    public string GameInstallationDirectory { get; }

    public string RuntimeModDirectory { get; }

    public string RuntimeMapsDirectory { get; }
}
